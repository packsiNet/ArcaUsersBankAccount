# Security and data handling

The centered Persian form collects first name, last name, optional mobile, and one to ten bank names/IBANs. Card numbers and account numbers have been removed from the frontend, binding model, validation, database model, admin details and CSV. The removal migration drops the previous account-number and card-last-four columns. The app never accepts CVV2, expiry dates, PINs, OTPs or bank credentials.

## Encryption and validation

Iranian IBANs are encrypted with AES-256-GCM, fresh 96-bit nonces and 128-bit authentication tags. Associated data binds ciphertext to its account-entry UUID and the IBAN field. Keys are injected outside source and PostgreSQL, with an active identifier and historical keys for recovery. MOD-97 validates format/checksum; it does not prove ownership or existence. Bank-code matching is omitted without a maintained authoritative reference; the editable catalog includes an “other” option.

## Operational controls

* Use HTTPS through trusted Nginx. Kestrel and PostgreSQL bind loopback; Nginx overwrites forwarding headers and only loopback proxies are trusted.
* Apply least privilege to the runtime database role; use a separate role for migrations. Admin pages and CSV show masked IBANs only. CSV export requires the admin role and a CSRF-protected POST, contains tracking code, bank, masked IBAN and UTC timestamp, and is bounded to 10,000 applicants.
* Identity uses standard password hashing, a 14-character password minimum, five-failure/15-minute lockout, 20-minute admin sessions, CSRF and HttpOnly/Strict cookies, with Secure cookies in production. Consider an administrative VPN/allowlist.
* Idempotency identifiers are random and signed with a 12-hour expiry. PostgreSQL's unique index handles concurrent duplicates. One relational SaveChanges transaction persists the entire applicant/IBAN graph. Independent equal names are accepted.
* Registration is limited to 1,000 POSTs per minute per source IP to accommodate shared NAT; login is limited to 10 per minute per IP, in addition to per-account Identity lockout. Tune to the deployment.
* There is no bank-data logging, request-body logging, analytics, external runtime assets, browser localStorage or bank data in URLs. EF/Npgsql and unhandled-exception diagnostic logs are disabled to avoid leaking database error values.
* Dynamic responses use no-store, noindex/nofollow and CSP. Dynamic HTTPS responses are not compressed; Nginx compresses only CSS/JavaScript.
* Protect AES key files and the DataProtection key-ring directory with restricted filesystem permissions, outside source/webroot. Add certificate encryption or a managed secret store if required by the deployment's threat model.
* Back up the database, all historical AES keys and the DataProtection ring separately into encrypted, access-controlled storage. Rehearse restoration. Changing ActiveKeyId affects new writes only; retain historical keys for existing ciphertext and unexpired backups.
* Define collection purpose, privacy notice, closing date and retention period before collecting real information. Delete expired applicants (accounts cascade) and expire backups under that policy.

The operator clarified that 600 people are the total audience and simultaneous arrival is unlikely. The earlier local 100-user wave passed, while larger Windows waves had connection failures. Repeat realistic tests through Nginx on the target Ubuntu server before relying on capacity. This application is not a claim of independent security certification or proven 600-user concurrency.

