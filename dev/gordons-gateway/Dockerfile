FROM ghcr.io/karanshukla/openresto-nginx:2.5.0@sha256:b484130f3fb22a39b7e5ea65fcfbd23f4fe58d94125c26c4a830bda05bc1ff38
COPY default.conf /etc/nginx/conf.d/default.conf
ENTRYPOINT ["nginx", "-g", "daemon off;"]
