# Demo / staging deployment

Status: 2026-08-31

## Deployment

- URL: `https://fuapay.tul.cz`
- Alternate URL: `https://fuapay.fa.tul.cz` redirects to the canonical URL.
- Revision: `39293d85445bac0654b35bb2984617e273122481`
- Active release: `/opt/fuapay/releases/39293d85445b`
- Rollback release: `/opt/fuapay/releases/7cb9b1374970`
- Service account: `fuapay:fuapay`
- Kestrel: `127.0.0.1:5080`
- Reverse proxy: Nginx
- Configuration: `/etc/fuapay/staging.env`
- Database: `fuapay_demo`
- EF Core migrations: 17
- Payment provider: Development
- Staging test mode: enabled
- Interactive development/test sign-in: disabled
- Microsoft Entra login: enabled, live and used on `fuapay.tul.cz`
- Staging seed data: enabled
- Simulated payments: enabled
- Receipts: enabled
- Receipt preview mode: enabled
- Nginx Basic Authentication: not configured; the staging front door is intentionally public.

Microsoft Entra authentication is live on the staging deployment.
Production ČSOB integration and production database workload are not active.

Searchable customer selection is implemented and accepted on both desktop and a
real phone. PR #31 initially changed primary touch devices to the native platform
`<select>`, but real Android/Chrome staging smoke rejected that UX. PR #32
restored the same searchable picker on touch devices while skipping only the
desktop `pointerdown.preventDefault()` behavior for primary touch/coarse-pointer
devices. The final deployed revision was verified on a real phone with filtering
and tap selection working normally.

The settlement-return foundation remains deployed. Actual ČSOB
`payment/reverse` and `payment/refund` provider calls, automatic financial retries
after ambiguity and production financial traffic remain disabled/not implemented
as documented for the settlement-return foundation.

`Database__ApplyMigrationsOnStart=false`; database migration remains a
controlled deployment step.

## 2026-08-31 C-01 + C-02 staging release

### Scope

The staging application was first advanced from
`c0dba8bfb3eec6bc04d69271ff293c023098b409` to
`7cb9b1374970b12af32b7d57895df620c83fac3f`, containing:

- C-01: customer job-payment UI uses authoritative available/spendable credit;
- C-02 first pass: mobile customer selection used the native platform select.

The first deployment was technically healthy, but real-phone acceptance showed
that Android/Chrome rendered the native customer select as an undesirable
radio-style picker. PR #32 supplied the minimal follow-up and produced the final
runtime revision:

`39293d85445bac0654b35bb2984617e273122481`

PR #32 changes only `wwwroot/js/customer-select-filter.js`: desktop searchable
selection remains unchanged and primary touch/coarse-pointer devices use the same
searchable picker while omitting the desktop pointerdown prevention that could
interfere with touch selection.

No EF model or schema change was introduced by C-01, C-02 or the PR #32
follow-up. No migration SQL was generated or applied and `fuapay_demo` remains at
17 applied migrations.

### Verification

The final `main` release gate passed:

- Release build: PASS;
- formatting: PASS;
- `FuaPay.Web.Tests`: 775/775 PASS;
- EF pending-model check: no model changes since the last migration;
- PR #32 CI #131: PASS;
- PR #32 CodeQL #133: PASS.

The release was published self-contained for `linux-x64` after locked restore.
`appsettings.Development.json` was absent from the publish output.

Final release archive:

`fuapay-staging-39293d85445bac0654b35bb2984617e273122481-linux-x64.tar.gz`

SHA-256:

`2a3ad32ae7291ea58e51406fd267543b514eeda9ddf95cdb65b6b312032ba46d`

Size:

`122839371` bytes

The canonical deployment-artifact verifier reported:

- directories: 12 entries, mode `0770`;
- ordinary files: 400 entries, mode `0660`;
- `FuaPay.Web`: mode `0750`.

After transfer, the server-side archive matched both the expected SHA-256 and
byte size. `gzip -t` and a full tar listing completed successfully before
installation.

### Installation and activation

The final release was installed beside the active release at:

`/opt/fuapay/releases/39293d85445b`

Before activation, recursive verification confirmed:

- all release content owned by `fuapay:fuapay`;
- every directory mode `0770`;
- every ordinary file except the host executable mode `0660`;
- `FuaPay.Web` mode `0750` and executable by the `fuapay` service account;
- `appsettings.Development.json` absent;
- 401 files total and 12 directories.

`/opt/fuapay/current` was switched atomically from
`/opt/fuapay/releases/7cb9b1374970` to
`/opt/fuapay/releases/39293d85445b`, then `fuapay.service` was restarted.
The running process executable resolved to:

`/opt/fuapay/releases/39293d85445b/FuaPay.Web`

The first direct readiness connection immediately after restart could occur
before Kestrel had bound to `127.0.0.1:5080`; the bounded retry then returned
`{"status":"Healthy"}` as designed.

### Post-deployment and functional verification

Verified after restart:

- `/opt/fuapay/current` resolves to
  `/opt/fuapay/releases/39293d85445b`;
- `fuapay.service` is active;
- direct `/health/ready`: status `Healthy`;
- `https://fuapay.tul.cz/`: HTTP 200;
- `http://fuapay.tul.cz/`: HTTP 301 to `https://fuapay.tul.cz/`;
- `https://fuapay.fa.tul.cz/`: HTTP 301 to `https://fuapay.tul.cz/`;
- no warning-or-higher `fuapay.service` journal entries were observed in the
  deployment verification window;
- `systemctl --failed` reported no failed units.

Direct Kestrel health checks include both `Host: fuapay.tul.cz` and
`X-Forwarded-Proto: https`, matching the configured forwarded-header and host
validation boundary.

Functional staging acceptance confirmed:

- the desktop Management job-create customer selector remains searchable and
  behaves as before;
- on a real phone, the customer selector presents the same searchable UI,
  filters normally and completes selection by tap;
- Microsoft Entra authentication remains live and usable.

C-01's authoritative available-credit behavior is covered by the focused
regression test in the 775-test gate; no artificial blocking-credit state was
created solely for staging smoke.

The prior release `/opt/fuapay/releases/7cb9b1374970` remains present as the
immediate rollback target.

### Deployment cleanup

After successful technical and real-device verification:

- the transferred final release archive was removed from the deployment user's
  home directory;
- the transferred previous `7cb9...` release archive was also absent/removed;
- no temporary activation symlink remains;
- `/opt/fuapay/current` still resolves to
  `/opt/fuapay/releases/39293d85445b`;
- `fuapay.service` remains active.

The locally created hash-verified release archive is retained as release
evidence on the deployment workstation.

## 2026-08-30 main-alignment redeployment

### Scope

The staging runtime was redeployed from repository revision
`c0dba8bfb3eec6bc04d69271ff293c023098b409` so that the active release matches
current `main` and exercises the hardened canonical release-artifact workflow.

The Git comparison from the previously active application revision
`45b86e3f2d1e217140e2422b185dd8f616fe4856` to the new revision contained only
CI, solution metadata, documentation, deployment tooling and deployment-tooling
tests. It contained no `src/FuaPay.Web` change. The local release verification
also confirmed that the EF model still matches the existing migrations.

No migration SQL was generated or applied and the staging database remained at
17 migrations. No application configuration, Nginx configuration or database
content was intentionally changed by this redeployment.

### Release artifact

Before packaging, the canonical verification passed with 773/773 web/application
tests and a clean EF-model check. Locked `linux-x64` restore and self-contained
Release publish completed successfully with warnings treated as errors.

The repository deployment-artifact tool created and independently re-verified
the archive with the canonical profile:

- directories: 12 entries, mode `0770`;
- ordinary files: 400 entries, mode `0660`;
- `FuaPay.Web`: mode `0750`;
- `appsettings.Development.json`: absent.

Release archive:

`fuapay-staging-c0dba8bfb3eec6bc04d69271ff293c023098b409-linux-x64.tar.gz`

SHA-256:

`d36efcc523764c33d83eb228645e0658c48587edfeda2722b086e9b56c4e15e0`

Size:

`122837835` bytes

The transferred server-side archive matched both the expected SHA-256 and byte
size before extraction. `gzip -t` and a full tar listing completed successfully
before installation.

### Installation and activation

The new release was installed beside the active release at:

`/opt/fuapay/releases/c0dba8bfb3ee`

Before activation, recursive checks confirmed:

- all files/directories owned by `fuapay:fuapay`;
- every directory mode `0770`;
- every ordinary file except the host executable mode `0660`;
- `FuaPay.Web` mode `0750`;
- `FuaPay.Web` executable by the `fuapay` service account;
- 401 files in total, matching the 400 ordinary archive files plus the host
  executable.

`/opt/fuapay/current` was then switched atomically to the new release and
`fuapay.service` was restarted. The running process executable resolved to:

`/opt/fuapay/releases/c0dba8bfb3ee/FuaPay.Web`

### Post-deployment verification

Verified after restart:

- `/opt/fuapay/current` resolves to
  `/opt/fuapay/releases/c0dba8bfb3ee`;
- `fuapay.service` is active;
- `/health/live`: HTTP 200 with status `Healthy`;
- `/health/ready`: HTTP 200 with status `Healthy`;
- `https://fuapay.tul.cz/`: HTTP 200;
- `http://fuapay.tul.cz/`: HTTP 301 to `https://fuapay.tul.cz/`;
- `https://fuapay.fa.tul.cz/`: HTTP 301 to `https://fuapay.tul.cz/`;
- no warning-or-higher `fuapay.service` journal entries were observed in the
  post-restart verification window.

Direct Kestrel health checks included both `Host: fuapay.tul.cz` and
`X-Forwarded-Proto: https`, matching the configured forwarded-header and host
validation boundary.

The prior release `/opt/fuapay/releases/45b86e3f2d1e` remains present and its
`FuaPay.Web` executable was verified as a ready rollback target.

### Deployment cleanup

After successful verification, the transferred release archive was removed from
the deployment user's home directory. The locally created, hash-verified archive
was retained as release evidence on the deployment workstation. No temporary
activation symlink remains.

## 2026-08-29 settlement-return deployment

### Release artifact

Release archive SHA-256:

`31b4d19dd22fdb91ddbe792ac0db2addc3d68d2d2a037109a528ffe4ac51b3d9`

The transferred archive was verified by SHA-256 before extraction.

The release was installed beside the previous release. Its final server-side
permission profile is:

- directories: mode `0770`;
- ordinary files: mode `0660`;
- `FuaPay.Web`: mode `0750`;
- owner/group: `fuapay:fuapay`.

`appsettings.Development.json` is not present in the release.

### Pre-deployment database backup

Backup:

`/var/backups/fuapay/fuapay_demo-pre-45b86e3f2d1e-20260829T160525Z.dump`

SHA-256:

`fed9d864838d6979c3f4ce0f1fc94eeed235d4382798074b5c2421a4a0c90367`

The custom-format PostgreSQL backup passed a `pg_restore` structure check
before any migration was applied.

### Database migration

The database started at 14 applied migrations with:

`20260826161935_EnforcePrintReservationLifecycle`

The following three migrations were applied:

1. `20260828105225_AddSettlementReturns`
2. `20260828155222_AddCreditReturnHolds`
3. `20260829121234_AddSettlementReturnProviderAttempts`

Verified migration SQL SHA-256:

`4c10f38ef4da8b9fc06cdb9120793a9db14e48fb14b34a6f943a241369a9a197`

The migration was executed under PostgreSQL role `fuapay_migrator`.

The EF-generated SQL file contained a UTF-8 BOM. A temporary BOM-free working
copy was used for execution and removed after deployment; the original
hash-verified SQL file was not modified.

Successful migration log:

`/var/backups/fuapay/migration-14-to-17-45b86e3f2d1e-20260829T163012Z.log`

Post-migration verification confirmed:

- 17 applied EF Core migrations;
- latest migration:
  `20260829121234_AddSettlementReturnProviderAttempts`;
- `credits.return_holds` owner: `fuapay_migrator`;
- `payments.settlement_returns` owner: `fuapay_migrator`;
- `payments.settlement_return_provider_attempts` owner: `fuapay_migrator`;
- `fuapay_app` has SELECT, INSERT, UPDATE and DELETE privileges on all three
  new tables.

### Application switch and verification

The release was activated using the atomic `/opt/fuapay/current` switch.

Verified after restart:

- `/opt/fuapay/current` resolves to
  `/opt/fuapay/releases/45b86e3f2d1e`;
- `fuapay.service` is active;
- `/health/live`: HTTP 200 with status `Healthy`;
- `/health/ready`: HTTP 200 with status `Healthy`;
- `/health/workers/csob-reconciliation`: HTTP 200 with status `Disabled`;
- direct Kestrel smoke requests for `/`, `/Privacy` and `/Terms`: HTTP 200;
- no warning-or-higher `fuapay.service` journal entries were observed in the
  deployment verification window;
- `https://fuapay.tul.cz/`: HTTP 200;
- `http://fuapay.tul.cz/`: HTTP 301;
- `https://fuapay.fa.tul.cz/`: HTTP 301;
- Nginx Basic Authentication is absent as intended;
- rollback release `/opt/fuapay/releases/87ef21877809` remains present and
  executable.

Direct Kestrel smoke requests that represent HTTPS include both
`Host: fuapay.tul.cz` and `X-Forwarded-Proto: https`.

### Deployment cleanup

After successful verification:

- transferred release archive and checksum were removed from the deployment
  user's home directory;
- transferred migration SQL and checksum were removed;
- temporary BOM-free migration SQL was removed;
- no temporary deployment or rollback symlinks remain;
- active Nginx configuration was not modified by this deployment;
- the validated database backup and successful migration log were retained as
  deployment evidence.

## PDF receipt preview

Receipt preview remains enabled in staging:

- `Receipts__Enabled=true`;
- `Receipts__PreviewMode=true`;
- regular font: `/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf`;
- bold font: `/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf`.

The PDF receipt had previously been verified through an authenticated Customer
view of an already-paid job.

## Runtime baseline

The previously verified runtime-security baseline remains outside the scope of
the 2026-08-29 application deployment:

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
