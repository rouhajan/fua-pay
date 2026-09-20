# Off-host staging environment

Status: target model agreed 2026-09-20.

This document defines the target non-production staging model after the first
clean FUA Pay production cutover.

The word **staging** here means a persistent logical environment with its own
database, configuration, secrets and test history. It does **not** mean a second
permanent VM and it does not mean a second runtime hosted on the production VM.

## 1. Production host boundary

The production VM that serves `https://fuapay.tul.cz` is production-only.

After the clean cutover it must contain only the active production FUA Pay
runtime and production dependencies required on that host. In particular:

- `fuapay.tul.cz` is always Production;
- the active production service is the only FUA Pay application runtime on the
  production VM;
- no staging FUA Pay service listens on another port on the production VM;
- no staging configuration, staging integration secrets or staging Data
  Protection key ring is used by the production runtime;
- the old `fuapay_demo` database is not converted into production.

During the cutover the old staging database may remain temporarily present but
inactive on the PostgreSQL host only as a verified restore/transfer source. No
post-cutover staging runtime may connect to it there. After the off-host staging
restore is verified and retention requirements are satisfied, it can be removed
from the production host in a separate controlled cleanup.

## 2. Persistent staging outside the production VM

Staging is preserved off-host, initially on the developer workstation.

It may be stopped most of the time. Persistence means that its state survives
between test sessions, not that a server must run continuously.

The staging environment keeps its own:

- PostgreSQL database restored from the preserved staging snapshot;
- FUA Pay runtime configuration;
- Data Protection key ring;
- Entra staging/test configuration;
- ČSOB integration merchant configuration and keys;
- integration/test-only FUA Print credentials when needed;
- test users and internal staging UserIds;
- demo/test credit history;
- integration Payments and reconciliation history;
- staging FinancialDocuments;
- print reservations and staging print credentials;
- acceptance evidence relevant to that environment.

None of those values is production authority.

The first off-host staging bootstrap should preserve the verified archive of the
2026-09-19 staging database rather than reseeding a new unrelated test history.

## 3. Data separation

Production and staging may use the same source code and the same migration
lineage, but must not share:

- a PostgreSQL database;
- a connection string;
- a Data Protection key ring;
- environment/secret files;
- production service credentials;
- production ČSOB keys or merchant configuration;
- production FUA Print service credentials or pepper;
- financial history;
- print credentials.

A staging UserId is never a production UserId.

No mechanism may copy the staging database into production.

If a real datum ever needs to move between environments, that transfer requires
a separately designed and audited migration for that specific datum type.

Legacy SafeQ is the canonical example: matching and transfer target the
production UserId created by a legitimate first login to Production, never a
staging UserId.

## 4. Runtime model on the developer workstation

The developer workstation is allowed to host the off-host staging runtime and
its PostgreSQL database.

The exact Windows service/container packaging can be chosen independently of
the production deployment model. The required properties are:

- dedicated staging database;
- dedicated staging configuration and secret storage;
- no use of production secrets;
- explicit `ASPNETCORE_ENVIRONMENT=Staging` or another documented
  non-production profile supported by the application;
- development/demo behavior enabled only when deliberately required by a
  staging acceptance scenario;
- clear visual marking that the environment is STAGING / TEST;
- startup and shutdown controlled by the operator;
- no dependency on the production VM for normal staging execution.

The staging runtime does not have to be public or running continuously for
ordinary development, migration verification or regression testing.

## 5. Public HTTPS for integration acceptance

A full ČSOB browser/return acceptance cannot be assumed to work solely on an
unroutable localhost endpoint.

When an acceptance scenario requires an externally reachable callback/return
flow, staging gets a separate controlled public HTTPS ingress that terminates at
the off-host staging runtime.

The ingress must:

- use a staging-only hostname/URL, never `fuapay.tul.cz`;
- be explicitly allowed in the relevant Entra/ČSOB integration configuration;
- forward only to the staging runtime;
- expose no production secret or production database;
- be enabled only as long as operationally required if the chosen mechanism is
  temporary;
- preserve normal HTTPS, forwarded-header and host validation boundaries.

The exact tunnel/reverse-proxy/DNS mechanism is an operational follow-up and
must be verified against the actual ČSOB integration and Entra redirect
requirements before a full browser/return acceptance is declared PASS.

Absence of this public staging ingress does not block the first Production
go-live with `Payments__Provider=None` and `Csob__Enabled=false`.

## 6. Release promotion

The preferred future workflow is:

```text
implementation
-> automated verification
-> immutable release + migration artifacts
-> off-host staging migration/deploy
-> staging acceptance
-> approve the same release candidate
-> production backup/preflight
-> production migration/deploy
-> production smoke
```

Production should promote the already accepted artifact, not silently rebuild a
different artifact after staging acceptance.

Staging can legitimately be ahead of Production while a future release is under
acceptance.

## 7. Current staging preservation

The staging history that existed on `fuapay.tul.cz` before the first
production cutover is already protected by the verified archive:

```text
/var/backups/fuapay/fuapay_demo-20260919T182636Z-cc142e23a200e72831284605d8553962b160a984.dump
```

SHA-256:

```text
5642726e5b75e9964482836e048e70363b1d6f5d734e83192236099ef0b97d08
```

The archive has already passed `pg_restore --list` and a full isolated restore
check with 24 migrations and the preserved staging data inventory.

That archive is the source for the first off-host staging restore.

## 8. Immediate priority

The immediate operational priority remains the clean Production launch on
`fuapay.tul.cz`:

- new clean production PostgreSQL database;
- canonical migration chain;
- production bootstrap;
- Entra;
- production-only runtime/secrets;
- ČSOB disabled;
- FUA Print disabled.

The off-host staging runtime and its public integration ingress can be completed
after Production is safely live because the staging state has already been
archived and restore-verified.

The production cutover must not be delayed merely to keep the old staging
runtime running on the production VM.
