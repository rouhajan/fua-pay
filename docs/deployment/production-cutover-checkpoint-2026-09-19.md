# Production cutover checkpoint 2026-09-19

Status: STOP point after successful archive/restore verification of the current demo database.

This document records the exact verified state reached during the first clean
production cutover on 2026-09-19. It is an evidence/checkpoint document only.
It does not change runtime configuration, PostgreSQL roles, databases, releases,
secrets, Nginx, Entra, FUA Print or ČSOB.

The canonical cutover intent remains in
[`production-cutover-plan.md`](production-cutover-plan.md). The canonical
release and migration artifact procedure remains in
[`release-artifacts.md`](release-artifacts.md). Historical/current demo runtime
evidence remains in [`demo-staging.md`](demo-staging.md).

## 1. Repository and release candidate

The clean cutover work is based on exact `main` SHA:

```text
774b324c48d8f874db21f115479f3b317c2a73d0
```

This is merge commit PR #66,
`docs: decouple FUA Print from first production go-live`.

At the checkpoint, GitHub `main` still pointed exactly to this SHA.

PR #66 is documentation-only. Its relevant first-go-live decision is:

```text
Payments__Provider=None
Csob__Enabled=false
PrintPayments__Enabled=false
PrintCredentials__Enabled=false
```

FUA Print and production ČSOB activation are separate later milestones and are
not blockers for the first FUA Pay production go-live.

### Prepared local release artifacts

The release artifacts had already been created and verified locally before the
server-side database archive work started:

```text
C:\secure\release\fuapay-linux-x64.tar.gz         123584446 bytes
C:\secure\release\fuapay-migrations.sql               96840 bytes
C:\secure\release\fuapay-migrations.execution.sql     96865 bytes

RELEASE ARTIFACT CHECKPOINT: PASS
RELEASE SHA: 774b324c48d8f874db21f115479f3b317c2a73d0
```

Do not rebuild or silently replace these artifacts during continuation. If they
must be regenerated for any reason, treat that as a new artifact checkpoint and
re-verify identity/hashes before deployment.

## 2. Live server state before cutover

Read-only server preflight at approximately 2026-09-19 18:23 UTC established:

```text
hostname: fuapay
fuapay.service: active

/opt/fuapay/current ->
/opt/fuapay/releases/cc142e23a200e72831284605d8553962b160a984

running executable:
/opt/fuapay/releases/cc142e23a200e72831284605d8553962b160a984/FuaPay.Web
```

Current demo database:

```text
database: fuapay_demo
PostgreSQL: 16.15 (Ubuntu 16.15-0ubuntu0.24.04.1)
EF migrations: 24
database size before archive: 10015 kB
pg_dump: 16.15
pg_restore: 16.15
```

Preflight result:

```text
DEMO BACKUP PREFLIGHT: PASS
```

No service stop, release switch, schema migration or production database change
occurred during this preflight.

## 3. Demo database archive

Cutover step 5 required archiving the current demo database before any clean
production database work.

A PostgreSQL custom-format dump was created:

```text
/var/backups/fuapay/fuapay_demo-20260919T182636Z-cc142e23a200e72831284605d8553962b160a984.dump
```

Evidence:

```text
dump owner/mode: postgres:postgres 0600
dump displayed size: 141K

TOC list:
/var/backups/fuapay/fuapay_demo-20260919T182636Z-cc142e23a200e72831284605d8553962b160a984.dump.list
displayed size: 16K
owner/mode: postgres:postgres 0600

SHA evidence:
/var/backups/fuapay/fuapay_demo-20260919T182636Z-cc142e23a200e72831284605d8553962b160a984.dump.sha256
displayed size: 161 bytes
owner/mode: postgres:postgres 0600
```

Exact dump SHA-256:

```text
5642726e5b75e9964482836e048e70363b1d6f5d734e83192236099ef0b97d08
```

`pg_restore --list` parsed the archive successfully.

TOC entry count:

```text
207
```

Archive result:

```text
DEMO CUSTOM BACKUP: PASS
```

Important operational detail: `/var/backups/fuapay` is protected for
`postgres`; archive checks from the deployment account must therefore be run
through `sudo -u postgres`. An initial restore-preflight attempt used an
unprivileged `test -f` and failed before any database creation. That was a shell
permission mistake, not an archive failure.

Also avoid setting `set -e` directly in the interactive SSH parent shell.
Use a subshell `( set -euo pipefail; ... )` so a failed guard does not close the
SSH session.

## 4. Restore verification

The archive was re-verified before restore:

```text
SHA-256:
5642726e5b75e9964482836e048e70363b1d6f5d734e83192236099ef0b97d08

pg_restore --list: PASS
existing restore-check DB before creation: 0
live fuapay.service: active
live release: cc142e23a200e72831284605d8553962b160a984

RESTORE PREFLIGHT: PASS
```

The exact dump was then restored into the isolated database:

```text
fuapay_restorecheck_20260919_182636
```

Restore evidence:

```text
createdb: PASS
pg_restore --exit-on-error: PASS
restored EF migrations: 24
latest MigrationId:
20260917064943_AddFinancialDocumentTaxSnapshot
restored application user tables: 25
restored database size: 9863 kB
```

The live service remained unchanged throughout:

```text
fuapay.service: active
/opt/fuapay/current ->
/opt/fuapay/releases/cc142e23a200e72831284605d8553962b160a984
```

Restore result:

```text
DEMO BACKUP RESTORE: PASS
```

### Restored schemas

```text
access
app
audit
credits
financial_documents
jobs
notifications
payments
public
service_units
```

### Restored row inventory

The restored archive contained:

```text
access.external_identities|12
access.role_assignments|20
access.users|11
app.__ef_migrations_history|24
audit.events|252
credits.accounts|3
credits.adjustment_commands|0
credits.manual_topup_commands|2
credits.movements|12
credits.print_credentials|1
credits.print_reservations|4
credits.return_holds|0
financial_documents.documents|3
financial_documents.number_counters|1
jobs.job_number_sequences|4
jobs.jobs|16
notifications.outbox|18
payments.csob_payment_reconciliation|17
payments.order_number_sequence|1
payments.payment_initiations|19
payments.payments|19
payments.settlement_return_provider_attempts|1
payments.settlement_returns|1
service_units.requester_assignments|8
service_units.units|4
```

Inventory result:

```text
RESTORED DATA INVENTORY: PASS
```

This proves that the cutover backup is not merely present on disk: the exact
custom-format archive is parseable and restorable into an isolated PostgreSQL
database with the expected migration history, schemas, tables and real demo
content.

Therefore cutover step 5, "archive current demo DB", is closed as PASS.

## 5. PostgreSQL role/ownership evidence from the current demo

A read-only catalogue inspection of `fuapay_demo` established the current
historical staging model.

Database owner:

```text
fuapay_demo|fuapay_migrator
```

Role attributes:

```text
fuapay_app|LOGIN=true|SUPERUSER=false|CREATEDB=false|CREATEROLE=false|INHERIT=true
fuapay_migrator|LOGIN=true|SUPERUSER=false|CREATEDB=false|CREATEROLE=false|INHERIT=true
```

No FUA Pay role memberships were present.

Schema owners:

```text
access|fuapay_migrator
app|fuapay_migrator
audit|fuapay_migrator
credits|fuapay_migrator
financial_documents|fuapay_migrator
jobs|fuapay_migrator
notifications|fuapay_migrator
payments|fuapay_migrator
public|pg_database_owner
service_units|fuapay_migrator
```

Table ownership summary:

```text
access|fuapay_migrator|3
app|fuapay_migrator|1
audit|fuapay_migrator|1
credits|fuapay_migrator|7
financial_documents|fuapay_migrator|2
jobs|fuapay_migrator|2
notifications|fuapay_migrator|1
payments|fuapay_migrator|6
service_units|fuapay_migrator|2
```

Database privileges observed:

```text
fuapay_app      CONNECT=true CREATE=false TEMP=false
fuapay_migrator CONNECT=true CREATE=true  TEMP=true
```

Database ACL expansion confirmed:

```text
fuapay_app      | CONNECT
fuapay_migrator | CONNECT
fuapay_migrator | CREATE
fuapay_migrator | TEMPORARY
```

`fuapay_app` had schema `USAGE=true` and `CREATE=false` on all application
schemas plus `public`.

The explicit sequence probe established for
`credits.movements_id_seq`:

```text
USAGE=true
SELECT=true
UPDATE=true
```

The last pasted ACL probe did not produce a usable table/default-privilege
listing. Do not infer table/default ACL from that absence. Re-read the canonical
documented grant/bootstrap procedure before making production ACL changes.

## 6. Critical documentation distinction: staging is not CI

The current repository documentation explicitly distinguishes staging from the
generic CI/deployment example.

`docs/deployment/demo-staging.md` records that the current staging database:

- is owned by `fuapay_migrator`;
- uses `fuapay_app` and `fuapay_migrator` as LOGIN roles;
- has no `fuapay_deployer`;
- has no FUA Pay role memberships;
- uses local Unix-socket `peer` authentication and localhost TCP
  `scram-sha-256`;
- has no OS account named `fuapay_migrator`;
- executes staging migrations from a local `postgres` peer session while the
  verified execution SQL explicitly runs
  `SET ROLE "fuapay_migrator";`.

The same document explicitly says that `fuapay_deployer` shown in CI and in a
generic `release-artifacts.md` example is not a staging account and must not be
derived or created for staging.

By contrast, `.github/workflows/ci.yml` at release SHA `774b324...` uses an
isolated CI-only role model:

```text
fuapay_migrator  NOLOGIN
fuapay_deployer  LOGIN
GRANT fuapay_migrator TO fuapay_deployer
test DB OWNER fuapay_migrator
```

The CI migration session connects as `fuapay_deployer`; the verified migration
execution artifact then performs `SET ROLE "fuapay_migrator";`.

Do not copy the CI role bootstrap into production merely because it exists.
Do not copy the historical staging role model into production merely because it
currently works. Continue from the canonical production database/bootstrap
documentation and verified server state only.

## 7. Exact STOP point

At the end of work on 2026-09-19:

- cutover step 5 (archive current demo DB) is PASS and closed;
- the restore-check database
  `fuapay_restorecheck_20260919_182636` was intentionally retained for the
  checkpoint and had not yet been dropped;
- no clean production database had been created;
- no production PostgreSQL role had been created, altered or granted;
- no production ACL had been changed;
- no production migration SQL had been applied;
- `fuapay_demo` had not been modified by the cutover work;
- `fuapay.service` had not been stopped or restarted by the cutover work;
- `/opt/fuapay/current` still pointed to
  `cc142e23a200e72831284605d8553962b160a984`;
- release `774b324...` had not been installed or activated on the server;
- production environment/secrets had not been installed;
- Entra production re-acceptance had not yet begun;
- FUA Print remained outside the first production go-live;
- production ČSOB remained outside the first production go-live.

This is the safe resume boundary.

## 8. Resume order

When work resumes, do not recreate already-proven evidence unless a relevant
state changed.

Resume in this order:

1. Re-read this checkpoint and the exact `774b324...` canonical deployment
   documentation.
2. Confirm GitHub `main` / approved release identity has not changed. If it has
   changed, stop and explicitly decide whether `774b324...` remains the approved
   release candidate.
3. Confirm live service/release and the demo archive still match this checkpoint.
4. Locate and follow the canonical documented production PostgreSQL
   role/database/bootstrap procedure. Do not derive it from CI or staging by
   analogy.
5. Only after that, remove the no-longer-needed isolated restore-check DB in a
   controlled step.
6. Create the clean production PostgreSQL database and required roles/ACL exactly
   according to the canonical production procedure.
7. Re-verify the target identity before migration execution.
8. Apply only the already-prepared, reviewed and verified
   `fuapay-migrations.execution.sql` through the canonical migration path.
9. Verify 24 migrations, expected schema ownership/ACL and a clean empty
   production financial state.
10. Continue cutover steps 7+ from
    `production-cutover-plan.md`: bootstrap the first Administrator and real
    ServiceUnits, production secrets/profile, Entra acceptance, release
    installation/activation, health and controlled smoke.

Do not:

- clean demo data in place;
- copy demo users, credit, jobs, payments, print credentials, audit history or
  FinancialDocuments into production;
- run database tests against production or a restore containing real personal
  data;
- enable FUA Print during this first go-live;
- enable production ČSOB during this first go-live;
- run automatic reverse migrations on rollback;
- guess PostgreSQL role or ACL configuration from incomplete evidence.

## 9. First-go-live product boundary retained

The first production go-live remains intentionally narrow:

```text
ASPNETCORE_ENVIRONMENT=Production
AllowedHosts=fuapay.tul.cz
Database__ApplyMigrationsOnStart=false

DevelopmentSignIn__Enabled=false
DevelopmentData__Enabled=false
DevelopmentData__ResetOnStart=false
StagingTestMode__Enabled=false

Entra__Enabled=true

Payments__Provider=None
Csob__Enabled=false

PrintPayments__Enabled=false
PrintCredentials__Enabled=false
```

Production must start on a new clean PostgreSQL database from the canonical
migration chain. Demo financial/history data is not production data and is not
copied.

---

Checkpoint principle: source of truth is the repository documentation plus
fresh verified server evidence. Do not fill gaps by assumption.
