#!/usr/bin/env bash
set -Eeuo pipefail
umask 077
[[ $EUID == 0 ]] || exit 1
exec 9>/run/lock/safetunnels-backup.lock
flock 9
directory=/var/backups/safetunnels
mkdir -p "$directory"
chmod 700 "$directory"
target="$directory/$(date -u +%Y%m%dT%H%M%S)-$$.tar.enc"
work=$(mktemp -d "$directory/.work.XXXXXX")
trap 'rm -rf -- "$work"' EXIT
runuser -u postgres -- pg_dump --format=custom safetunnels > "$work/database.dump"
cp /etc/safetunnels/secrets.json "$work/secrets.json"
cp -a /var/lib/safetunnels/keys "$work/data-protection"
tar -C "$work" -cf - . | openssl enc -aes-256-cbc -salt -pbkdf2 -iter 200000 \
    -pass file:/etc/safetunnels/backup.key -out "$target.partial"
mv -- "$target.partial" "$target"
# Keep 30 days; the encryption key must also be stored securely off-server.
find "$directory" -maxdepth 1 -type f -name '*.tar.enc' -mtime +30 -delete
printf 'Encrypted backup completed.\n'
