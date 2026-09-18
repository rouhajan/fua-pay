# FinancialDocuments v2 Stage D – staging acceptance 2026-09-18

Status: core Stage D staging acceptance PASS on exact runtime SHA
`a6d012012679992e221769d8719460a81cb88e29`. Mobile smoke was explicitly
deferred and is not claimed as passed.

This file is the durable evidence for the 2026-09-18 deployment and live
acceptance of card-backed FinancialDocuments flows. It records observed
repository, artifact, database and runtime facts. It does not replace the
normative contract in [`../features/financial-documents.md`](../features/financial-documents.md).

## Repository and PR baseline

Before deployment:

- local branch: `feature/financial-documents-v2-stage-d`;
- local HEAD and remote feature HEAD:
  `a6d012012679992e221769d8719460a81cb88e29`;
- remote `main`:
  `c2d2ac19e29f8407c31e634275fe0f27f5c35fb1`;
- divergence `origin/main...HEAD`: `0 3`;
- worktree: clean;
- `git diff --check`: PASS;
- PR #59 title:
  `feat: complete FinancialDocuments v2 card settlement flow`;
- PR #59 was intentionally kept in Draft during staging acceptance;
- PR checks over runtime SHA `a6d0120...` were green before deployment
  (CI and CodeQL PASS; GitHub showed 9/9 checks).

The complete Stage D implementation was adversarially reviewed before staging.
No blocking code finding was identified in the reviewed runtime scope.

## Staging preflight

Immediately before deployment the staging VM reported:

- active release:
  `/opt/fuapay/releases/c2d2ac19e29f8407c31e634275fe0f27f5c35fb1`;
- running executable matched that release;
- `fuapay.service`: `active`;
- `/health/ready`: `Healthy`;
- ČSOB reconciliation worker: `Healthy`, no failed cycle;
- public smoke: canonical HTTPS `200`, canonical HTTP `301`, alternate HTTPS
  `301`;
- configuration file: `/etc/fuapay/staging.env`, owner/mode
  `root:fuapay 640`;
- `Database__ApplyMigrationsOnStart=false`;
- database: `fuapay_demo`;
- database owner: `fuapay_migrator`.

The staging database had exactly 24 applied EF migrations and the repository at
`a6d0120...` also contained exactly 24 migrations. The latest three were:

- `20260916132427_AddFinancialDocumentsCore`;
- `20260916150005_AddManualTopUpFinancialDocumentCutover`;
- `20260917064943_AddFinancialDocumentTaxSnapshot`.

Therefore Stage D itself required no new schema migration and no migration SQL
was executed during this deployment.

FinancialDocuments schema preflight:

- `financial_documents.documents` owner: `fuapay_migrator`;
- `financial_documents.number_counters` owner: `fuapay_migrator`;
- `fuapay_app` privilege probe returned
  `t|t|t|t|t|t` for schema usage and required SELECT/INSERT/UPDATE table
  privileges;
- baseline FinancialDocuments count was `1|1`: one document total and one
  current schema/render `2/2` document.

PostgreSQL roles `fuapay_app` and `fuapay_migrator` remained non-superuser,
without CREATEDB or CREATEROLE.

## Release artifact evidence

Canonical local verification on exact SHA `a6d0120...`:

- `scripts/verify.ps1`: PASS;
- Release build: PASS;
- formatting: PASS;
- web/application tests: `1020/1020` PASS;
- EF pending-model check: PASS;
- locked `linux-x64` restore: PASS;
- self-contained Release publish: PASS;
- deterministic release create + verify: PASS;
- migration prepare + verify: PASS;
- repository remained clean after artifact creation.

Release archive:

- file:
  `fuapay-staging-a6d012012679992e221769d8719460a81cb88e29-linux-x64.tar.gz`;
- bytes: `123511732`;
- SHA-256:
  `61c4ac9d9de8036efc8e1b3874443a18a75899ebc9765c94174f319d4738fc01`.

Migration SQL artifact:

- bytes: `94735`;
- SHA-256:
  `d2770fb951aac6721c0ff416dde15b16be57070f6e9ec67d9fc96d52209cbfc4`.

Migration execution artifact:

- bytes: `94760`;
- SHA-256:
  `76dbf47e51a9878b5947544eec34ed34915d785dcdc35020f0050f4623610e88`;
- first line: `SET ROLE "fuapay_migrator";`;
- execution/original relation verification: PASS.

After upload, server-side size and all three SHA-256 checks matched exactly.
`gzip -t` and full tar listing passed. The private upload directory was
normalized to mode `0700` and all three uploaded files to `0600`.

## Pre-deploy backup

A fresh PostgreSQL custom-format backup was created before activation:

- path:
  `/home/rouha/fuapay_demo-pre-stage-d-20260918T123109Z.dump`;
- owner: `rouha:rouha`;
- mode: `0600`;
- bytes: `221952`;
- SHA-256:
  `28babff27bb17c3da3eef6d2646eed75d7582fd38e7d5ed205f5b10cacf7d7af`;
- `pg_restore --list`: PASS.

Immediately after the backup the database still reported 24 migrations and
application readiness remained `Healthy`.

## Side-by-side installation and activation

The exact release was installed beside the running release at:

`/opt/fuapay/releases/a6d012012679992e221769d8719460a81cb88e29`

Pre-activation checks passed:

- all release content owned by `fuapay:fuapay`;
- all directories mode `0770`;
- ordinary files mode `0660`;
- `FuaPay.Web` mode `0750`;
- executable check as service account: PASS;
- active release remained the old `c2d2ac19...` until the explicit activation.

Activation used an atomic `/opt/fuapay/current` symlink switch followed by a
service restart with bounded automatic code rollback if health gates failed.
No rollback was needed.

Activation gates:

- first readiness attempt: no response during startup;
- second readiness attempt: `Healthy`;
- ČSOB worker first checked state: `Healthy`;
- running executable exactly:
  `/opt/fuapay/releases/a6d012012679992e221769d8719460a81cb88e29/FuaPay.Web`;
- service: `active`.

Post-activation gate:

- current release exactly `a6d0120...`;
- readiness: `Healthy`;
- ČSOB reconciliation worker: `Healthy`, no failed cycle;
- canonical HTTPS: `200`;
- canonical HTTP: `301`;
- alternate HTTPS: `301`;
- database migrations remained exactly `24`;
- FinancialDocuments state remained `1|1` immediately after deployment;
- `journalctl --unit=fuapay.service --priority=err --since='10 minutes ago'`:
  no entries.

The host also reported pending OS updates and `System restart required`.
Operating-system maintenance was deliberately not mixed into this financial
application deployment.

## Live acceptance – card wallet top-up

A fresh customer card top-up of 137 Kč was executed through the real ČSOB
integration environment.

Payment:

- PaymentId:
  `4e94bc30-375a-4218-a2e8-94060a0d982a`;
- purpose: `CreditTopUp`;
- provider: ČSOB;
- state: `Succeeded`;
- amount: `13700` minor units;
- provider reference: `768e5bc33647@LI`;
- completed:
  `2026-09-18 12:39:59.578718+00`.

Canonical credit effect:

- operation ID exactly equals PaymentId;
- movement type: credit;
- amount: `13700`;
- resulting test-account balance: `45900`;
- recorded at:
  `2026-09-18 12:39:59.500262+00`.

FinancialDocument:

- DocumentId:
  `4897d40a-1fef-4800-ac13-ce799bfdc330`;
- number: `FUA-2026-000002`;
- document type: `CardWalletTopUp`;
- source type: `Payment`;
- source ID exactly equals PaymentId;
- amount: `13700`;
- settlement method: payment provider;
- provider snapshot: `Csob`;
- provider reference: `768e5bc33647@LI`;
- provider order number: `18`;
- financial event:
  `2026-09-18 12:39:59.500262+00`;
- issued:
  `2026-09-18 12:39:59.591414+00`;
- tax base: `11322`;
- VAT: `2378`;
- VAT rate basis points: `2100`;
- schema/render: `2/2`.

The live uniqueness probe returned exactly `1|1|1` for payment, credit movement
and FinancialDocument.

The generated PDF visibly contained:

- `Doklad o úhradě`;
- `FUA-2026-000002`;
- approved issuer snapshot;
- purpose `Dobití kreditu`;
- payment method `Platební karta`;
- gross `137,00 Kč`;
- base `113,22 Kč`;
- VAT 21 % `23,78 Kč`;
- total `137,00 Kč`.

### PDF/read idempotence

Before repeated reads the probe was:

`2|1|1|2`

meaning:

- 2 FinancialDocuments total;
- exactly one document for this PaymentId;
- exactly one credit movement for this PaymentId;
- 2026 document counter at 2.

After multiple PDF downloads interleaved with page refreshes the probe remained
exactly:

`2|1|1|2`.

Therefore repeated page reads and PDF downloads created no new document, credit
effect or document number.

## Live acceptance – direct card job payment

A fresh test job was deliberately created to avoid contamination by an older
stuck payment attempt:

- job number: `PLT-2026-000011`;
- title: `FD Stage D direct card`;
- service unit: `Plotr`;
- amount: 100 Kč.

A new direct card payment redirected to the real ČSOB integration page and
settled successfully.

Payment:

- PaymentId:
  `10c4dcf8-1bb1-433b-aad4-f410c071878a`;
- purpose: `Job`;
- JobId:
  `ff48164f-061d-4703-a1f4-0eb61c5d4eef`;
- provider: ČSOB;
- state: `Succeeded`;
- amount: `10000`;
- provider reference: `5dca1bb8b44f@LI`;
- completed:
  `2026-09-18 12:53:15.307456+00`.

Job settlement:

- job: `PLT-2026-000011`;
- payment status: paid;
- settlement type: `DirectPayment`;
- settlement reference exactly equals PaymentId;
- settled at:
  `2026-09-18 12:53:15.287521+00`.

FinancialDocument:

- DocumentId:
  `38f466c6-fed4-4e84-96c1-7bd9f551ed01`;
- number: `FUA-2026-000003`;
- document type: `DirectJobCardPayment`;
- source type: `Payment`;
- source ID exactly equals PaymentId;
- amount: `10000`;
- settlement method: payment provider;
- provider: `Csob`;
- provider reference: `5dca1bb8b44f@LI`;
- provider order number: `19`;
- job snapshot:
  `ff48164f-061d-4703-a1f4-0eb61c5d4eef | PLT-2026-000011 | FD Stage D direct card | Plotr`;
- financial event exactly equals job `settled_at`:
  `2026-09-18 12:53:15.287521+00`;
- issued:
  `2026-09-18 12:53:15.307644+00`;
- tax base: `8264`;
- VAT: `1736`;
- VAT rate basis points: `2100`;
- schema/render: `2/2`.

The live uniqueness probe returned exactly `1|1|1` for payment, direct job
settlement and FinancialDocument.

The generated PDF visibly contained:

- `FUA-2026-000003`;
- purpose `Úhrada zakázky`;
- title `FD Stage D direct card`;
- service unit `Plotr`;
- job number `PLT-2026-000011`;
- payment method `Platební karta`;
- gross `100,00 Kč`;
- base `82,64 Kč`;
- VAT 21 % `17,36 Kč`;
- total `100,00 Kč`.

## Negative financial-document boundary

A staging-wide read-only query grouped non-successful ČSOB payments and joined
them to FinancialDocuments:

- Failed: `5` payments, `0` documents;
- Cancelled: `2` payments, `0` documents;
- Expired: `1` payment, `0` documents.

A second query over all payment-backed documents returned `0` documents whose
payment status was anything other than `Succeeded`.

This directly confirms on current staging data that non-authoritative payment
states do not have canonical FinancialDocuments.

## Existing manual top-up document

Before today's card-flow tests staging already contained one canonical
FinancialDocument from the earlier manual top-up acceptance:

- number: `FUA-2026-000001`;
- schema/render: `2/2`.

The 2026-09-18 card acceptance therefore continued the same annual number
sequence with `000002` and `000003`.

## UX observation – document link after asynchronous settlement

The payment status poller updates an already-open detail from Pending to
Succeeded without a full reload. It does not currently inject the newly-created
FinancialDocument block into that already-rendered HTML.

Observed direct-card behavior:

1. return page changed to `Uhrazená` through polling;
2. the FinancialDocument block was not yet visible;
3. one normal page refresh displayed `Doklad o úhradě -> Stáhnout PDF`.

This is a known presentation limitation, not a financial consistency defect.
The canonical document already existed after authoritative settlement.

## Separate release rest found during acceptance

An older test job exposed a real payment-recovery edge case unrelated to Stage D
document issuance:

- job: `PLT-2026-000010`;
- JobId:
  `c230336b-49ba-401d-94e7-f6ac950cd491`;
- old PaymentId:
  `10db109f-97db-4d95-9031-c7e93f6a4b3d`;
- payment status remained `Created`;
- provider: ČSOB;
- provider reference: null;
- PaymentInitiation state: `Uncertain`;
- order number: `15`;
- last error:
  `Výsledek zahájení platby u poskytovatele nebyl lokálně potvrzen.`;
- process URI: null;
- observed provider reference: null;
- created:
  `2026-09-13 13:00:59.027964+00`;
- initiation finished:
  `2026-09-13 13:01:29.205018+00`.

Because `Created` is a blocking job-payment status and
`InitializeIfPreparedAsync` only reinitializes a `Prepared` initiation, a new
"Zaplatit přímo" action on that old job resolves back to the existing stuck
payment detail instead of creating a fresh payment.

No row was deleted or manually changed during acceptance. A fresh job was used
for Stage D proof. This is tracked as a separate payment/recovery release rest
and must not be misclassified as a FinancialDocuments defect.

## Deferred / not claimed

The following Stage G items are deliberately not claimed as passed by this
evidence:

- mobile UI smoke: explicitly skipped on 2026-09-18;
- a live mutation test proving an already-issued document remains unchanged
  after later customer-profile/config changes was not repeated during this
  session; the invariant is covered by implementation/tests but not by this
  live acceptance;
- remaining project-level ČSOB production activation scenarios remain governed
  by `docs/integrations/csob-production-readiness.md`.

Core desktop Stage D staging acceptance is PASS. FinancialDocuments v2 should
not be called fully project-released until deferred acceptance items and the
project-level release definition are closed.

## Next work – FUA Print

After recording this evidence, work intentionally moves to the separate FUA
Print runtime integration. FUA Pay remains the sole financial authority. Existing
PrintPayments and PrintCredentials features must remain disabled until the real
FUA Print runtime/client, service identity/secret configuration and end-to-end
credit lifecycle are audited and proven.

The next intended acceptance path is the real user flow based on persistent
email + print credential ("tiskový kód"), followed by held print job,
credit reservation/capture/release and failure/recovery tests.
