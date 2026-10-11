#!/usr/bin/env bash
# Root installer for a dedicated Ubuntu 26.04 host. Do not enable shell tracing.
set -Eeuo pipefail
umask 077
[[ $EUID == 0 ]] || { echo 'Run as root or through passwordless sudo.' >&2; exit 1; }
# shellcheck source=/dev/null
source /etc/os-release
[[ $ID == ubuntu && $VERSION_ID == 26.04 ]] || { echo 'Ubuntu 26.04 is required.' >&2; exit 1; }
bundle=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)
config=${1:?Deployment JSON is required}
exec 9>/run/lock/safetunnels-deploy.lock
flock -w 600 9
export DEBIAN_FRONTEND=noninteractive
apt-get update -q
apt-get install -y --no-install-recommends software-properties-common
add-apt-repository -y universe
apt-get update -q
apt-get install -y --no-install-recommends aspnetcore-runtime-10.0 nginx postgresql postgresql-client \
    certbot python3-certbot-nginx ca-certificates curl openssl python3 ufw
systemctl enable --now postgresql nginx
getent passwd safetunnels >/dev/null || useradd --system --user-group --home-dir /var/lib/safetunnels --shell /usr/sbin/nologin safetunnels
install -d -m 700 /etc/safetunnels /var/backups/safetunnels
install -d -o safetunnels -g safetunnels -m 700 /var/lib/safetunnels /var/lib/safetunnels/keys
install -d -m 755 /opt/safetunnels/releases /var/www/letsencrypt
eval "$(python3 "$bundle/deploy/server_config.py" "$config" /etc/safetunnels)"
: "${RELEASE:?Validated release is required}"
release="/opt/safetunnels/releases/$RELEASE"
[[ ! -e $release ]] || { echo 'Release identifier already exists; rerun the workflow for a new identifier.' >&2; exit 1; }
install -d -m 755 "$release"
cp -a "$bundle/app/." "$release/"
chown -R root:root "$release"
chmod -R u=rwX,go=rX "$release"
[[ -f $release/SafeTunnels.Account.dll ]] || { echo 'Application bundle is incomplete.' >&2; exit 1; }
# Existing external PostgreSQL listeners are not silently reconfigured.
listen=$(runuser -u postgres -- psql -XAt -v ON_ERROR_STOP=1 -c 'SHOW listen_addresses')
[[ $listen == localhost || $listen == 127.0.0.1 ]] || { echo 'PostgreSQL must listen only on loopback.' >&2; exit 1; }
port=$(runuser -u postgres -- psql -XAt -v ON_ERROR_STOP=1 -c 'SHOW port')
[[ $port == 5432 ]] || { echo 'PostgreSQL port 5432 is required.' >&2; exit 1; }
# Values never appear in command arguments or GitHub logs.
OWNER_PASSWORD=$(python3 -c 'import json;print(json.load(open("/etc/safetunnels/secrets.json"))["owner_password"])')
RUNTIME_PASSWORD=$(python3 -c 'import json;print(json.load(open("/etc/safetunnels/secrets.json"))["runtime_password"])')
export OWNER_PASSWORD RUNTIME_PASSWORD
sql_log=/etc/safetunnels/database-setup.log
if ! runuser -u postgres -- psql -X -v ON_ERROR_STOP=1 >"$sql_log" 2>&1 <<'SQL'
SET log_statement = 'none';
SET log_min_error_statement = 'panic';
\getenv owner_password OWNER_PASSWORD
\getenv runtime_password RUNTIME_PASSWORD
SELECT 'CREATE ROLE safetunnels_owner LOGIN' WHERE NOT EXISTS (SELECT FROM pg_roles WHERE rolname='safetunnels_owner') \gexec
SELECT 'CREATE ROLE safetunnels_runtime LOGIN' WHERE NOT EXISTS (SELECT FROM pg_roles WHERE rolname='safetunnels_runtime') \gexec
ALTER ROLE safetunnels_owner WITH LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION PASSWORD :'owner_password';
ALTER ROLE safetunnels_runtime WITH LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION PASSWORD :'runtime_password';
SELECT 'CREATE DATABASE safetunnels OWNER safetunnels_owner' WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname='safetunnels') \gexec
SQL
then echo 'Database setup failed; inspect the root-only database-setup.log on the server.' >&2; exit 1; fi
unset OWNER_PASSWORD RUNTIME_PASSWORD
owner=$(runuser -u postgres -- psql -XAt -v ON_ERROR_STOP=1 -c "SELECT pg_get_userbyid(datdba) FROM pg_database WHERE datname='safetunnels'")
[[ $owner == safetunnels_owner ]] || { echo 'Existing database has an unexpected owner; no ownership takeover performed.' >&2; exit 1; }
runuser -u postgres -- psql -X -v ON_ERROR_STOP=1 -d safetunnels >"$sql_log" 2>&1 <<'SQL'
REVOKE ALL ON DATABASE safetunnels FROM PUBLIC;
GRANT CONNECT ON DATABASE safetunnels TO safetunnels_owner, safetunnels_runtime;
REVOKE CREATE ON SCHEMA public FROM PUBLIC;
GRANT USAGE ON SCHEMA public TO safetunnels_runtime;
ALTER DEFAULT PRIVILEGES FOR ROLE safetunnels_owner IN SCHEMA public GRANT SELECT,INSERT,UPDATE,DELETE ON TABLES TO safetunnels_runtime;
ALTER DEFAULT PRIVILEGES FOR ROLE safetunnels_owner IN SCHEMA public GRANT USAGE,SELECT ON SEQUENCES TO safetunnels_runtime;
SQL
install -m 700 "$bundle/deploy/backup.sh" /usr/local/sbin/safetunnels-backup
install -m 644 "$bundle/deploy/templates/safetunnels-backup.service" /etc/systemd/system/safetunnels-backup.service
install -m 644 "$bundle/deploy/templates/safetunnels-backup.timer" /etc/systemd/system/safetunnels-backup.timer
# Backup before any schema change, including the initial empty database.
/usr/local/sbin/safetunnels-backup
previous=$(readlink -f /opt/safetunnels/current 2>/dev/null || true)
activated=false
site=/etc/nginx/sites-available/safetunnels-account
old_site=/etc/safetunnels/nginx.previous
had_site=false
if [[ -f $site ]]; then cp -a "$site" "$old_site"; had_site=true; fi
restore_app() {
    status=$?
    trap - ERR
    if [[ $had_site == true ]]; then
        cp -a "$old_site" "$site"
        nginx -t && systemctl reload nginx || true
    else
        rm -f /etc/nginx/sites-enabled/safetunnels-account "$site"
        nginx -t && systemctl reload nginx || true
    fi
    if [[ $activated == true && -n $previous && -d $previous ]]; then
        rm -f /opt/safetunnels/.rollback
        ln -s "$previous" /opt/safetunnels/.rollback
        mv -Tf /opt/safetunnels/.rollback /opt/safetunnels/current
        systemctl restart safetunnels-account || true
        echo 'Previous application release restored. Database was not downgraded.' >&2
    fi
    echo 'Deployment failed; server diagnostics are retained locally.' >&2
    exit "$status"
}
trap restore_app ERR
# systemd reads the root-only environment file, then runs the app unprivileged.
systemd-run --quiet --wait --collect --unit="safetunnels-migrate-${RELEASE}" \
    -p User=safetunnels -p Group=safetunnels -p "WorkingDirectory=$release" \
    -p EnvironmentFile=/etc/safetunnels/migration.env \
    /usr/bin/dotnet "$release/SafeTunnels.Account.dll" --migrate --ensure-admin
runuser -u postgres -- psql -X -v ON_ERROR_STOP=1 -d safetunnels >"$sql_log" 2>&1 <<'SQL'
GRANT SELECT,INSERT,UPDATE,DELETE ON ALL TABLES IN SCHEMA public TO safetunnels_runtime;
GRANT USAGE,SELECT ON ALL SEQUENCES IN SCHEMA public TO safetunnels_runtime;
REVOKE INSERT,UPDATE,DELETE ON TABLE "__EFMigrationsHistory" FROM safetunnels_runtime;
SQL
if [[ ! -e /root/safetunnels-admin.txt ]]; then
    python3 - <<'PY'
import json, os
s=json.load(open('/etc/safetunnels/secrets.json'))
fd=os.open('/root/safetunnels-admin.txt', os.O_WRONLY|os.O_CREAT|os.O_EXCL, 0o600)
with os.fdopen(fd,'w') as f:
    f.write('Login: https://'+s['domain']+'/admin/login\nEmail: '+s['admin_email']+'\nPassword: '+s['admin_password']+'\n')
PY
fi
rm -f /etc/safetunnels/migration.env
install -m 644 "$bundle/deploy/safetunnels-account.service" /etc/systemd/system/safetunnels-account.service
rm -f /opt/safetunnels/.next
ln -s "$release" /opt/safetunnels/.next
mv -Tf /opt/safetunnels/.next /opt/safetunnels/current
activated=true
systemctl daemon-reload
systemctl enable safetunnels-account safetunnels-backup.timer
systemctl restart safetunnels-account
systemctl start safetunnels-backup.timer
ready=false
for ((attempt=0; attempt<30; attempt++)); do
    if curl --fail --silent --max-time 5 -H "Host: $DOMAIN" -H 'X-Forwarded-Proto: https' \
        http://127.0.0.1:5080/health/ready >/dev/null; then ready=true; break; fi
    sleep 2
done
[[ $ready == true ]] || { echo 'Application/database readiness failed.' >&2; false; }
# Preserve existing firewall rules. Open SSH before enabling the firewall.
ufw allow "$SSH_PORT/tcp"
connection=${SSH_CONNECTION:-}
actual_ssh_port=${connection##* }
if [[ $actual_ssh_port =~ ^[0-9]+$ && $actual_ssh_port -ge 1 && $actual_ssh_port -le 65535 ]]; then
    ufw allow "$actual_ssh_port/tcp"
fi
ufw allow 80/tcp
ufw allow 443/tcp
ufw --force enable
if [[ ! -f /etc/letsencrypt/live/$DOMAIN/fullchain.pem ]]; then
    sed "s/@DOMAIN@/$DOMAIN/g" "$bundle/deploy/templates/nginx-http.conf" > "$site"
    ln -sfn "$site" /etc/nginx/sites-enabled/safetunnels-account
    nginx -t
    systemctl reload nginx
fi
contact=(--register-unsafely-without-email)
[[ -z $CERTIFICATE_EMAIL ]] || contact=(--email "$CERTIFICATE_EMAIL")
certbot certonly --webroot --webroot-path /var/www/letsencrypt --cert-name "$DOMAIN" -d "$DOMAIN" \
    --non-interactive --agree-tos --keep-until-expiring "${contact[@]}"
sed "s/@DOMAIN@/$DOMAIN/g" "$bundle/deploy/templates/nginx-https.conf" > "$site"
ln -sfn "$site" /etc/nginx/sites-enabled/safetunnels-account
nginx -t
systemctl reload nginx
install -d -m 755 /etc/letsencrypt/renewal-hooks/deploy
printf '#!/bin/sh\n/usr/sbin/nginx -t && /usr/bin/systemctl reload nginx\n' > /etc/letsencrypt/renewal-hooks/deploy/safetunnels-nginx
chmod 755 /etc/letsencrypt/renewal-hooks/deploy/safetunnels-nginx
systemctl enable --now certbot.timer
curl --fail --silent --show-error --max-time 30 --resolve "$DOMAIN:443:127.0.0.1" "https://$DOMAIN/" >/dev/null
trap - ERR
printf 'Deployment succeeded: https://%s\nInitial administrator credentials: /root/safetunnels-admin.txt (root only).\n' "$DOMAIN"
