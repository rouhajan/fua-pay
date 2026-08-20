# Demo / staging deployment

Status: 2026-08-18

FUA Pay is currently deployed as a public demo/staging instance.

## Deployment

- Canonical URL: `https://fuapay.tul.cz`
- Alternate URL: `https://fuapay.fa.tul.cz` redirects to the canonical URL.
- Deployed application revision: `986be97d44adb417872babf65d212c16080eed65`
- Active release: `/opt/fuapay/releases/986be97d44ad`
- Previous rollback release: `/opt/fuapay/releases/8295a7c076ee`
- `/opt/fuapay/current` points to the active release.
- The application runs as `fuapay.service` under `fuapay:fuapay`.
- Kestrel listens only on `127.0.0.1:5080`.
- Nginx terminates HTTPS and proxies the canonical site to Kestrel.
- Nginx Basic Authentication protects the public demo with realm `FUA Pay demo`. Unauthenticated public requests therefore return HTTP 401 before reaching the application. Credentials are server-local and are not stored in this repository.
- Existing ACME/TLS configuration remains in use.
- Application configuration is loaded from the server-local `/etc/fuapay/staging.env`.

The 2026-08-18 deployment was verified after the atomic `/opt/fuapay/current` switch: local `/health/live` and `/health/ready` returned HTTP 200, the new split CSS assets were served by Kestrel, and the running process and working directory resolved to `/opt/fuapay/releases/986be97d44ad`. Uploaded deployment archives and temporary deployment paths were removed after the successful switch.

## Demo data and features

- Environment: `Staging`
- Database: `fuapay_demo`
- Database schema: 12 applied EF Core migrations
- Interactive test identities are enabled.
- Development/simulated payments are enabled.
- Demo data is isolated from the future production database.

## Not active in this deployment

- Microsoft Entra ID production authentication
- CSOB Payment Gateway
- Production database `fuapay`
- PDF potvrzení o úhradě (není součástí aktuálně nasazené revize `986be97d44adb417872babf65d212c16080eed65`)

The production database and `/etc/fuapay/production.env` are not used by this demo deployment.

This environment is intended for functional and UX testing only and must not be treated as the final production configuration.
