# Gordons Booking: lokale Entwicklung

Eigene Entwicklung auf OpenResto **v2.5.0**, Commit
`bd004c6cf5327d53ee2f56ab5fa6bd868d6baa31`, unter MIT. Die Lizenz und
Urheberhinweise bleiben erhalten. Der Zweig `gordons` ist die eigene Basis;
`main` im GitHub-Fork bleibt zunächst die beim Forken übernommene Originalhistorie.

## Lokale Testumgebung auf dem Mac

Voraussetzungen: Python 3, Docker CLI mit Compose und Colima. Ein eigenes
Colima-Profil verwendet 2 CPUs, 4 GiB RAM und einen 16-GiB-Datenträger.
Es startet nicht automatisch bei der Anmeldung und bindet keine Mac-Verzeichnisse ein.

```sh
colima start gordons-booking --cpus 2 --memory 4 --disk 16 --mount none --activate=false --vm-type vz --runtime docker
python3 scripts/gordons-local.py init
python3 scripts/gordons-local.py up
```

- Testseite: <http://localhost:5062>
- Admin-Anmeldung: `admin@gordons.test`; das zufällige lokale Passwort steht in
  `.gordons-local/environment.env`. Nicht in Git übernehmen oder im Chat posten.
- Testpostfach: <http://localhost:8025>. Im lokalen Adminbereich als SMTP-Ziel
  `mailpit`, Port `1025`, Benutzer/Passwort jeweils `local`, SSL aus verwenden.
  Die Anwendung speichert SMTP-Einstellungen in der Datenbank; ein Container-
  Umgebungswert allein richtet den Versand hier nicht ein.

Zunächst laufen die Originalimages 2.5.0 als Vergleichsbasis. Eigene Änderungen
werden anschließend aus diesem Checkout gebaut:

```sh
python3 scripts/gordons-local.py build
python3 scripts/gordons-local.py status
python3 scripts/gordons-local.py stop
colima stop gordons-booking
```

`up` wählt bewusst wieder die Vergleichsimages; `build` wählt die eigenen
Source-Images. Beide verwenden dieselben lokalen Testvolumes. Nach einer eigenen
inkompatiblen Migration nicht mit `up` zur alten Version zurückwechseln; dann eine
gesonderte lokale Testdatenbank verwenden oder den geprüften Rückfallpfad anwenden.
`stop` stoppt die laufenden Dienste und löscht keine Daten. Der Colima-Stopp gibt
den reservierten Arbeitsspeicher frei.

## Trennung und Grenzen

Das Startskript akzeptiert ausschließlich den lokalen Docker-Socket des eigenen
Colima-Profils. Es prüft Netz, Ports und Volumes und übernimmt keine Docker-,
Compose- oder Zugangsdatenvariablen aus einer anderen Shell-Umgebung.
Backend, Frontend und Mailpit verwenden ein internes Netz ohne ausgehende
Internetverbindung. Ein zusätzlicher Zugangsproxy besitzt ein zweites Netz,
damit Docker die beiden Webports an `127.0.0.1` veröffentlichen kann. Dieser
Proxy hat weder Datenvolumes noch Zugangsdaten und leitet nur an die beiden
festen internen Webdienste weiter. E-Mails bleiben im lokalen
Mailpit. Placetel, Push-Zugänge und Produktionsschlüssel werden nicht eingerichtet.
Paket- und Imageabrufe während des Bauens laufen außerhalb dieses Laufzeitnetzes.
Die Testoberfläche kann weiterhin externe Links/Medien im Browser enthalten;
keine Produktionsadressen als API-Ziel oder echte Gastdaten verwenden.

Die Datenbank und Medien sind neue Docker-Volumes dieses Projekts. Es wird keine
Produktionssicherung importiert. Die anfangs sichtbaren Restaurants sind die
synthetischen OpenResto-Beispiele. Eine eigene Testbelegung für Gordons wird
später ausschließlich aus erfundenen Gästen und abgestimmten Tischdaten erstellt.

## Buchungsregressionen lokal prüfen

Die HTTP-Regressionen für Tischkombinationen, Verlängerung, Sammelverlängerung,
Wiederherstellen und Status-Undo verwenden jeweils eine neue SQLite-Datei mit
eigenständigen Verbindungen. Der Testcontainer erhält keine Datenvolumes, keine
Zugangsdaten aus der Testinstallation und kein Netzwerk:

```sh
docker --context colima-gordons-booking build -f dev/gordons-tests/Dockerfile -t gordons-booking-tests:local .
docker --context colima-gordons-booking run --rm --network none gordons-booking-tests:local --filter FullyQualifiedName~BookingOccupancyGuardsTests
```

Ohne `--filter` läuft die gesamte Backendsuite. Der Build benötigt Internet für
öffentliche .NET-Pakete. Diese fachlichen Prüfungen verhindern noch keine Rennen
zwischen parallelen Anfragen: Die gemeinsame Schreibtransaktion, dauerhafte
Vormerkungen und wiederholsichere Buchungsbefehle bleiben weitere Arbeitspakete.

## GitHub und Updates

`origin` ist der eigene Fork, `upstream` das Original. Der lokale Push-Zugang zu
`upstream` ist gesperrt. Neue Änderungen in kurzen Featurezweigen entwickeln und
gegen `gordons` prüfen. Keine Produktionszugänge als Repository-Secrets hinterlegen.

Die ursprünglichen sechs Workflows sind unter `.github/upstream-workflows/`
archiviert und können dort nicht als Actions starten. Die eigene Prüfungsdatei
hat nur Leserechte und führt weder Deployments noch Veröffentlichungen aus.
Builds und Tests beweisen zunächst nur die geprüften Abläufe; die Absicherung
aller Buchungskanäle und Migrationsproben sind eigene nächste Arbeitspakete.

Ein späteres Release muss ARM64 unterstützen und wird über feste Image-Digests
identifiziert. Produktion erhält erst ein konkret geprüftes und freigegebenes
Änderungspaket. Diese lokale Einrichtung ändert weder Coolify noch DNS oder Server.
