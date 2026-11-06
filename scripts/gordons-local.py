#!/usr/bin/env python3
"""Operate only the isolated Gordons Docker context, never a remote daemon."""

import argparse
import json
import os
from pathlib import Path
import secrets
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[1]
STATE = ROOT / ".gordons-local"
ENV_FILE = STATE / "environment.env"
CONTEXT = "colima-gordons-booking"
PROJECT = "gordons-booking-local"


def initialize():
    if STATE.is_symlink() or ENV_FILE.is_symlink():
        raise ValueError("Die lokale Zugangsablage darf kein symbolischer Link sein.")
    STATE.mkdir(mode=0o700, exist_ok=True)
    STATE.chmod(0o700)
    if not ENV_FILE.exists():
        fd = os.open(ENV_FILE, os.O_CREAT | os.O_EXCL | os.O_WRONLY, 0o600)
        with os.fdopen(fd, "w") as handle:
            handle.write("# Nur lokale Testzugänge; niemals veröffentlichen.\n")
            handle.write("JWT_KEY=" + secrets.token_hex(48) + "\n")
            handle.write("ADMIN_PASSWORD=" + secrets.token_urlsafe(32) + "\n")
    ENV_FILE.chmod(0o600)


def host_environment():
    # Shell variables override Compose env files. Do not inherit credentials or
    # Docker/Compose overrides from another project or a production shell.
    return {key: value for key, value in os.environ.items()
            if key in {"PATH", "HOME", "USER", "LOGNAME", "TMPDIR", "LANG", "LC_ALL", "TERM"}}


def docker(*args, capture=False):
    return subprocess.run(["docker", *args], cwd=ROOT, env=host_environment(),
                          check=True, text=True,
                          stdout=subprocess.PIPE if capture else None)


def compose_command(source):
    result = ["--context", CONTEXT, "compose", "--project-name", PROJECT,
              "--env-file", str(ENV_FILE), "-f", "compose.gordons.local.yml"]
    if source:
        result += ["-f", "compose.gordons.build.yml"]
    return result


def check_configuration(source):
    contexts = json.loads(docker("context", "inspect", CONTEXT, capture=True).stdout)
    expected = "unix://" + str(Path.home() / ".colima/gordons-booking/docker.sock")
    if contexts[0]["Endpoints"]["docker"]["Host"] != expected:
        raise ValueError("Abbruch: Docker-Ziel ist nicht die lokale Gordons-Umgebung.")
    config = json.loads(docker(*compose_command(source), "config", "--format", "json", capture=True).stdout)
    if config["name"] != PROJECT:
        raise ValueError("Unerwarteter Projektname.")
    networks = config["networks"]
    if (set(networks) != {"isolated", "local_entry"}
            or not networks["isolated"].get("internal")
            or any(network.get("external") for network in networks.values())):
        raise ValueError("Abbruch: Unerwartete Netztrennung.")
    if any(volume.get("external") for volume in config["volumes"].values()):
        raise ValueError("Abbruch: Fremde Datenvolumes sind nicht erlaubt.")
    for name, service in config["services"].items():
        expected_networks = {"isolated", "local_entry"} if name == "local-gateway" else {"isolated"}
        if service.get("network_mode") or set(service.get("networks", {})) != expected_networks:
            raise ValueError("Abbruch: Abweichendes Containernetz.")
        if service.get("privileged") or service.get("devices"):
            raise ValueError("Abbruch: Erweiterte Hostrechte sind nicht erlaubt.")
        if any(port.get("host_ip") != "127.0.0.1" for port in service.get("ports", [])):
            raise ValueError("Abbruch: Testports müssen auf diesem Mac bleiben.")
        if any(volume["type"] != "volume" for volume in service.get("volumes", [])):
            raise ValueError("Abbruch: Hostverzeichnisse werden nicht eingebunden.")
    backend = config["services"]["backend"]["environment"]
    if (backend["Admin__Email"] != "admin@gordons.test"
            or backend["Admin__Password"] != backend["ADMIN_PASSWORD"]
            or backend["Jwt__Key"] != backend["JWT_KEY"]):
        raise ValueError("Abbruch: Lokale Zugangsdaten müssen Entwicklungsvorgaben überschreiben.")
    if backend["CONNECTION_STRING"] != "Data Source=/data/openresto.db":
        raise ValueError("Unerwarteter Datenbankpfad.")
    if backend["WEBSITE_URL"] != "http://localhost:5062":
        raise ValueError("Unerwartetes API-/Webziel.")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("action", choices=["init", "check", "up", "build", "stop", "status"])
    args = parser.parse_args()
    initialize()
    if args.action == "init":
        print("Lokale Zugangsdaten geschützt angelegt:", ENV_FILE)
        return
    source = args.action == "build"
    check_configuration(source)
    command = compose_command(source)
    if args.action == "check":
        print("Lokales Docker-Ziel, Netz, Ports und Datenvolumes geprüft.")
    elif args.action in {"up", "build"}:
        docker(*command, "up", "--detach", "--wait", "--wait-timeout", "180",
               *(["--build"] if source else []))
        print("Testseite: http://localhost:5062 | Testpostfach: http://localhost:8025")
    elif args.action == "stop":
        # No down -v: stopping must preserve the synthetic test database.
        docker(*command, "stop")
    else:
        docker(*command, "ps")


if __name__ == "__main__":
    try:
        main()
    except (ValueError, OSError, subprocess.CalledProcessError) as error:
        print("Lokaler Start abgebrochen:", error, file=sys.stderr)
        sys.exit(1)
