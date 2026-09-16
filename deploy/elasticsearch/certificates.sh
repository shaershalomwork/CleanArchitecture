#!/usr/bin/env bash
set -euo pipefail
umask 077
cd /setup
if [ ! -f certs/ca.crt ]; then
  mkdir -p certs collector-secrets
  elasticsearch-certutil ca --silent --pem -out /setup/ca.zip
  unzip -oq /setup/ca.zip -d generated
  elasticsearch-certutil cert --silent --pem --name local --dns localhost,elasticsearch,otel-collector,web,kibana --ip 127.0.0.1 --ca-cert /setup/generated/ca/ca.crt --ca-key /setup/generated/ca/ca.key --out /setup/leaf.zip
  unzip -oq /setup/leaf.zip -d generated
  cp generated/ca/ca.crt certs/ca.crt
  cp generated/local/local.crt certs/tls.crt
  cp generated/local/local.key certs/tls.key
  cp certs/tls.crt collector-secrets/tls.crt
  cp certs/tls.key collector-secrets/tls.key
  cp certs/ca.crt collector-secrets/elasticsearch-ca.crt
fi
# Local bind mounts must be readable by the distinct non-root service UIDs.
chmod 755 certs collector-secrets
chmod 644 certs/* collector-secrets/*
chown 1000:0 elastic-password
chmod 640 elastic-password
