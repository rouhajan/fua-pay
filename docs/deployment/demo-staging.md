# Demo / staging deployment

Status: 2026-08-21

## Deployment

- URL: `https://fuapay.tul.cz`
- Alternate URL: `https://fuapay.fa.tul.cz` redirects to the canonical URL.
- Revision: `b90d2f5b5665e2a1547383fb32de0e519208ec89`
- Active release: `/opt/fuapay/releases/b90d2f5b5665`
- Rollback release: `/opt/fuapay/releases/986be97d44ad`
- Service account: `fuapay:fuapay`
- Kestrel: `127.0.0.1:5080`
- Reverse proxy: Nginx
- Configuration: `/etc/fuapay/staging.env`
- Database: `fuapay_demo`
- EF Core migrations: 12
- Payment provider: Development
- Interactive test identities: enabled
- Simulated payments: enabled
- Nginx Basic Authentication: enabled

Production Entra authentication, production ČSOB integration and production
database workload are not active. PDF receipt code is deployed; receipts
remain disabled without explicit receipt configuration.

## Verification

Release archive SHA-256:

`3d653a206a200c819d9f561faee01d57da9631cd1e6e5951be77513810f969a8`

Verified after the atomic `/opt/fuapay/current` switch:

- executable and working directory use release `b90d2f5b5665`;
- `/health/live`: HTTP 200;
- `/health/ready`: HTTP 200;
- `/Development/SignIn`: HTTP 200, 9 test profiles;
- HTTPS front door: HTTP 401 before Basic Authentication;
- HSTS: `max-age=31536000`;
- CSP: `base-uri 'none'`;
- `X-Content-Type-Options: nosniff`;
- no warning-or-higher service log entries during deployment verification.

No EF migration changed from the previous staging revision.
`Database__ApplyMigrationsOnStart=false`.

## Runtime baseline

Verified:

- `ProtectSystem=strict`;
- `NoNewPrivileges=yes`;
- `PrivateTmp=yes`;
- `PrivateDevices=yes`;
- `ProtectKernelTunables=yes`;
- `ProtectKernelModules=yes`;
- `ProtectControlGroups=yes`;
- `RestrictSUIDSGID=yes`;
- Kestrel and PostgreSQL listen only on loopback;
- UFW default incoming policy is deny; ports 22, 80 and 443 are allowed;
- SSH root, password and keyboard-interactive login are disabled;
- Data Protection keyring directory: mode `0700`;
- Data Protection key: mode `0600`;
- `fuapay_app` and `fuapay_migrator` are separate non-superuser roles;
- application schemas and tables are owned by `fuapay_migrator`.

This deployment is staging, not production.
