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
8. The desired document-number semantics are compatible with PostgreSQL non-transactional sequence allocation: an allocated number may be lost on rollback (gap allowed) but must never be recycled. The exact annual implementation is still to be selected and proven by integration tests before merge.
9. Business year for `FUA-YYYY-NNNNNN` is `Europe/Prague`, not raw UTC.
10. Tax/VAT/formal accounting semantics are an external fact, not a coding assumption. They remain fail-closed until confirmed by TUL/accounting.

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

Implement the smallest model capable of storing an immutable financial snapshot without inventing tax semantics.

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
- tax/formal wording remains disabled/fail-closed wherever TUL accounting facts are still unknown.

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

- TUL/accounting approval of VAT/tax treatment, formal document naming and any legally/accountingly required issuer fields;
- exact approved issuer data/config if not already present in authoritative project configuration;
- bank/POS-side ČSOB production activation actions that only the external party can perform;
- actual FUA Print runtime behavior where repository/runtime evidence is not available.

Code may prepare explicit storage/configuration for approved values, but unknown accounting facts must never be filled with defaults from `Receipts` or invented values.

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
- unresolved tax/accounting claims remain fail-closed rather than guessed.

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