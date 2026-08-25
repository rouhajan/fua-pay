# Demo / staging deployment

Status: 2026-08-25

## Deployment

- URL: `https://fuapay.tul.cz`
- Alternate URL: `https://fuapay.fa.tul.cz` redirects to the canonical URL.
- Revision: `69853d416931860944a86188eb883576f914f404`
- Active release: `/opt/fuapay/releases/69853d416931`
- Rollback release: `/opt/fuapay/releases/8f3f8109ccb3`
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

`38a332c1c72ef03cce4c991106e15943e17e183409a84ce9d599dc1e1d12d561`

Verified after the atomic `/opt/fuapay/current` switch:

- executable and working directory use release `69853d416931`;
- `/health/live`: HTTP 200;
- `/health/ready`: HTTP 200;
- `/health/workers/csob-reconciliation`: HTTP 200 with status `Disabled`;
- direct Kestrel smoke requests for `/`, `/Privacy`, `/Terms` and `/Development/SignIn` succeeded;
- the new process log contained no warning/error entries;
- HTTPS front door: HTTP 401 before Basic Authentication with realm `FUA Pay demo`;
- `https://fuapay.fa.tul.cz/`: HTTP 301 to `https://fuapay.tul.cz/`;
- `http://fuapay.tul.cz/`: HTTP 301 to `https://fuapay.tul.cz/`;
- rollback release `/opt/fuapay/releases/8f3f8109ccb3` remains present and executable.

Direct Kestrel smoke requests that represent HTTPS must include both
`Host: fuapay.tul.cz` and `X-Forwarded-Proto: https`. Without the forwarded
scheme the request is treated as HTTP, so secure antiforgery cookie generation
on form pages such as `/Development/SignIn` is intentionally rejected.

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
