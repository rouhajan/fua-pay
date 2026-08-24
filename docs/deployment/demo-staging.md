# Demo / staging deployment

Status: 2026-08-24

## Deployment

- URL: `https://fuapay.tul.cz`
- Alternate URL: `https://fuapay.fa.tul.cz` redirects to the canonical URL.
- Revision: `8f3f8109ccb32ab67ee9d91e5d9fe8f25cc51ade`
- Active release: `/opt/fuapay/releases/8f3f8109ccb3`
- Rollback release: `/opt/fuapay/releases/8f6cc4ae2a78`
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

`e6cc4f3f709ef30cc1df565d993ccfae8c3d02a6a62667710e671fce60e917bb`

Verified after the atomic `/opt/fuapay/current` switch:

- executable and working directory use release `8f3f8109ccb3`;
- `/health/live`: HTTP 200;
- `/health/ready`: HTTP 200;
- direct Kestrel smoke requests for `/`, `/Privacy`, `/Terms` and `/Development/SignIn` succeeded;
- the public homepage contains the expected TUL sign-in copy;
- the Privacy and Terms pages contain the expected public content;
- the expected staging test-mode warning was emitted exactly once and no other warning/error condition was present in the new process log;
- HTTPS front door: HTTP 401 before Basic Authentication with realm `FUA Pay demo`;
- `https://fuapay.fa.tul.cz/`: HTTP 301 to `https://fuapay.tul.cz/`;
- `http://fuapay.tul.cz/`: HTTP 301 to `https://fuapay.tul.cz/`;
- rollback release `/opt/fuapay/releases/8f6cc4ae2a78` remains present and executable.

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
