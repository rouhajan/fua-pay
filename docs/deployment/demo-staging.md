# Demo / staging deployment

Status: 2026-09-08

Tento soubor popisuje pouze aktuální staging runtime a poslední deployment
evidence. Kanonické vytváření/installace release artefaktu je v
[`release-artifacts.md`](release-artifacts.md). Aktuální ČSOB postup až do
production readiness je v
[`../integrations/csob-production-readiness.md`](../integrations/csob-production-readiness.md).

## Aktuální runtime

- URL: `https://fuapay.tul.cz`
- Alternate URL: `https://fuapay.fa.tul.cz` -> canonical URL.
- Revision: `a638cad732b210a2f949f12c05ecdf84a2bce7a8`.
- Active release: `/opt/fuapay/releases/a638cad732b2`.
- Immediate rollback release: `/opt/fuapay/releases/39293d85445b`.
- Service account: `fuapay:fuapay`.
- Kestrel: `127.0.0.1:5080` behind Nginx.
- Configuration: `/etc/fuapay/staging.env`.
- Database: `fuapay_demo`.
- EF Core migrations: 17.
- `Database__ApplyMigrationsOnStart=false`.
- Microsoft Entra login: live and in use.
- Payment provider: ČSOB integration, Merchant ID `M1EPAY2213`.
- Simulated payments: disabled.
- ČSOB reconciliation worker: enabled and healthy.
- Staging seed data: enabled.
- Receipt preview mode: enabled.
- Nginx Basic Authentication: intentionally absent.
- Production ČSOB traffic and production database workload: not active.

## 2026-09-08 PR #35 deployment

PR #35 (`fix: complete ČSOB top-up return flow`) was merged as:

`a638cad732b210a2f949f12c05ecdf84a2bce7a8`

Scope relevant to staging:

- customer top-up is available with active ČSOB provider;
- CreateTopUp highlights `Kredit` while payment index/detail remain `Platby`;
- ČSOB browser return redirects to routed
  `/Customer/Payments/Details/{id}?view=customer` instead of the invalid query
  form that previously produced 404;
- no EF model/schema change and no migration.

### Release gate

Before packaging:

- canonical `scripts/verify.ps1`: PASS;
- Release build: PASS, 0 warnings/errors;
- formatting: PASS;
- `FuaPay.Web.Tests`: 780/780 PASS;
- EF pending-model check: no model changes since the last migration;
- PostgreSQL integration gate: 224/224 PASS on isolated `fuapay_test_*` DB;
- live ČSOB GET echo: PASS.

Release archive:

`fuapay-staging-a638cad732b210a2f949f12c05ecdf84a2bce7a8-linux-x64.tar.gz`

SHA-256:

`379ac60d7ca48f266d5863d28aa398144e6f60fb6825e1dcc349ea24cb8b53f2`

Size:

`122840907` bytes

Canonical archive verification:

- directories: 12, mode `0770`;
- ordinary files: 400, mode `0660`;
- `FuaPay.Web`: mode `0750`.

After transfer the server independently matched SHA-256 and byte size;
`gzip -t` and full tar listing passed before installation.

### Installation and activation

The release was installed beside the active release at:

`/opt/fuapay/releases/a638cad732b2`

Pre-activation checks passed:

- all release content owned by `fuapay:fuapay`;
- all directories mode `0770`;
- all ordinary files except host executable mode `0660`;
- `FuaPay.Web` mode `0750` and executable by the service account;
- `appsettings.Development.json` absent.

The first activation was rolled back unnecessarily by an incorrect deployment
check that treated the ČSOB worker's transient post-start HTTP 503/`NotStarted`
state as immediate failure. The application readiness itself was already
`Healthy`. No application or database migration rollback was involved.

The corrected activation used bounded worker warm-up. Final result:

- `/opt/fuapay/current` -> `/opt/fuapay/releases/a638cad732b2`;
- `fuapay.service`: active;
- running executable:
  `/opt/fuapay/releases/a638cad732b2/FuaPay.Web`;
- `/health/ready`: `Healthy`;
- `/health/workers/csob-reconciliation`: `Healthy`, no failed cycle reported;
- `https://fuapay.tul.cz/`: HTTP 200;
- `http://fuapay.tul.cz/`: HTTP 301;
- `https://fuapay.fa.tul.cz/`: HTTP 301.

### Canonical staging post-activation health rule

Direct Kestrel requests must include both:

```text
Host: fuapay.tul.cz
X-Forwarded-Proto: https
```

Startup health is bounded, not instantaneous:

1. retry `/health/ready` until `Healthy` or timeout;
2. then poll `/health/workers/csob-reconciliation`;
3. worker `NotStarted` immediately after restart is a warm-up state, not a
   rollback reason;
4. worker `Healthy` is PASS;
5. worker `Failed`, `Stale` or bounded timeout is FAIL and may trigger rollback;
6. verify the running executable resolves to the new release;
7. finish with canonical/alternate HTTPS smoke.

Do not infer a broken `/opt/fuapay/current` target from an unprivileged
`readlink -f` when the deployment user cannot traverse the release directory;
use an appropriately privileged read-only check.

## 2026-09-08 live ČSOB functional acceptance

Two customer scenarios were exercised against the real ČSOB integration
environment after deployment.

Successful top-up:

- amount: 100 Kč;
- provider reference: `fcd3c38f6325@LI`;
- browser returned directly to the routed payment detail without 404;
- reconciliation settled the payment to `Succeeded` / „Uhrazená“;
- exactly one additional 100 Kč credit effect was visible; after the two
  successful 100 Kč integration top-ups the test account displayed 200 Kč.

Customer-cancelled top-up:

- provider reference: `47ac34a8568f@LI`;
- the attempt ended locally as `Cancelled` / „Zrušená“;
- no credit was added.

The successful return exposed one UX gap: the detail page can load before the
asynchronous reconciliation worker finishes, so it initially remained
`Pending` and the new credit appeared only after manual F5. The financial model
is correct because browser return only schedules authoritative server-side
reconciliation; the UI should be improved with bounded asynchronous status
refresh instead of requiring a full-page reload.

Current payment UX also inserts an internal detail page between successful
`payment/init` and `payment/process`. The agreed target is direct redirect to the
ČSOB process URI while retaining Details as a recovery path for existing
`Pending` attempts.

These UX/lifecycle items and the remaining production-readiness gaps are tracked
only in `docs/integrations/csob-production-readiness.md` to avoid duplicated
stale TODO lists.

## Previous rollback baseline

Before this deployment staging ran:

- revision `39293d85445bac0654b35bb2984617e273122481`;
- release `/opt/fuapay/releases/39293d85445b`;
- artifact SHA-256
  `2a3ad32ae7291ea58e51406fd267543b514eeda9ddf95cdb65b6b312032ba46d`;
- artifact size `122839371` bytes.

That release remains the immediate rollback target until the current ČSOB
acceptance work is closed. Older deployment evidence remains available in Git
history; it is intentionally not duplicated in this current-state document.
