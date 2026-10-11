# Ubuntu 24.04 deployment

Target: 4 vCPU, 8 GB RAM, 40 GB NVMe; domain `account.safetunnels.net`. These are deployment assumptions, not tested capacity guarantees.

## Host and database

Install the supported .NET 10 ASP.NET runtime from Microsoft's Ubuntu repository, PostgreSQL, Nginx, certbot and monitoring utilities. Review current official install instructions before provisioning: https://learn.microsoft.com/dotnet/core/install/linux-ubuntu-install and https://www.postgresql.org/download/linux/ubuntu/ . Allow managed SSH before enabling UFW, then allow only 80/443 publicly. Keep port 5080 and 5432 private.

```bash
sudo apt update
sudo apt install nginx postgresql certbot sysstat
sudo useradd --system --home /var/lib/safetunnels --shell /usr/sbin/nologin safetunnels
sudo install -d -o safetunnels -g safetunnels -m 700 /var/lib/safetunnels/keys
sudo install -d -o root -g safetunnels -m 750 /etc/safetunnels /opt/safetunnels/app
```

Set PostgreSQL `listen_addresses = 'localhost'`, `password_encryption = 'scram-sha-256'`, and appropriate loopback `host` entries in `pg_hba.conf` using `scram-sha-256`, not trust. Set max_connections around 120 as a starting point; runtime pool is capped at 80. Tune after measurements.

Create a schema owner/migration role `safetunnels_owner` and a separate runtime role `safetunnels`; set their passwords interactively with `\password` so they are not stored in shell history. Example using `sudo -u postgres psql`:

```sql
CREATE ROLE safetunnels_owner LOGIN;
CREATE ROLE safetunnels LOGIN;
CREATE DATABASE safetunnels OWNER safetunnels_owner;
\password safetunnels_owner
\password safetunnels
\connect safetunnels
REVOKE CREATE ON SCHEMA public FROM PUBLIC;
GRANT USAGE ON SCHEMA public TO safetunnels;
ALTER DEFAULT PRIVILEGES FOR ROLE safetunnels_owner IN SCHEMA public GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO safetunnels;
ALTER DEFAULT PRIVILEGES FOR ROLE safetunnels_owner IN SCHEMA public GRANT USAGE, SELECT ON SEQUENCES TO safetunnels;
```

After migrations, grant DML on existing tables and sequences to runtime, but exclude schema changes. Restore grants if a backup rebuilds the database.

## Application and keys

Run `dotnet publish SafeTunnels.Account -c Release -o artifacts/publish` on a build host. Copy the published directory to `/opt/safetunnels/app`; give root ownership and read/execute access to the service group. Confirm **no appsettings.Local.json** is in the output.

Copy `environment.example` to `/etc/safetunnels/account.env` and set root:safetunnels ownership, mode 0640. Replace all placeholders. Generate an AES key using `openssl rand -base64 32`, save it only into the protected environment file and encrypted secret backup. Do not paste secrets into Git, issue trackers or command-line arguments. The DataProtection directory must remain writable only by the service user.

Use the migration role connection only in a protected one-time provisioning environment, then run:

```bash
sudo -u safetunnels sh -c 'set -a; . /etc/safetunnels/migration.env; set +a; cd /opt/safetunnels/app; dotnet SafeTunnels.Account.dll --migrate'
```

The migration file uses the same environment variables as `account.env`, except its connection string uses `safetunnels_owner`. Create the first administrator with a separate protected bootstrap file containing `Bootstrap__Email` and `Bootstrap__Password` (minimum 14 characters, mixed case, digits, symbol). Source the normal runtime environment and bootstrap file, then run `dotnet SafeTunnels.Account.dll --create-admin` as the service user. Delete the temporary bootstrap file afterward. No automatic startup seeding occurs, and existing administrators are never silently reset.

Alternatively, from source on a build/admin host:

```bash
dotnet tool restore
dotnet ef database update --project SafeTunnels.Account
```

Here `ConnectionStrings__DefaultConnection` must be the migration-role connection. Automatic runtime migration is deliberately disabled. Back up before any schema upgrade.

## TLS and proxy

Point the DNS A/AAAA records to the server. Before the first certificate, enable only the HTTP server block in `nginx.conf`, create `/var/www/letsencrypt`, and verify `sudo nginx -t`.

```bash
sudo certbot certonly --webroot -w /var/www/letsencrypt -d account.safetunnels.net
```

Install the complete supplied Nginx configuration after the certificate exists. Place the supplied service in `/etc/systemd/system/safetunnels-account.service`:

```bash
sudo nginx -t
sudo systemctl reload nginx
sudo systemctl daemon-reload
sudo systemctl enable --now safetunnels-account
sudo certbot renew --dry-run
sudo systemctl status safetunnels-account
```

Run a local trusted request through Nginx to confirm HTTPS, HSTS, secure cookies, forwarding behavior, admin access restrictions, no-store and noindex headers. No sensitive values should be in URLs or operational logs. The service automatically restarts on failure. Only loopback forwarding proxies are trusted; if the topology changes, review trust boundaries before modifying headers.

## Backup and restore

Store backups outside the server in an encrypted, access-controlled repository. PostgreSQL dumps contain personal names/mobile and encrypted bank fields. Use `.pgpass` mode 0600 or a managed secret for authentication, never plaintext passwords in command arguments. Back up daily while this temporary campaign is running, subject to the operator's retention policy.

```bash
pg_dump -h 127.0.0.1 -U safetunnels_owner -Fc safetunnels > /PROTECTED_LOCATION/safetunnels.dump
```

Separately back up the entire protected AES key set and `/var/lib/safetunnels/keys`. Encrypt these secret backups with a separately protected recovery key. A database backup without its AES keys cannot recover bank data. Old AES keys must survive active records and retained backups. DataProtection keys preserve cookie/token usability; a lost ring invalidates sessions and pending forms.

To restore, provision an isolated database, create roles, stop the application, run `pg_restore --no-owner --role=safetunnels_owner -d RESTORE_DATABASE safetunnels.dump` using an appropriately privileged operator, restore permissions, AES keys and DataProtection ring, then test decryption and masked admin output. Confirm there are no public listeners before starting. Rehearse restore before collection begins. Rotate admin passwords after a security event.

## Release gate

Set a collection closing date, privacy notice, retention period and approved administrator list. Confirm the IBAN collection purpose and operational requirements in SECURITY.md. Run the Playwright suite and 100/300/600 plus burst k6 scenarios on an isolated staging database on the target Ubuntu server; retain CPU/RAM/I/O and HTTP metrics. Audit NuGet dependencies (`dotnet list SafeTunnels.Account package --vulnerable --include-transitive`) and npm build dependencies (`npm audit`). Review DNS/TLS, firewall, backups and restoration. This workspace does not provision the public server or publish to the final domain.

The simplified form uses bank name and IBAN only. `RemoveCardAndAccountNumbers` permanently drops the previous encrypted account-number and card-last-four columns. Its schema rollback cannot recover those values; retain a protected pre-upgrade backup if recovery is required.
