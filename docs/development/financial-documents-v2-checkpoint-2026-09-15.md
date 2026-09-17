# FinancialDocuments v2 – implementation checkpoint 2026-09-15

Status: authoritative continuation checkpoint for `feature/financial-documents-v2`.

This file exists so work can resume after a chat/session interruption without reconstructing design decisions from memory. It records only verified repository state, agreed invariants, the next implementation sequence, explicit stop conditions, and the definition of done.

The normative financial-document contract is `docs/features/financial-documents.md`. If this checkpoint and that contract ever conflict, stop and reconcile the documentation before changing runtime code.

## Repository anchors

- Repository: `rouhajan/fua-pay`
- Protected release branch: `main`
- Verified `main` baseline: `7569c46361eaae3b2e0a0e77b982f3e17b3ce520`
- FinancialDocuments feature branch: `feature/financial-documents-v2`
- Verified feature HEAD before this checkpoint: `9033cba5f5161a591b918c66c9d12fd7e9f0f1c6`
- `9033cba...` is the merge of PR #56 (`docs: define FinancialDocuments v2 invariants`) into the feature branch.
- Contract commit from PR #56: `125269bf272f098e6febedfccda2cadde7c73d39`
- No FinancialDocuments runtime/schema implementation was merged by PR #56.

Do not infer commit identity from a Git tree SHA. The verified `main` commit remains `7569c46...`; tree identifiers are not branch HEAD commits.

## Verified pre-implementation state

The current FinancialDocuments feature branch is intentionally a design-contract checkpoint, not a partially deployed runtime feature.

Verified facts from the preceding audit/work:

1. The abandoned prototype that attempted a custom annual numbering table/separate DbContext was removed locally before further work. It generated no migration, was not committed, was not pushed, and was not deployed.
2. The existing `Receipts` module is an on-demand preview/read model. It is not immutable accounting truth and its current VAT/default behavior must not be reused as tax truth for FinancialDocuments.
3. Existing payment settlement already treats the provider result as authoritative and applies money inside application/database transaction boundaries with replay/idempotency protections.
4. Existing payment initiation persists uncertain outcomes instead of treating timeout/unknown as a confirmed failure.
5. Existing administrator manual credit top-up already has command idempotency, canonical credit movement, audit, concurrency handling and rollback tests. FinancialDocuments must preserve these properties.
6. A read-only audit established that a naive outer wrapper around the current `ManualCreditTopUpService` is unsafe as an implementation shortcut. Its current retry/replay and database-error handling assumes its existing transaction boundary. A PostgreSQL error inside a naively nested outer transaction could leave that transaction unusable for the subsequent replay read. The implementation must therefore be designed around the actual service/transaction behavior, not simply wrapped from the outside.
7. The application transaction abstraction does not replace explicit EF `SaveChanges`; exact save/transaction ordering must therefore remain visible and tested in the final implementation.
8. The desired document-number semantics are implemented by the annual
   PostgreSQL counter described in Stage A evidence: an allocated number may be
   lost on rollback (gap allowed) but is never recycled.
9. Business year for `FUA-YYYY-NNNNNN` is `Europe/Prague`, not raw UTC.
10. The approved Stage C correction fixes the title, issuer, numbering and tax
    contract. The document title is `Doklad o úhradě`, the canonical issuer is
    the exact TUL/FUA identity recorded below, and gross amounts include 21 %
    VAT. These facts must be persisted at issuance and must not be read from
    `Receipts` configuration during rendering.

## Local Stage A implementation evidence – 2026-09-16

Stage A was implemented and verified only in the isolated local worktree
`C:\Projects\fua-pay-fd-core` on branch
`wip/financial-documents-v2-core`, based on commit
`0592a275b815f08b7193b90ad684730d13124052`. The changes described in this
section remain uncommitted and have not been pushed, merged, deployed or applied
to staging.

The generated EF migration is
`20260916132427_AddFinancialDocumentsCore`. It creates the dedicated
`financial_documents` schema, an immutable-document persistence table and an
annual counter table. Database uniqueness covers both `DocumentNumber` and
`(SourceType, SourceId)`.

`FinancialDocumentType` is a technical classification separate from source
identity. Its values are `ManualCreditTopUp`, `CardWalletTopUp` and
`DirectJobCardPayment`; `FinancialDocumentSourceType` remains limited to
`ManualCreditTopUp` and `Payment`. Domain validation and a database check
constraint allow only these combinations:

- `ManualCreditTopUp` document -> `ManualCreditTopUp` source, manual settlement,
  no provider and no job snapshot;
- `CardWalletTopUp` document -> `Payment` source, payment-provider settlement,
  provider snapshot present and job snapshot absent;
- `DirectJobCardPayment` document -> `Payment` source, payment-provider
  settlement, provider and job snapshots present.

The immutable nullable issuer snapshot uses only the configuration shape already
defined by the repository: `LegalName`, `UnitName`, `AddressLine1`,
`AddressLine2`, `Country`, `RegistrationNumber`, `VatNumber` and `ContactEmail`.
The snapshot is either wholly absent or all fields are present and nonblank; it
has no defaults. No issuer value, VAT rate, tax treatment or accounting meaning
was copied from Receipts or invented. Stage A had no PDF rendering path; the
Stage C renderer now rejects those schema/render `1/1` documents as
`legacy-incomplete`.

Annual numbering uses one migrated counter row per Prague business year and a
single atomic PostgreSQL
`INSERT ... ON CONFLICT ... DO UPDATE ... RETURNING` statement. The allocator
opens a separate `NpgsqlConnection` with `Enlist=false`; it does not attach the
command to the EF business transaction. Successful allocation therefore
autocommits independently, permits gaps and does not recycle a number when the
business transaction rolls back. `Europe/Prague` determines the year and the
counter fails closed above `999999`.

The migration was applied to the isolated loopback test database
`localhost:5432/fuapay_test_e178cdf` as `fuapay_app`. The repository safety guard
was enabled only for the test process. Canonical
`scripts/verify.ps1 -RunDatabaseTests` passed with:

- Release build: PASS, zero warnings and errors;
- formatting: PASS;
- web/application tests: `967/967` PASS;
- EF pending-model check: PASS;
- PostgreSQL tests: `261/261` PASS.

The nine targeted `FinancialDocumentPersistenceTests` also passed independently.
They proved concurrent allocations unique; one common monotonic annual sequence
by persisting two different document types; the Prague New Year UTC boundary;
non-recycling after an outer business transaction rollback; race-safe first
allocation of a new year; source uniqueness; document-number uniqueness across
different sources; fail-closed exhaustion at `999999`; and full immutable
round-trip of every document field. The round-trip evidence includes a direct-job
payment with non-null issuer, provider reference/payId, provider order/VS and job
snapshots.

No deployment evidence is implied by these local results.

One deployment blocker remains: repository documentation does not establish how
the runtime `fuapay_app` role receives the required schema usage and table DML
privileges for newly migrated, `fuapay_migrator`-owned objects. Do not add
runtime DDL or guessed grants. Resolve and verify the existing privilege model
before staging.

The earlier statement that issuer values, VAT/tax treatment and document naming
still awaited TUL/accounting approval is superseded. The approved values are
recorded in the Stage C correction below. Stage A intentionally kept the fields
nullable so existing schema `1/1` documents remain valid.

At this historical Stage A checkpoint, Stage B was the next implementation
step. The later Stage B evidence below supersedes that status. Staging was not
changed and remains on the previously documented
`9ecee2d9c57d88a2969d42094e49597b41f1642c` release.

## Local Stage B implementation evidence - 2026-09-16

Stage A is anchored at exact commit
`759142351ef3c2e50c4b508e1088632b3fb1ed49`. GitHub CI run #256 and CodeQL
run #260 both passed for that closed stage.

Stage B was implemented and verified locally on branch
`wip/financial-documents-v2-manual-topup`. The work remains uncommitted and was
not pushed, opened as a PR, deployed or applied to staging.

`ManualCreditTopUpService` owns the top-level business transaction through
`IApplicationTransaction.ExecuteTopLevelAsync`. The EF implementation fails
before invoking the callback when a transaction is already active. Ordinary
`ExecuteAsync` retains its join behavior so nested `CreditService` calls use the
owned transaction. Its ordering is deliberate: replay check; stage command;
stage audit; call
`CreditService.CreditAsync`; let `EfCreditAccountRepository.SaveChangesAsync`
flush the credit movement, command and audit; obtain `IssuedAt`; allocate the
document number outside the business transaction; create and stage the immutable
manual-top-up document; explicitly call
`IFinancialDocumentRepository.PersistStagedAsync`; then commit the outer
transaction. `Stage` remains save-free. A canonical manual-document factory owns
the current schema/render version convention instead of duplicating version
literals in Credits.

The repository translates the PostgreSQL unique violation for canonical
`(SourceType, SourceId)` into
`FinancialDocumentSourceAlreadyExistsException`. That exception unwinds through
`EfApplicationTransaction`, which rolls the business transaction back before
`ManualCreditTopUpService` performs any replay read. No query is issued through
an aborted PostgreSQL transaction. A PostgreSQL test starts an ambient
transaction through the same scoped `FuaPayDbContext`, proves that the manual
flow fails before its callback, then successfully executes `SELECT 1` through
the still-usable transaction before rolling it back.

The Admin Credit page keeps its existing active-customer validation, then reads
the existing `AccessUserOption` and maps its `Id`, `DisplayName` and `Email` to a
`FinancialDocumentCustomerSnapshot` at the source boundary. The manual-top-up
flow verifies that the snapshot customer ID equals the command owner. Mutable
name and email are intentionally excluded from command conflict identity.

Migration `20260916150005_AddManualTopUpFinancialDocumentCutover` adds the
non-nullable boolean `credits.manual_topup_commands.financial_document_required`
without a database default. The migration explicitly sets existing rows to
`FALSE` before enforcing `NOT NULL`, so they are pre-cutover legacy commands.
New writers must supply the marker and the Stage B repository explicitly writes
`TRUE`; omitting the column fails at the database boundary rather than silently
classifying a new command as legacy. Exact legacy replay returns the original
result without reading or creating a FinancialDocument and without allocating a
number. A post-cutover command marked `TRUE` must have its canonical document; a
missing document is a fail-closed corruption state and is never backfilled from
current customer data. Ordinary post-cutover replay reuses the original document
and number.

The canonical local gate used the isolated loopback database
`localhost:5432/fuapay_test_e178cdf` and passed:

- Release build and formatting: PASS with zero warnings and errors;
- web/application tests: `970/970` PASS;
- EF pending-model check: PASS, with the cutover migration matching the model;
- PostgreSQL tests: `268/268` PASS;
- targeted `ManualCreditTopUpPersistenceTests`: `9/9` PASS;
- targeted `FinancialDocumentPersistenceTests`: `10/10` PASS.

The Stage B PostgreSQL tests prove one command, movement, audit and document on
success; exact replay with stable document ID/number and no counter advance;
concurrent duplicate processing with one financial effect, one document and one
allocation; rollback before allocation with no counter advance; and immutable
customer snapshot behavior. The deterministic post-allocation failure decorator
first executes the real document `SaveChanges`, then throws before the outer
commit. The test proves that command, movement, audit and document all roll back,
the allocated number remains consumed, and the next successful operation receives
sequence `000002`.

The same targeted suite proves legacy exact replay is a database no-op with no
document or counter allocation; legacy payload mismatch still conflicts; a
post-cutover command missing its document fails closed; all newly persisted
commands carry `financial_document_required = TRUE`; the cutover column is
`NOT NULL` with no database default; a direct SQL insert omitting the marker
fails with `NotNullViolation`; and ambient transactions are rejected before any
command, credit, audit, allocation or document work. A migration-upgrade proof
inserted a pre-cutover row, applied the generated PostgreSQL SQL, and observed
that row as `FALSE` while `information_schema.columns` reported
`is_nullable = NO` and `column_default = NULL`.

The cutover marker is the only Stage B schema change and the migration is purely
additive. Stage B emitted schema/render `1/1` documents without issuer or tax
because the approved contract had not yet been reflected in repository code.
Stage C must therefore update issuance before enabling its renderer; payment
settlement/Stage D was not implemented. Staging remains unchanged.

The deployment privilege blocker remains unresolved: repository evidence still
does not establish how `fuapay_migrator`-owned objects grant the required runtime
access to `fuapay_app`. Do not add runtime DDL or guessed grants. Issuer identity,
VAT/tax treatment, title and numbering are no longer external approval blockers.

## Stage C approved contract correction - 2026-09-16

The following decisions supersede every older statement in this checkpoint that
describes issuer identity, formal naming, numbering or VAT treatment as awaiting
approval:

- title: `Doklad o úhradě` (never invoice or tax-document wording);
- number: persistent `FUA-YYYY-NNNNNN`, one sequence per Prague year;
- issuer: Technická univerzita v Liberci; Fakulta umění a architektury;
  Studentská 1402/2; 461 17 Liberec 1; Česká republika; IČO 46747885;
  DIČ CZ46747885; fua@tul.cz;
- approved VAT rate: 21 %, with `AmountMinorUnits` as gross including VAT;
- the base is rounded once at issuance in minor units using
  `MidpointRounding.AwayFromZero`; VAT is the residual, so base + VAT = gross;
  schema/render `2/2` accepts only this exact mathematical split in both the
  domain and PostgreSQL CHECK constraint, not merely any non-negative values
  with the correct sum and rate;
- issuer and tax values come from one FinancialDocuments-owned approved
  issuance profile and are persisted as immutable snapshots;
- rendering reads no financial value from mutable runtime configuration or
  `Receipts` and performs no tax calculation;
- new documents use schema/render `2/2`; historical `1/1` documents remain
  immutable and replayable but are `legacy-incomplete` for PDF rendering.

The PDF does not show customer identity, provider reference/payId, provider
order/VS, `SourceId`, `PaymentId` or any internal technical reference. Manual
top-up shows `Dobití kreditu`, event date and settlement method. Direct job
payment shows job title, service unit, job number, payment date and settlement
method. Repeated rendering has no database or financial side effect.

## Local Stage C implementation evidence - 2026-09-16

Stage C was implemented locally in `C:\Projects\fua-pay-fd-core` on branch
`wip/financial-documents-v2-pdf-query`, based on exact baseline
`9aa5ab3acd2dfdbeb27389c48ed219660f35174d`. The work remains uncommitted and
has not been pushed, opened as a PR, deployed or applied outside the isolated
local test database.

Generated EF migration `20260917064943_AddFinancialDocumentTaxSnapshot` adds
four nullable, default-free columns: tax treatment, VAT rate in basis points,
tax base minor units and VAT amount minor units. Existing `1/1` rows remain
unchanged and valid. Database constraints require new `2/2` rows to carry the
complete issuer and approved standard-rate-included 2100-basis-point tax
snapshot with nonnegative amounts, the exact approved rounded gross-inclusive
base, residual VAT and `base + VAT = gross`.

`ApprovedFinancialDocumentIssuanceProfile` is the single FinancialDocuments-
owned source of approved issuer/tax values. `ManualCreditTopUpService` snapshots
it only on new issuance. Replay reads the existing document, including
historical issuer-null `1/1` documents, without recalculation or renumbering.

The owner-scoped and admin query paths use EF `AsNoTracking`. Customer and admin
download endpoints are addressed by `DocumentId`, return 404 for missing or
unauthorized records, and send `application/pdf` with private no-store headers.
Only `1/1` and `2/2` are valid canonical domain/version pairs. `2/2` is
dispatched to the renderer, while `1/1` is rejected as legacy-incomplete; the
renderer version dispatcher separately fails closed with a typed reason for
unknown versions. No listing or navigation entry was added.

PDFsharp font initialization and text layout are now process-wide BuildingBlocks
infrastructure shared with legacy `Receipts`. The FinancialDocuments module has
no dependency on `Receipts`; its renderer reads only the immutable document and
the shared logo/font assets. The generated A4 output was rasterized and visually
checked for logo, Czech diacritics, issuer, persisted number, amount rows and
neutral footer. Issuer and detail values use the shared measured text wrapping,
advance the vertical position by their actual line count and fail closed with a
typed layout-overflow reason before producing a PDF that would overlap its
footer.

Canonical `scripts/verify.ps1 -RunDatabaseTests` passed against the isolated
loopback database `fuapay_test_e178cdf` after applying the generated migration:

- Release build: PASS, zero warnings and errors;
- formatting: PASS;
- web/application tests: `1004/1004` PASS;
- EF pending-model check: PASS;
- PostgreSQL integration tests: `272/272` PASS.

## Non-negotiable functional invariants

The full normative list is in `docs/features/financial-documents.md`. The implementation must preserve at least these release-blocking properties:

- Persistent immutable `FinancialDocument` is the financial source of truth; PDF is only a render.
- External cash inflow creates a document:
  - administrator manual credit top-up;
  - successful card wallet top-up;
  - successful direct card payment of a job.
- Spending already loaded wallet credit on a job does not create another external-cash document.
- Administrative credit correction is not a funding receipt.
- Failed, cancelled, expired, uncertain, `RequiresAttention`, or otherwise non-authoritative payment states create no document.
- Browser return is not financial authority.
- Stable source uniqueness must guarantee at most one document for one financial source.
- A document snapshots the data required to remain stable after customer/profile/config changes.
- Document number is independent of job number, `PaymentId`, provider order/VS and provider `payId`.
- Format target: `FUA-YYYY-NNNNNN`.
- One sequence per Prague business year is shared by all document types.
- Allocated numbers are never reused. Gaps are allowed.
- Financial effect and document persistence are atomic from the business operation point of view.
- Re-rendering/downloading a PDF has no financial side effect and allocates no number.

## Exact resume procedure

Before changing runtime code, establish the repository state. Do not assume the developer workstation already contains PR #56.

1. On the workstation, fetch the repository and fast-forward `feature/financial-documents-v2` to the verified remote feature HEAD. Do not merge arbitrary local work into it.
2. Confirm clean worktree, exact branch/HEAD, and `git diff --check`.
3. Run the repository's normal verification gate before the first runtime change. Baseline failures are STOP conditions and must not be attributed to FinancialDocuments.
4. Create a small child implementation branch from the current verified `feature/financial-documents-v2` HEAD. Do not implement directly on `main`.
5. Keep each independently reviewable runtime slice behind CI + CodeQL and merge it only into the FinancialDocuments feature branch after PASS.
6. Keep `main` unchanged until the complete feature is implemented, audited, migrated, tested and staging-accepted.

## First morning read-only map – no coding before this is closed

Inspect the actual current files/callers and record the result before selecting the transaction shape:

- `src/FuaPay.Web/BuildingBlocks/Application/IApplicationTransaction.cs`
- `src/FuaPay.Web/BuildingBlocks/Persistence/EfApplicationTransaction.cs`
- `src/FuaPay.Web/BuildingBlocks/Persistence/FuaPayDbContext.cs`
- `src/FuaPay.Web/Modules/Credits/Application/ManualCreditTopUpService.cs`
- `src/FuaPay.Web/Modules/Credits/Infrastructure/Persistence/ManualCreditTopUpCommandEntity.cs`
- the exact Admin page/handler/caller that invokes manual top-up;
- the exact dependency-injection registration for manual top-up and the affected persistence services;
- `src/FuaPay.Web/Modules/Access/Application/AccessUserReadModels.cs` plus the underlying stable user/profile persistence needed for immutable customer snapshot;
- the current clock/time-zone abstractions, if present;
- payment settlement code and its exact save/commit ordering;
- migration/model-snapshot conventions and the repository command used to generate EF migrations;
- existing database-test fixture/transaction/concurrency conventions.

Do not create a new clock, user model, DbContext, retry helper or transaction abstraction until the repository map proves one is missing.

### Transaction-design acceptance question

Before implementation, write down the exact answer to:

> Which existing top-level application transaction owns the financial operation, where is the FinancialDocument staged, where does `SaveChanges` occur, and how are duplicate/retry database exceptions handled without querying through an aborted PostgreSQL transaction?

If that answer is not explicit from the actual code, STOP. Do not solve it with an unverified wrapper.

## Implementation sequence

### Stage A – persistent FinancialDocument model and numbering foundation

This historical Stage A plan intentionally omitted tax semantics. Stage C adds
the approved immutable v2 tax snapshot without changing existing v1 rows.

Required design outcomes:

- internal `DocumentId` UUID;
- immutable `DocumentNumber`;
- document/source type;
- stable source ID;
- unique database constraint for `(SourceType, SourceId)` or the equivalent verified schema;
- customer internal ID plus immutable customer display-name/email snapshot;
- amount in minor units and currency;
- financial-event timestamp and issued timestamp;
- explicit settlement method;
- approved issuer snapshot fields only;
- provider IDs where applicable;
- job relation/snapshot where applicable;
- schema/rendering version;
- no guessed VAT/accounting values.

Numbering must be implemented using a PostgreSQL mechanism that proves all of the following under database tests:

- concurrent allocations are unique;
- one business-year sequence is shared by document types;
- Prague-year boundary is correct, including UTC instants around midnight/new year;
- a number consumed by a rolled-back transaction is not recycled;
- annual reset/selection is race-safe.

Do not hand-edit a guessed EF migration/model snapshot. Generate the migration with the repository's actual EF tooling after the model is settled, inspect it, run the pending-model check, and include the migration in the same verified slice.

### Stage B – administrator manual credit top-up

This is the first real financial integration slice because the existing path already has strong idempotency/rollback tests.

Target result:

- one successful manual external-money top-up produces exactly one canonical credit effect, command/audit state, and exactly one immutable FinancialDocument;
- replay returns/reuses the existing result and does not allocate another document;
- source mismatch remains a conflict;
- concurrent duplicate requests produce one financial effect and one document;
- injected failure after number allocation and before commit leaves neither committed credit nor committed document; the consumed number may remain a gap;
- no fake `Payment` is created.

The implementation must preserve the current retry/concurrency semantics. Refactor the existing top-level service transaction deliberately if necessary; do not bolt FinancialDocuments onto the outside of it.

### Stage C – immutable PDF/query surface

Only after persistent snapshots are correct:

- add read/query path by document identity/source as required by UI;
- render formal PDF solely from the stored FinancialDocument snapshot;
- repeated download/render must allocate no number and change no financial state;
- changing customer profile/config after issuance must not change already-issued output;
- dispatch is explicit: only schema/render `2/2` renders; `1/1` fails as
  `legacy-incomplete` and every unknown version fails as unsupported.

Existing Receipts rendering code may be reused only as presentation infrastructure after review. It must not become the data source for FinancialDocuments.

### Stage D – payment settlement integration

Integrate document creation into the existing authoritative settlement transaction, not browser-return handling.

Required outcomes:

- successful card wallet top-up -> one financial document;
- successful direct job card payment -> one financial document;
- wallet credit spend -> no second external-cash document;
- replay/repeated provider status -> no duplicate document or financial effect;
- cancelled/expired/failed/uncertain/RequiresAttention -> no document;
- provider identifiers and job snapshot are captured from the successful operation while immutable;
- existing payment idempotency and unknown-state behavior remain unchanged.

### Stage E – accounting/admin read model

Expose only the views actually required for operation/audit. At minimum preserve the ability to trace:

`FinancialDocument -> source (manual top-up/payment) -> payment/job where applicable -> provider order/VS/payId where applicable`.

Do not turn a reporting requirement into a second financial source of truth. Unspent customer credit must remain derivable/reconcilable from the canonical wallet/ledger model.

### Stage F – full automated gate and independent adversarial review

Before staging:

- repository verify script PASS;
- all application/web/database tests PASS;
- Release build PASS;
- formatting/analyzers PASS;
- EF pending-model check PASS;
- Git diff/check hygiene PASS;
- GitHub CI PASS;
- CodeQL PASS;
- dedicated FinancialDocuments adversarial tests PASS;
- independent final review specifically searches for:
  - credit without document;
  - document without credit;
  - duplicate document/source;
  - number recycling;
  - document on failed/uncertain payment;
  - mutable data used when rendering an issued document;
  - document creation during PDF download;
  - concurrency/retry paths that leave inconsistent state;
  - double document for direct-card versus later wallet spend.

### Stage G – staging migration/deploy/acceptance

Staging must not be described as current until explicitly redeployed and verified.

Last documented staging release before this feature work: `9ecee2d9c57d88a2969d42094e49597b41f1642c`.

After the complete feature branch passes automation:

- backup/rollback prerequisites as required by the existing deployment runbook;
- apply the reviewed migration in staging;
- deploy exact commit SHA;
- verify health;
- repeat manual top-up acceptance and inspect DB evidence;
- exercise card wallet top-up and direct job payment in the allowed ČSOB environment;
- prove failed/cancelled/expired/non-authoritative flows create no document;
- download the same PDF repeatedly and verify no DB/financial changes;
- verify immutable snapshot behavior after profile/config changes;
- desktop + mobile smoke for any affected UI.

Record exact deployed SHA and evidence in documentation before considering staging PASS.

## Work after FinancialDocuments – route to final FUA Pay completion

FinancialDocuments is not the last project milestone. The remaining planned path to a complete FUA Pay is:

### 1. FUA Print real runtime integration

FUA Pay remains the sole financial authority. The existing PrintPayments API contract stays disabled until real runtime readiness is demonstrated.

Required work:

- read-only audit of actual current FUA Print runtime/code;
- preserve the proven CUPS hold + command-broker security boundary unless a reviewed change is required;
- close durable `jobUuid` recovery gaps;
- use stable Entra `tid+oid` where available, or the defined persistent email + print credential flow;
- implement durable FUA Print API client with stable command IDs and recovery by job UUID;
- configure source/service credentials securely;
- staging E2E for enough credit, insufficient credit, duplicate request, lost response, restart, success, failure and ambiguous physical outcome;
- prove at-most-one debit before enabling the feature.

### 2. Remaining ČSOB production acceptance

Already-proven flows must not be repeated as assumptions; remaining release acceptance must be explicitly closed.

Known remaining acceptance items include:

- fresh echo immediately before production activation;
- POS/Merchant confirmation and bank-side production activation;
- decline/failed acceptance;
- duplicate browser return;
- lost return;
- restart while pending;
- repeated status/reconciliation;
- abandoned pending -> terminal behavior;
- happy/cancel/expired UX;
- mobile + desktop smoke.

### 3. Remaining release UX checks

Explicitly verify rather than assume:

- whether a newly assigned job appears automatically in an already-open customer session or requires reload;
- exact first-login/onboarding behavior for a new customer.

Any defect found becomes a normal isolated tested slice; do not mix unrelated UX changes into FinancialDocuments persistence work.

### 4. Final whole-repository/release audit

After all features are complete:

- security/authz/authn review;
- financial/idempotency/concurrency review;
- database migration/state review;
- secrets/configuration review;
- logging/audit/privacy review;
- dependency/static-analysis/CodeQL review;
- staging acceptance from the exact release candidate SHA;
- documentation closeout;
- only then PR the complete verified feature/release changes to `main` under the protected-branch rules.

## External blockers / facts that must not be guessed

These are legitimate STOP points, not reasons to weaken the implementation:

- bank/POS-side ČSOB production activation actions that only the external party can perform;
- actual FUA Print runtime behavior where repository/runtime evidence is not available.

Approved issuer and tax values must be snapshotted from the canonical
FinancialDocuments issuance profile. No value may be filled from `Receipts` or
computed from mutable configuration during rendering.

## Definition of done – FinancialDocuments v2

FinancialDocuments v2 is done only when all of the following are true:

- immutable persistent model + real reviewed migration exist;
- annual non-recycling numbering semantics are proven against PostgreSQL;
- manual top-up, card wallet top-up and direct card job payment create exactly the required document atomically;
- wallet spend/corrections/non-authoritative payments create no inappropriate document;
- source replay and concurrency cannot duplicate documents or money;
- PDF is rendered from immutable snapshot and is side-effect free;
- required audit/query traceability exists;
- all automated gates and adversarial tests pass;
- CI and CodeQL pass on the exact feature head;
- staging migration/deploy/manual acceptance pass on an exact documented SHA;
- documentation reflects the implementation, not merely the design;
- renderer version dispatch and incomplete legacy documents remain fail-closed.

## Definition of done – complete FUA Pay release

The project-level release is not called complete until, in addition to FinancialDocuments:

- FUA Print real E2E is complete and safe to enable;
- remaining ČSOB production acceptance/activation is closed;
- remaining release UX checks are resolved;
- full final security/financial/repository audit passes;
- exact release candidate is staging-accepted;
- final documentation/checkpoint is committed;
- protected `main` receives the reviewed passing release change.

## If the chat/session is lost

Resume from this file and `docs/features/financial-documents.md`, then verify Git before trusting any remembered state.

Minimum recovery sequence:

1. fetch remote refs;
2. inspect `main` and `feature/financial-documents-v2` exact SHAs;
3. confirm worktree cleanliness and local divergence;
4. read the latest FinancialDocuments checkpoint/closeout docs in Git;
5. inspect open PR/check status before writing code;
6. continue at the first incomplete stage above;
7. never infer successful migration/deploy/CI/staging from conversation history alone.

Repository, tests, migrations, CI/CodeQL evidence and deployment evidence are source of truth. Conversation memory is only a navigation aid.
