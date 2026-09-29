# ČSOB final bank submission checkpoint – 2026-09-29

Status: **mandatory integration acceptance PASS; POS Merchant validation PASS; activation request submitted; ČSOB review pending.**

This is the current operational checkpoint for the bank-activation workstream. It supersedes the older resume instructions in
`docs/testing/csob-final-staging-acceptance-2026-09-25.md` for what to do next, while that older document remains the historical
record of the broader 2026-09-25 acceptance.

## Exact application release used for final bank acceptance

Current final release tested on staging:

`8a9983f938581363899ef8465ec406adf116c749`

Observed staging target:

`/opt/fuapay-staging/current -> /opt/fuapay-staging/releases/8a9983f938581363899ef8465ec406adf116c749`

The local repository checkout used to build the temporary acceptance runner was clean at the same commit.

This release includes the post-2026-09-25 public/payment-presentation follow-up merged by PR #85
(`feat: finish public payment presentation readiness`), including:

- confirmed FUA Pay operational contact `fuapay@tul.cz` in `/Privacy`;
- official Visa and Mastercard acceptance marks at actual card-payment entry points;
- visible credit-top-up guidance with the enforced minimum of 10 CZK and UI-only recommended maximum of 10,000 CZK;
- no change to the payment lifecycle, ČSOB orchestration/reconciliation/settlement model or database schema.

The PR verification recorded 1193/1193 web/application tests PASS, Release build PASS, formatting PASS and no pending EF model change.

## Staging integration profile during final acceptance

Observed non-secret staging configuration:

- `Payments__Provider=Csob`;
- `Csob__Enabled=true`;
- `Csob__ApiBaseUrl=https://iapi.iplatebnibrana.csob.cz/`;
- merchant `M1EPAY2213`;
- return URL `https://fuapay.fa.tul.cz:8443/payments/csob/return`;
- `Csob__PaymentTtlSeconds=1800`;
- `PrintPayments__Enabled=true`;
- `PrintCredentials__Enabled=true`.

`fuapay-staging.service` was active.

The Print feature flags were intentionally left unchanged. The bank acceptance scope was limited to the mandatory ČSOB activation scenarios.

## 1/6 GET echo – PASS

A temporary self-contained echo runner was built from the exact final release.

Runner SHA-256:

`AA345A42B87B49EF39FE56EBF4233411C22C539373E73B372464C6B1B3417460`

Observed result:

- `resultCode=0`;
- `resultMessage=OK`;
- signed response verified.

## 2/6 POST echo – PASS

The same exact-release runner produced:

- `resultCode=0`;
- `resultMessage=OK`;
- signed response verified.

## 3/6 successful authorised payment – PASS

Fresh CardTopUp:

- FUA payment ID: `9b960525-11c6-4fc5-8834-15ad5ac0f0f0`;
- ČSOB payId: `8ad448cf925a@LI`;
- amount: 10 CZK.

Read-only persisted evidence:

- local payment status `3` = `Succeeded`;
- purpose `1` = `CreditTopUp`;
- provider `2` = `Csob`;
- reconciliation state `4` = `Completed`;
- signed/authoritative gateway result `0/7`;
- exactly one credit movement of +1000 minor units;
- exactly one financial document;
- financial document `FUA-2026-000016`, amount 1000 minor units.

## 4/6 customer cancellation – PASS

Fresh CardTopUp:

- FUA payment ID: `573ed6be-3cc9-411e-b679-7902f6ff415b`;
- ČSOB payId: `dc4b56459740@LI`;
- amount: 10 CZK.

The payment was cancelled by the customer on the ČSOB page before entering card data.

Read-only persisted evidence:

- local payment status `5` = `Cancelled`;
- reconciliation state `4` = `Completed`;
- gateway `0/3`;
- zero credit movements;
- zero financial documents.

## 5/6 30-minute expiry – PASS

Fresh CardTopUp:

- FUA payment ID: `949814d2-7528-49b6-ac56-33639b0c0623`;
- ČSOB payId: `8b82656f18c5@LI`;
- amount: 10 CZK.

The browser was left untouched on the ČSOB payment page for the full expiry window.

Read-only persisted evidence:

- local payment status `6` = `Expired`;
- reconciliation state `4` = `Completed`;
- verified browser expiry evidence `130/6`;
- subsequent authoritative server status `0/6`;
- zero credit movements;
- zero financial documents.

This is the official-style 1800-second expiry test, not the earlier 900-second application-default probe.

## 6/6 payment reverse – PASS

Fresh successful CardTopUp:

- FUA payment ID: `e21dfa62-139f-40bb-b092-b22929dece80`;
- ČSOB payId: `7737c978d017@LI`;
- amount: 10 CZK.

After the successful payment, the full original top-up was returned immediately from Admin -> Payments.

Settlement evidence:

- settlement return ID: `fb3acbd8-1548-49a2-b827-b5781bf6294b`;
- kind `3` = `CardTopUp`;
- return state `3` = `Completed`;
- amount 1000 minor units;
- provider attempt ID: `9af2846d-80c9-41f8-b578-5259dbbf4691`;
- provider operation `1` = `Reverse`;
- provider attempt state `3` = `Confirmed`;
- exactly one confirmed Reverse attempt;
- zero Refund attempts.

Audit evidence:

- `settlement-return.card-top-up.reverse-started`;
- `settlement-return.card-top-up.reverse-confirmed`;
- confirmation description:
  `Signed CSOB evidence confirmed resultCode 0 and paymentStatus 5.`

The customer credit history showed the successful +10 CZK top-up followed by the matching -10 CZK return.

This is therefore direct signed `payment/reverse 0/5`, not a Refund fallback.

## Mandatory ČSOB activation set – final result

All six mandatory integration scenarios are complete on the final tested release:

1. GET echo – PASS;
2. POST echo – PASS;
3. successful authorised payment – PASS;
4. customer cancellation – PASS;
5. 30-minute expiry – PASS;
6. payment reversal – PASS.

No Production activation or Production ČSOB credential/config change was performed as part of this final integration acceptance.

## POS Merchant validation and submission – 2026-09-29

The integration POS Merchant validation for merchant `M1EPAY2213` was run by the central/operational colleague.

POS Merchant displayed:

> Všechny podmínky byly úspěšně splněny. Nyní můžete požádat o aktivaci platební brány.

Its validation result showed successful checks for transactions with states:

- `Zamítnuta`;
- `Zrušena`;
- `Autorizace potvrzena/Posláno k zaúčtování`;
- `Reverzováno`.

The POS Merchant page therefore considered the activation prerequisites satisfied.

The colleague then clicked **Odeslat požadavek**.

Current external status:

**activation/readiness request submitted to ČSOB; waiting for the bank's completion/approval email and production-environment instructions.**

Do not describe the production gateway as approved until that response is received.

## Public/payment presentation state

The final tested release contains the public/payment-presentation changes that were still TODOs in the historical 2026-09-25 checkpoint:

- `/Privacy` and `/Terms` are anonymously accessible and linked from the public footer;
- `/Privacy` uses `fuapay@tul.cz` as the application operational contact and keeps `poverenec@tul.cz` as the separate TUL DPO contact;
- card-processing text states that the card is processed on the ČSOB gateway and FUA Pay does not store PAN/card number or CVC/CVV;
- official Visa and Mastercard acceptance marks are present at the actual card-payment entry points;
- top-up UI states the 10 CZK enforced minimum and the 10,000 CZK recommended maximum.

Gateway visual customization (for example FUA-style colours/logo in POS Merchant) remains presentation-only follow-up and is not part of the completed mandatory integration test set.

## Staging state after submission

Do **not** perform the historical 2026-09-25 shutdown/disable closeout merely because the bank submission is complete.

Current operating decision:

- staging may remain running;
- ČSOB integration may remain enabled on staging;
- existing Print functionality/config may remain enabled;
- keep staging pointed to the ČSOB **integration** environment;
- do not replace integration keys/config with Production material.

This preserves a usable regression environment for future payment changes.

## Next action

There is no further bank-integration action to perform now.

Wait for the ČSOB email confirming the result of the review and giving the next production-environment/credential instructions.

When that email arrives:

1. preserve the exact bank instructions and identify the Production API/merchant/key material supplied or requested;
2. keep staging on the integration environment;
3. configure Production separately with Production endpoint and Production signing/verification material;
4. do not copy integration private keys into Production;
5. perform a controlled small real-card Production payment and verify the full FUA Pay financial effect before opening card payments generally;
6. finish/confirm gateway visual branding if desired.

If the FUA Pay payment/ČSOB core changes materially in the future, repeat an appropriate integration regression before Production deployment. A new bank readiness submission is not assumed unless ČSOB requires it for that change or service.

## Clean resume point

As of 2026-09-29:

- final release `8a9983f...` acceptance: **PASS**;
- mandatory ČSOB scenarios: **6/6 PASS**;
- public payment-presentation follow-ups: **present in final release**;
- POS Merchant validation: **PASS**;
- activation request: **SUBMITTED**;
- ČSOB production approval/email: **PENDING**;
- Production activation: **NOT STARTED**.

The next payment-gateway session should start from the ČSOB response email, not by repeating the completed integration acceptance.
