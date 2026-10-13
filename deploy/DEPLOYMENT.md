# Ubuntu 26.04 CI/CD

The workflow `.github/workflows/deploy.yml` tests every pull request and deploys successful pushes to `main`. A manual **Actions > Test and deploy Ubuntu 26.04 > Run workflow** on `main` also deploys. Production deployments are serialized; a server lock also prevents concurrent installers.

## Required GitHub settings

Under **Settings > Secrets and variables > Actions**, configure:

| Name | Value |
| --- | --- |
| `APP_URL` | `https://account.zanatech.online` |
| `SERVER_HOST` | `account.zanatech.online` or the server's IPv4 address |
| `SERVER_USER` | `root` |
| `SERVER_SSH_KEY` | Complete unencrypted OpenSSH private key, including BEGIN/END lines |
| `APIIR_TOKEN` | api.ir access token for production IBAN inquiries; store as an Actions secret |

The matching public key must already be in `/root/.ssh/authorized_keys`. `SERVER_SSH_KEY` must be a secret; the other values can be secrets or variables. The server must already run Ubuntu **26.04**, have internet access and permit SSH from GitHub-hosted runners. Non-root users require passwordless sudo. No database password or application encryption key is needed in GitHub.

Optional settings: `SERVER_PORT` (default 22), `LETSENCRYPT_EMAIL` (renewal notices), `ADMIN_EMAIL` (default `admin@account.zanatech.online`), `SERVER_KNOWN_HOSTS` (verified SSH known_hosts entry). Existing managed domain/admin email must remain stable on subsequent deployments.

`APIIR_TOKEN` is sent over SSH and written to the root-only server state and `account.env` as `ApiIr__Token`. It is excluded from generated shell assignments and deployment metadata. A non-empty secret updates the server token; an absent secret preserves the existing token. To remove a token, remove it from both the server's `secrets.json` and `account.env`. After adding the GitHub secret, run the deployment workflow to apply it; saving a secret alone does not update the running server.

Without `SERVER_KNOWN_HOSTS`, each run discovers and trusts the observed host key using ssh-keyscan; this does not independently authenticate the server. For verified host identity, obtain the key/fingerprint through your server provider's trusted console and supply the matching known_hosts line. SSH still enforces StrictHostKeyChecking. Passphrase-protected private keys are not supported by this unattended workflow.

If you configure GitHub environment protection on `production`, those rules apply before deployment. The workflow itself introduces no manual approval gate.

## SSH authentication troubleshooting

`Permission denied (publickey,password)` means authentication failed before any installer ran. A parsed private key does not prove that the destination authorizes it. Verify SERVER_HOST, SERVER_USER and that SERVER_SSH_KEY contains the complete matching private key. The workflow prints the public key's SHA256 fingerprint for comparison.

On your own computer, using the same private-key file whose contents are stored in GitHub:

```bash
ssh-keygen -y -f /PATH/TO/PRIVATE_KEY
ssh-keygen -lf /PATH/TO/PRIVATE_KEY -E sha256
ssh -i /PATH/TO/PRIVATE_KEY -o IdentitiesOnly=yes root@account.zanatech.online
```

Using the provider's console on the server, for SERVER_USER=root:

```bash
install -d -m 700 -o root -g root /root/.ssh
touch /root/.ssh/authorized_keys
chown root:root /root/.ssh/authorized_keys
chmod 600 /root/.ssh/authorized_keys
# Append the matching PUBLIC key as one line; preserve existing keys.
nano /root/.ssh/authorized_keys
ssh-keygen -lf /root/.ssh/authorized_keys -E sha256
journalctl -u ssh --since '15 minutes ago' --no-pager
```

Do not put the private key on the server. Public-key login must be enabled, and root login permitted by the effective sshd policy, including Match blocks. If root login is intentionally disabled, use an authorized non-root account with passwordless sudo and set SERVER_USER accordingly. Changing Nginx, Certbot or SERVER_KNOWN_HOSTS does not resolve a rejected user key.

## DNS and network prerequisites

Point the domain's A record to this server. If an AAAA record exists, it must also reach this server over IPv6; remove an incorrect AAAA record. HTTP port 80 must be publicly reachable for ACME issuance/renewal, and HTTPS 443 for the site. Provider firewalls/security groups must permit them; the installer manages only Ubuntu UFW. Disable forced CDN/proxy redirects that prevent ACME webroot verification during initial setup. Do not configure another Nginx site with the same server_name.

UFW preserves existing rules and allows the configured SSH port before enabling, then opens 80/443. PostgreSQL and Kestrel listen on loopback, at 5432 and 5080. Existing non-loopback PostgreSQL installations are rejected rather than silently changed. The installer is intended for a dedicated server and does not reinstall Ubuntu, delete databases or reset firewall rules.

## Automated steps

1. CI runs shell syntax checks, ShellCheck, server-state tests, .NET self-tests, and 16 Chromium/PostgreSQL integration tests against a disposable database. It builds local CSS/JS and publishes a portable framework-dependent .NET 10 release with no local credentials.
2. The deploy job transfers a checksummed artifact over authenticated SSH. The server verifies its checksum before running the root installer.
3. The installer uses Ubuntu 26.04's own repositories to install `aspnetcore-runtime-10.0`, PostgreSQL, Nginx, Certbot with its Nginx plugin, UFW and required utilities. No SDK/Node compiler is installed on production. [Microsoft installation documentation](https://learn.microsoft.com/dotnet/core/install/linux-ubuntu-install).
4. It creates stable random database passwords, a 256-bit AES key, backup key and administrator password **once on the server**. It creates separate migration and runtime roles, takes an encrypted pre-migration backup, applies EF migrations and creates the initial administrator without resetting an existing account.
5. It activates `/opt/safetunnels/current`, starts the hardened systemd service, and checks application/schema/database readiness. Failures after activation restore the previous application symlink and managed Nginx configuration. Database migrations are never automatically reversed, because that could destroy submitted records. Future migrations must remain compatible with the previous app when rollback is required.
6. It obtains a trusted certificate with Certbot's HTTP webroot method, configures HTTPS/HTTP redirection, enables `certbot.timer` and a post-renewal Nginx reload hook. Optional email is used if provided; otherwise ACME registration proceeds without email.
7. It checks HTTPS locally with certificate verification and then from the GitHub runner. Successful output reports the site URL, never credentials.

Packages receive available distro updates when deploying. The PostgreSQL major version comes from Ubuntu 26.04's repository; CI uses PostgreSQL 17 for integration checks. Test validation cannot replace exercising the installer on the actual Ubuntu server.

## First administrator login

Retrieve credentials over your own SSH session:

```bash
ssh root@account.zanatech.online 'cat /root/safetunnels-admin.txt'
```

Open `https://account.zanatech.online/admin/login`. The credentials file is root-only (0600). Never paste it into Actions logs or Git. Redeployment preserves the database, administrator password, AES key and DataProtection ring.

Persistent state:

| Location | Purpose |
| --- | --- |
| `/etc/safetunnels/secrets.json` | Root-only persistent keys/passwords; never delete to fix a deployment |
| `/etc/safetunnels/account.env` | Root-only runtime configuration read by systemd |
| `/var/lib/safetunnels/keys` | Application-owned DataProtection ring |
| `/opt/safetunnels/releases/` | Immutable published releases |
| `/opt/safetunnels/current` | Active release symlink |
| `/var/backups/safetunnels/` | Encrypted pre-deploy and daily backups, 30-day retention |
| `/etc/safetunnels/backup.key` | Recovery key; keep a protected copy off-server |

`migration.env` contains elevated credentials only during provisioning and is deleted after successful migration/admin setup. A failed migration can leave this root-only file for diagnostics; subsequent deployment replaces it. Existing unmanaged installations require an explicit key/database migration; the script refuses to generate replacement keys over an existing account.env.

## Operations

```bash
systemctl status safetunnels-account nginx postgresql
journalctl -u safetunnels-account --since '15 minutes ago'
cat /etc/safetunnels/database-setup.log   # root-only; do not publish
nginx -t
certbot certificates
certbot renew --dry-run
systemctl list-timers certbot.timer safetunnels-backup.timer
/usr/local/sbin/safetunnels-backup
curl -f -H 'Host: account.zanatech.online' -H 'X-Forwarded-Proto: https' http://127.0.0.1:5080/health/ready
```

The readiness URL is blocked by Nginx externally. On migration failure, inspect the transient `safetunnels-migrate-*` systemd journal. Error details are kept on the server rather than uploaded as CI artifacts. Re-run the workflow after correcting DNS/SSH/package prerequisites. A new workflow attempt creates a new release ID.

## Backup and recovery

Daily backups contain a PostgreSQL custom-format dump, encryption state and DataProtection ring. Archives use AES-256-CBC with PBKDF2 (200,000 iterations); they are root-only. Copy backups **and the recovery key** to separate protected off-server storage; local backups alone do not cover loss of the server. The application AES key is required to decrypt stored IBANs. Define retention and rehearse a restore before relying on backups.

On an isolated recovery host, with root-only working directory:

```bash
umask 077
mkdir -p /root/safetunnels-recovery
openssl enc -d -aes-256-cbc -pbkdf2 -iter 200000 -pass file:/PROTECTED/backup.key \
  -in /PROTECTED/BACKUP.tar.enc -out /root/safetunnels-recovery/backup.tar
tar -xf /root/safetunnels-recovery/backup.tar -C /root/safetunnels-recovery
```

Stop the app; restore `database.dump` into an isolated database with `pg_restore --no-owner --role=safetunnels_owner`, restore the preserved encryption state and DataProtection keys, recreate grants and protected environment, then verify decryption and masked admin output before switching traffic. Do not restore over the live database automatically. Backup archives currently rely on protected storage for tamper protection.

The removal migration `RemoveCardAndAccountNumbers` drops old account/card columns permanently. An app rollback cannot recover those values; only a pre-upgrade backup can.
