#!/usr/bin/env bash
set -Eeuo pipefail
umask 077
[[ -n ${SERVER_HOST:-} && -n ${SERVER_SSH_KEY:-} ]] || { echo 'SERVER_HOST and SERVER_SSH_KEY are required.' >&2; exit 1; }
: "${SERVER_PORT:?SSH port is required}" "${SERVER_USER:?SSH user is required}"
[[ $SERVER_HOST =~ ^[a-zA-Z0-9][a-zA-Z0-9.-]*$ && $SERVER_USER =~ ^[a-z_][a-z0-9_-]*$ ]] || exit 1
[[ $SERVER_PORT =~ ^[0-9]+$ && $SERVER_PORT -ge 1 && $SERVER_PORT -le 65535 ]] || exit 1
work=$(mktemp -d)
remote=''
target="$SERVER_USER@$SERVER_HOST"
cleanup() {
    # The path is validated below and deliberately expanded on the client.
    # shellcheck disable=SC2029
    if [[ -n $remote ]]; then ssh "${ssh_options[@]}" "$target" "rm -rf -- '$remote'" >/dev/null 2>&1 || true; fi
    rm -rf -- "$work"
}
trap cleanup EXIT
printf '%s\n' "$SERVER_SSH_KEY" | tr -d '\r' > "$work/key"
unset SERVER_SSH_KEY
chmod 600 "$work/key"
ssh-keygen -y -f "$work/key" > "$work/key.pub"
printf 'Deployment SSH public-key fingerprint: '
ssh-keygen -lf "$work/key.pub" -E sha256
if [[ -n ${SERVER_KNOWN_HOSTS:-} ]]; then
    printf '%s\n' "$SERVER_KNOWN_HOSTS" > "$work/known_hosts"
else
    echo 'No SERVER_KNOWN_HOSTS configured: trusting the first observed SSH host key.'
    ssh-keyscan -T 15 -p "$SERVER_PORT" -H "$SERVER_HOST" > "$work/known_hosts" 2>/dev/null
fi
[[ -s $work/known_hosts ]] || { echo 'SSH host key discovery failed.' >&2; exit 1; }
ssh_options=(-i "$work/key" -p "$SERVER_PORT" -o BatchMode=yes -o IdentitiesOnly=yes
    -o StrictHostKeyChecking=yes -o "UserKnownHostsFile=$work/known_hosts" -o ConnectTimeout=20 -o ServerAliveInterval=30)
python3 - "$work/deployment.json" <<'PY'
import json, os, sys
from deploy.server_config import validate
c=dict(app_url=os.environ['APP_URL'], release=os.environ['RELEASE_ID'],
       admin_email=os.environ.get('ADMIN_EMAIL',''), certificate_email=os.environ.get('LETSENCRYPT_EMAIL',''),
       ssh_port=os.environ['SERVER_PORT'])
validate(c)
with open(sys.argv[1],'w') as f: json.dump(c,f)
PY
(cd artifacts && sha256sum --check release.tar.gz.sha256)
if remote=$(ssh "${ssh_options[@]}" "$target" 'umask 077; mktemp -d /tmp/safetunnels-deploy.XXXXXXXX'); then
    :
else
    ssh_status=$?
    remote=''
    cat >&2 <<'MESSAGE'
SSH connection failed before installation. If the server reports Permission denied:
- SERVER_SSH_KEY must contain the complete private key, not a public key or filename.
- Its matching public key must be authorized for SERVER_USER on SERVER_HOST.
- Compare the public-key fingerprint above against authorized_keys on the server.
- For root, check /root/.ssh (0700), authorized_keys (0600), root ownership and SSH login policy.
- Inspect the server's ssh journal through the provider console. Never publish private keys.
See deploy/DEPLOYMENT.md, SSH authentication troubleshooting.
MESSAGE
    exit "$ssh_status"
fi
[[ $remote =~ ^/tmp/safetunnels-deploy\.[a-zA-Z0-9]{8}$ ]] || { echo 'Unexpected remote temporary path.' >&2; remote=''; exit 1; }
scp -i "$work/key" -P "$SERVER_PORT" -o BatchMode=yes -o IdentitiesOnly=yes -o StrictHostKeyChecking=yes \
    -o "UserKnownHostsFile=$work/known_hosts" artifacts/release.tar.gz artifacts/release.tar.gz.sha256 \
    "$work/deployment.json" "$target:$remote/"
privilege=''
[[ $SERVER_USER == root ]] || privilege='sudo -n'
# Validated paths and the fixed privilege prefix deliberately expand locally.
# shellcheck disable=SC2029
ssh "${ssh_options[@]}" "$target" \
    "set -eu; cd '$remote'; sha256sum --check release.tar.gz.sha256; tar -xzf release.tar.gz; $privilege bash deploy/install.sh '$remote/deployment.json'"
