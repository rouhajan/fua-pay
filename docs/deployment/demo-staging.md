# Demo / staging deployment

Status: 2026-08-30

## Deployment

- URL: `https://fuapay.tul.cz`
- Alternate URL: `https://fuapay.fa.tul.cz` redirects to the canonical URL.
- Revision: `c0dba8bfb3eec6bc04d69271ff293c023098b409`
- Active release: `/opt/fuapay/releases/c0dba8bfb3ee`
- Rollback release: `/opt/fuapay/releases/45b86e3f2d1e`
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

Microsoft Entra authentication is already live on the staging deployment.
Production ČSOB integration and production database workload are not active.

Searchable customer selection is already implemented and deployed; it was
merged in PR #15 (`8f6cc4ae2a78280931127ed0e709949a02ca7b90`) before the current
runtime revision.

The settlement-return foundation remains the currently deployed application
behavior. The 2026-08-30 redeployment aligned the active release with repository
`main` after deployment-artifact hardening; there were no intervening
`src/FuaPay.Web` changes or EF schema changes relative to the previously active
application revision.

Actual ČSOB `payment/reverse` and `payment/refund` provider calls, automatic
financial retries after ambiguity and production financial traffic remain
disabled/not implemented as documented for the settlement-return foundation.

`Database__ApplyMigrationsOnStart=false`; database migration remains a
controlled deployment step.

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
