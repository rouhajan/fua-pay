# Demo / staging deployment

Status: 2026-08-17

FUA Pay is currently deployed as a public demo/staging instance.

## Deployment

- Canonical URL: `https://fuapay.tul.cz`
- Alternate URL: `https://fuapay.fa.tul.cz` redirects to the canonical URL.
- Deployed application revision: `8295a7c076ee`
- Active release: `/opt/fuapay/releases/8295a7c076ee`
- `/opt/fuapay/current` points to the active release.
- The application runs as `fuapay.service`.
- Kestrel listens only on `127.0.0.1:5080`.
- Nginx terminates HTTPS and proxies the canonical site to Kestrel.
- Nginx Basic Authentication protects the public demo. Credentials are server-local and are not stored in this repository.
- Existing ACME/TLS configuration remains in use.

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

The production database and `/etc/fuapay/production.env` are not used by this demo deployment.

This environment is intended for functional and UX testing only and must not be treated as the final production configuration.
