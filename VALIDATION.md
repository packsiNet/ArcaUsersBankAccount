# Implementation and validation — 2026-10-11

The operator clarified that 600 people are the total audience and are unlikely to arrive simultaneously. The 600-VU waves are worst-case stress tests; they are not the expected normal traffic pattern.

## Delivered

One ASP.NET Core 10 Razor Pages application: a centered Persian RTL form, personal details and 1–10 bank/IBAN entries, frontend/backend normalization and checksum validation, PostgreSQL/EF Core migrations, atomic saves and signed idempotency tokens, encrypted IBAN values, Identity admin login/lockout, searchable paginated dashboard, masked IBAN details/CSV, local Tailwind/Vanilla JS/Vazirmatn assets, Nginx/systemd/environment examples, and deployment/backup/restore/security documentation. The introductory sidebar was removed. Card numbers and account numbers are no longer collected, validated or persisted; `RemoveCardAndAccountNumbers` drops the previous database columns.

## Verification

* Release build: successful, **0 warnings / 0 errors**.
* `dotnet publish`: successful in `artifacts/publish`; `appsettings.Local.json` is absent.
* Initial migration applied to real local PostgreSQL 17.6; EF reports no pending model changes. The local EF CLI 10.0.3 emits a version advisory against EF runtime 10.0.4; it did not prevent migration or model verification.
* **16 Playwright integration tests passed** on the simplified form: a non-editable bank dropdown, conditional required custom bank name with persisted custom value and correct account reindexing, registration without card/account inputs, removed database columns, one/multiple IBANs, Persian digits, blank/invalid mobile, invalid IBAN, add/remove/reindex, max/min count, eight concurrent identical requests producing one applicant, independent equal names, escaped HTML and same-origin assets, PostgreSQL-trigger-induced rollback, admin authorization/login/logout, masked CSV, lockout, CSRF/security headers/log checks and mobile layout/submission.
* **10 independent self-tests passed**: digit/whitespace/mobile normalization, MOD-97, count bounds, AES-GCM round-trip/random nonce/authentication/tamper rejection/historical-key recovery, CSV formula neutralization and complete masking of short values.
* Published app tested in Production mode on an isolated local port: HTTP redirects to HTTPS; HSTS, Secure/HttpOnly/Strict antiforgery cookies, no-store and noindex headers verified using trusted loopback proxy headers. This verifies application configuration, not public DNS/TLS/Nginx deployment.
* Desktop and 390px mobile screenshots inspected; mobile has no horizontal overflow, and submit controls meet 44px touch height.
* Official NuGet vulnerability feed checked **27 restored packages**, no findings. npm audit reported **0 vulnerabilities** at verification time. These are point-in-time checks, not a security certification.

## Earlier local k6 measurements (before form simplification)

Environment: Windows workstation; app, PostgreSQL and load generator share the host. One synthetic registration per VU, with GET/POST and real antiforgery tokens. Burst waits three seconds after GET. Raw JSON/log files are in `test-results/`. Results below are the latest recorded executions; 100/300 waves were rerun to preserve their report files.

| VUs / scenario | HTTP p95 (ms) | HTTP error rate | Failed registrations | HTTP requests/sec | Result |
|---|---:|---:|---:|---:|---|
| 100, one wave | 72.50 | 0% | 0% | 184.22 | Passed |
| 300, one wave | 191.74 | 10.09% | 18.33% | 447.33 | Failed |
| 600, one wave | 250.46 | 32.60% | 49.17% | 700.97 | Failed |
| 600, burst | 128.60 | 37.93% | 55.00% | 276.13 | Failed |

The larger waves include connection-refused errors during simultaneous GETs. The process remained available, and ordinary integration tests passed. Increasing the socket backlog to 2048 did not eliminate the local connection failures. The exact cause has not been established. Request latency and RPS do **not** establish capacity when registrations fail. No 600-concurrent-user production capacity claim is made.

Resource monitoring saved 97 samples in `test-results/resources-local.json`: peak host CPU 39.4%, peak tested app RSS 243.6 MiB, peak sum of PostgreSQL process RSS 1530.6 MiB. Host CPU includes other workstation activity; summed PostgreSQL RSS can count shared pages more than once. The monitoring window includes repeated waves and a briefly running second app instance used for Production-header checks. These are local observations, not target-server sizing evidence.

## Before public collection

1. Provision Ubuntu/PostgreSQL/Nginx, DNS and TLS; restrict listeners/firewall and protect all secrets/key-ring files.
2. Create the real administrator with a private password; local synthetic test credentials are not deployment credentials.
3. Set collection purpose, privacy notice, closing date and retention/backup policy. Card and account numbers are not collected.
4. Test backup restoration and masked output with recovered keys.
5. Repeat realistic traffic tests through Nginx on the target server. Use the supplied 300/600/burst scenarios if validating worst-case capacity; retain resource measurements and investigate any connection failures.

The public domain has not been deployed from this workspace. Instructions are in `README.md` and `deploy/DEPLOYMENT.md`.

