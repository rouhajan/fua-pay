# Fresh ČSOB staging acceptance checkpoint – 2026-09-25

Status: acceptance scenarios PASS; final staging closeout PASS.

This document is the operational handoff/checkpoint for the isolated FUA Pay staging
acceptance performed on 2026-09-25. It records only directly observed evidence and
keeps unfinished external/operational items explicit.

## Exact release and deployment evidence

Acceptance release:

`e84d851a31083a67f947e4d83b1ff37d2e5871e6`

The release was built from clean `main`; CI/CodeQL and the canonical local verification
were PASS, including 1191/1191 web/application tests and no pending EF model changes.

Staging release archive:

- file: `fuapay-staging-e84d851a31083a67f947e4d83b1ff37d2e5871e6-linux-x64.tar.gz`;
- size: `123774940` bytes;
- SHA-256: `743D275462F7F2B78D3412FE3527737AD51362C077264C2C926370B61FDC3974`.

Migration evidence:

- original idempotent SQL SHA-256:
  `57A2FD2B79FE5F2FEEF28F7236C4F942D4A9DBD9893D3F4446D04952D82E9F22`;
- execution SQL SHA-256:
  `FAAAEA4E886D56EAA27DD57012B0BC2D0CE34F30D5D4741F2AC9877554317394`;
- staging DB after migration: 26 EF migrations;
- last migration: `20260925105051_SupportCardJobPartialRefunds`;
- pre-migration staging backup:
  `/var/lib/postgresql/fuapay_staging-pre-e84d851-20260925T162941Z.dump`;
- backup SHA-256:
  `8af3d1b7db69ddda19683b27bd1f90d6f1f2701fc6687e112055dff24d098a1b`.

Activated staging release:

`/opt/fuapay-staging/current -> /opt/fuapay-staging/releases/e84d851a31083a67f947e4d83b1ff37d2e5871e6`

Release ownership/modes, executable bit and absence of
`appsettings.Development.json` were checked before activation.

## Staging integration profile used by this acceptance

Only the isolated staging runtime was changed:

- `Payments__Provider=Csob`;
- `Csob__Enabled=true`;
- `Csob__ApiBaseUrl=https://iapi.iplatebnibrana.csob.cz/`;
- merchant `M1EPAY2213`;
- return URL
  `https://fuapay.fa.tul.cz:8443/payments/csob/return`;
- `Csob__PaymentTtlSeconds=1800` for the official-style expiry run;
- `PrintPayments__Enabled=false`;
- `PrintCredentials__Enabled=false`.

The original fail-closed staging environment is preserved at
`/etc/fuapay-staging/staging.env.pre-csob-20260925T163512Z` with SHA-256
`3cc93de56b0a4329e331fe2ecf2e476cd90c269e1946650654786434fe2c69d4`.

The acceptance firewall window allowed only client IPv4 `147.230.72.120` to
`8443/tcp`.

## Production guard

No deployment-induced Production release/config/schema change was performed during
this staging acceptance.

Observed Production guard before/after later acceptance probes:

- `/opt/fuapay/current -> /opt/fuapay/releases/774b324c48d8f874db21f115479f3b317c2a73d0`;
- `/etc/fuapay/production.env` SHA-256
  `a2c615cec52e9f9f3498b01212c0d12a38057619dd9676049746a4ca73f9dba5`;
- Production Nginx site SHA-256
  `491a7228c460ce5c8674a98e6c4c0d0433e0b4fd990360f29cf2d9c9280a91d9`;
- `/health/ready = Healthy`;
- Production alias `fuapay.fa.tul.cz:443` remains a 301 redirect to canonical
  `https://fuapay.tul.cz/`.

This evidence does not claim that Production business-data bytes were static while
real users may use the system; it means this deployment/acceptance did not mutate
the Production release, environment, Nginx site or schema/migration state.

## Fresh signed communication gate

A temporary self-contained runner built from the exact release commit used the
staging signing material.

GET echo:

- `resultCode=0`;
- `resultMessage=OK`;
- signed response verified.

POST echo:

- `resultCode=0`;
- `resultMessage=OK`;
- signed response verified.

Echo runner:

- size: `113483584` bytes;
- SHA-256:
  `EB54322BE1287F96FE4C169E3BD4F4690A59BA208D6B169F68890AB11C759A93`.

## Browser/payment acceptance

### Successful CardTopUp

Payment `f94726f8-b3d1-4cd4-b3a6-a26cc7e118b1`,
payId `a462f2f04108@LI`, amount 10 CZK:

- local `Succeeded`;
- reconciliation `Completed`;
- gateway `0/7`;
- exactly one +1000-minor-unit credit movement;
- exactly one financial document was created;
- return/polling completed without manual refresh.

### Customer cancellation

Payment `875e1d5e-4124-4bf9-88e1-163f2a180b75`,
payId `860d7dfa9919@LI`, amount 10 CZK:

- local `Cancelled`;
- reconciliation `Completed`;
- gateway `0/3`;
- no credit movement;
- no financial document.

### Expiry

An initial 900-second run confirmed the application default TTL and expiry behavior.
The official-style 1800-second run used payment
`227c821c-c19e-47a0-b6c2-2de94b99e37b`, payId
`75963db1c6a2@LI`:

- verified browser expiry evidence `130/6`;
- subsequent authoritative server status `0/6`;
- local `Expired`;
- reconciliation `Completed`;
- no credit movement;
- no financial document.

The original browser window was accidentally closed; reopening the already persisted
`process_uri` continued the same payment and did not create a new payment.

## Live return/refund scope

### Fresh CardJob Reverse 0/5

Job `PLT-2026-000012`, payment
`8437d11d-9783-4d4c-b000-f8a2195b6180`,
payId `2e353ac24396@LI`, amount 20 CZK.

Settlement return:

`218f3268-77ab-4899-8619-1218a111ae46`

Result:

- one Reverse attempt;
- signed direct `0/5`;
- return `Completed`;
- original payment/job remain historically succeeded/paid.

### Full CardJob Reverse -> Refund

Historical paid job `3D-2026-000004`, payment
`5b3faa6e-292b-47ee-91b5-d7417eb192aa`,
payId `095401bf4bf9@LI`, amount 120 CZK.

Settlement return:

`6d334293-4850-4c51-80b0-c510a8b25758`

Provider attempts:

1. Reverse: definitively rejected after signed evidence proved settled status 8;
2. Refund: confirmed by direct signed `0/10`.

The single return therefore has two durable provider-attempt rows and ends
`Completed`.

### Repeated partial CardJob Refund

Historical paid job `PLT-2026-000002`, payment
`0f00a7b4-e746-4800-be30-415c7666b2ff`,
payId `0d973b5d9984@LI`, original amount 520 CZK.

Partial return 1:

- return `fd3ac921-9a7e-456a-8dba-f6d11a9150a1`;
- 100 CZK;
- direct signed Refund `0/10`;
- `Completed`.

Partial return 2:

- return `73ff4eb7-758f-47f3-87d5-fea471bca0a4`;
- 50 CZK;
- direct signed Refund `0/10`;
- `Completed`.

Total reserved/completed return amount is 150 CZK and safe remaining amount is
370 CZK.

### CardTopUp full return to original card

Original successful top-up payment
`f94726f8-b3d1-4cd4-b3a6-a26cc7e118b1`,
payId `a462f2f04108@LI`, amount 10 CZK.

Settlement return:

`a4be8709-97b9-4d26-9a7e-d6edc6b7c72b`

Result:

- direct Reverse `0/5`;
- provider attempt `Confirmed`;
- `CreditReturnHold` ended `Consumed`;
- exactly one 1000-minor-unit debit;
- original payment remains historically `Succeeded`;
- return `Completed`.

## Access-isolation live probes

Cross-customer crafted URL probes:

- Alfa requesting a Beta job detail -> HTTP 404;
- Alfa requesting a Beta payment detail -> HTTP 404.

Cross-requester crafted URL probe:

- Requester assigned to 3D Print requesting a Dilna job detail outside assigned
  service-unit scope -> HTTP 404.

These are fresh live browser evidence in addition to automated owner/scope tests.

## Concurrent/double-click CardJob creation

Job `PLT-2026-000013`,
ID `744e32ff-2105-4b7e-a0b5-5f7d102611c1`, amount 30 CZK.

Two browser windows submitted the direct-payment action less than one second apart.

Persisted evidence before cancellation:

- exactly one payment:
  `0d16f122-5cfe-4c49-a80b-a6ce951d8d33`;
- exactly one provider reference:
  `f5e9cffc96be@LI`;
- exactly one PaymentInitiation;
- `payment.created = 1`;
- `payment.provider-initiation.started = 1`;
- `payment.provider-initialized = 1`.

The second request reused the already initialized blocking payment rather than
creating another provider payment.

The one payment was then cancelled through the ČSOB page:

- local `Cancelled`;
- reconciliation `Completed`;
- gateway `0/3`;
- total payments for job remained 1;
- initiation count remained 1;
- financial documents remained 0;
- job remained unpaid and may legitimately be retried later as a new payment.

## Repeated live payment/status over an already Succeeded payment

Target payment:

- FUA payment `54dfc21c-5335-4ea3-a0a7-615d391e945c`;
- payId `e3cb247353c3@LI`;
- 10 CZK CardTopUp;
- one credit movement;
- one document `FUA-2026-000008`.

A temporary self-contained runner built from exact release
`e84d851a31083a67f947e4d83b1ff37d2e5871e6` called the real
`CsobPaymentReconciliationService.ReconcileAsync()` twice. Each invocation made
a fresh signed provider `payment/status` call and then entered the normal
settlement path.

Runner:

- size: `113486704` bytes;
- SHA-256:
  `41BCAF1A99B677DFBA92F2E48E8EA0DA17D330FFAAC65C783F3BFEE5148C300B`.

Observed results:

```text
pass=1 resultCode=0 paymentStatus=8 localStatus=Succeeded stateChanged=False
pass=2 resultCode=0 paymentStatus=8 localStatus=Succeeded stateChanged=False
REPEATED SUCCEEDED PAYMENT/STATUS PASS
```

Before/after DB evidence remained unchanged:

- payment status `Succeeded`;
- payment version `3`;
- payment updated/completed timestamps unchanged;
- reconciliation version `3` and timestamps unchanged;
- exactly one credit movement totaling 1000 minor units;
- exactly one financial document;
- document number remained `FUA-2026-000008`.

This is fresh live provider evidence that repeated status 8 over an already
Succeeded payment is financially idempotent.

## Accounting/reconciliation export

Admin -> Exporty -> `Ucetni CSOB reconciliation` was executed with no date
filter so older original payments with today's returns were included.

Export:

- file `fua-pay-csob-reconciliation-20260925-183134.csv`;
- size `10575` bytes;
- SHA-256:
  `6903af36fcc3215a1eae1c3e3ba2241fec4826f0cb5e260d43ca3395ac91896e`;
- 37 data rows plus header;
- 35 unique payments;
- no exact duplicate rows;
- each populated Provider-attempt ID is unique;
- no customer-name or customer-email columns.

The export contains the expected one-to-many evidence:

- full CardJob Reverse -> Refund as one SettlementReturn with two provider-attempt
  rows;
- two repeated partial CardJob returns as two separate SettlementReturns, each with
  its own Refund attempt;
- CardTopUp return;
- fresh CardJob Reverse.

Audit entry:

- action `export.payment-reconciliation`;
- entity `csv-export`;
- file name `fua-pay-csob-reconciliation-20260925-183134.csv`;
- description records 37 data rows.

## Final read-only gate observed before pause

At 2026-09-25 18:31 UTC:

- `Pending` ČSOB payments: **0**;
- due reconciliation rows: **0**;
- reconciliation rows in `RequiresAttention`: **3**.

All five returns created/used by the 2026-09-25 live acceptance are
`Completed`. Their provider-attempt counts are 1, 2, 1, 1 and 1 as expected.

The three `RequiresAttention` reconciliation rows were not classified before this
checkpoint. They may be historical evidence from prior test windows, but that must
not be assumed. Before bank GO they must be inspected read-only and explicitly
classified. Do not rewrite or delete them merely to make the count zero.

Suggested first command for the next session:

```sql
SELECT
    r.payment_id,
    p.status AS payment_status,
    i.order_number,
    r.provider_reference,
    r.state,
    r.attempt_count,
    r.next_attempt_at,
    r.last_attempt_at,
    r.last_browser_return_at,
    r.last_gateway_payment_status,
    r.last_result_code,
    r.last_error,
    r.created_at,
    r.updated_at,
    r.completed_at
FROM payments.csob_payment_reconciliation r
JOIN payments.payments p ON p.id = r.payment_id
LEFT JOIN payments.payment_initiations i ON i.payment_id = r.payment_id
WHERE r.state = 3
ORDER BY r.updated_at, r.payment_id;
```

## Final staging closeout PASS

The payment acceptance window was closed on 2026-09-25 after one final read-only
gate.

Observed before shutdown:

- `Pending=0`;
- due reconciliation `=0`;
- durable `RequiresAttention=3` rows remain for later read-only classification.

The fail-closed staging environment was restored exactly:

- active file: `/etc/fuapay-staging/staging.env`;
- SHA-256:
  `3cc93de56b0a4329e331fe2ecf2e476cd90c269e1946650654786434fe2c69d4`;
- `Payments__Provider=None`;
- `Csob__Enabled=false`;
- `PrintPayments__Enabled=false`;
- `PrintCredentials__Enabled=false`.

Runtime/network closeout:

- `fuapay-staging.service = inactive + disabled`;
- temporary UFW `8443/tcp` allow removed;
- no listener on `127.0.0.1:5081`;
- temporary ČSOB echo/status acceptance runners removed;
- staging current release intentionally remains
  `/opt/fuapay-staging/releases/e84d851a31083a67f947e4d83b1ff37d2e5871e6`.

Final Production guard after shutdown:

- `/health/ready = Healthy`;
- `/opt/fuapay/current -> /opt/fuapay/releases/774b324c48d8f874db21f115479f3b317c2a73d0`;
- Production env SHA-256
  `a2c615cec52e9f9f3498b01212c0d12a38057619dd9676049746a4ca73f9dba5`;
- Production Nginx site SHA-256
  `491a7228c460ce5c8674a98e6c4c0d0433e0b4fd990360f29cf2d9c9280a91d9`;
- `https://fuapay.fa.tul.cz/` remains HTTP 301 to
  `https://fuapay.tul.cz/`.

The three durable `RequiresAttention` rows are not a reason to reopen the web
runtime. They must be classified read-only before bank GO and must not be deleted
or rewritten merely to make their count zero.

## Bank activation status

The official ČSOB production-activation checklist currently requires the test
scenarios to be performed in the **integration environment**:

- GET echo;
- POST echo;
- successful authorised payment;
- customer cancellation;
- 30-minute expiry;
- payment reversal.

All of those protocol scenarios now have fresh live evidence on the exact
`e84d851...` staging release. The additional FUA Pay scope tested here — full
Refund, repeated partial Refund, CardTopUp return, repeated status idempotence,
access isolation, concurrent creation and reconciliation export — is stricter
than the bank's activation minimum and is application-owned evidence.

The bank's documented submission step is to confirm completion of the scenarios in
the integration ČSOB POS Merchant portal, which submits the tests for readiness
review. After bank approval the app must be reconfigured to the production API and
production signing/verification material. A small real production-card test is
recommended before opening card payments to ordinary users.

## Public pages / external presentation items

Current release exposes `/Privacy` and `/Terms` anonymously. The public footer
links both pages.

The pages currently cover:

- operator/controller context for TUL/FUA;
- TUL postal address and IČ;
- GDPR/DPO contact;
- application operational contact;
- university identity/authentication;
- purpose of data processing;
- job/credit/payment processing;
- CZK;
- character and delivery context of faculty services;
- complaint/return path;
- card processing on ČSOB;
- explicit statement that FUA Pay does not store PAN/card number or CVC/CVV.

Two public/presentation follow-ups remain separate from the tested payment core:

1. `fuapay@tul.cz` is already confirmed and functional. The exact tested release
   `e84d851...` nevertheless still renders `jan.rouha@tul.cz` as the FUA Pay
   operational contact in `/Privacy`. This is a source-content TODO: replace it
   with `fuapay@tul.cz` before the public production-card launch. `/Terms`
   already links to `/Privacy` and does not need to duplicate the operational
   contact;
2. official Visa/Mastercard acceptance marks are not currently documented as
   present in the checkout UI. ČSOB's separate payment-method-presentation
   guidance says to place Visa and Mastercard acceptance marks on the checkout
   page. This is distinct from the mandatory integration test-case list and
   should be finished before public production-card launch using official assets
   and presentation rules.

Merchant-logo customization in POS Merchant is a separate branding feature and is
not one of the listed production-activation test cases.

## Handoff to FUA Print

The payment acceptance window kept both Print feature flags off. FUA Pay release
`e84d851...` contains the existing hardened PrintPayments API and persistent
print-credential code; this payment release did not change the Print contract.

After the canonical payment closeout above, a new **short isolated Print staging
window** may be opened. That window must use the same isolated
`fuapay_staging` database and `https://fuapay.fa.tul.cz:8443`, enable only the
required PrintPayments/PrintCredentials configuration and secrets, and keep
Production untouched.

The intended next acceptance is:

FUA Pay print credential -> authenticated FUA Print reserve -> held physical job ->
Capture/Release/ResolutionRequired according to the actual physical outcome, with
read-only DB evidence proving exactly one debit for a successful print.

Do not enable Print in Production merely because this payment acceptance passed.
The real FUA Print canary/client/broker path must be accepted separately.

## Resume point

The next session should start from this document, not from memory.

Order:

1. inspect/classify the 3 durable `RequiresAttention` reconciliation rows
   read-only while staging remains stopped;
2. change the public FUA Pay operational contact in `/Privacy` from
   `jan.rouha@tul.cz` to the already confirmed and functional
   `fuapay@tul.cz`; do not lose the separate DPO contact
   `poverenec@tul.cz`;
3. finish the remaining public payment-presentation item (official
   Visa/Mastercard acceptance marks) before public production-card launch;
4. hand the closed-state evidence to the FUA Print workstream;
5. open a narrow Print staging window and run the print-code/PIN + actual
   print-for-credit acceptance;
6. before the later bank submission, reopen a fresh payment acceptance window
   only if needed, repeat the official bank scenarios on the final unchanged
   payment release/config, close it cleanly and submit via POS Merchant/central
   TUL operational owner;
7. after ČSOB production activation, install production credentials/config and
   perform one controlled small real production payment before general enablement.
