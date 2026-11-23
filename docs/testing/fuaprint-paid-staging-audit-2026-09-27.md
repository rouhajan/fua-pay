# FUA Print Paid staging audit – 2026-09-27

Status: **PASS** for the observed FUA Print -> FUA Pay staging financial/runtime checkpoint.

This checkpoint records read-only evidence supplied from the isolated FUA Pay staging runtime after additional Paid prints from the PC9 canary using the static staging customer profile `development/fua-pay-local/customer-primary` ("Testovací zákazník Alfa").

No production mutation, staging mutation, restart, configuration edit, database write, replay, refund, capture or release command was performed as part of this audit. The database inspection ran inside an explicit `REPEATABLE READ READ ONLY` transaction and ended with `ROLLBACK`.

## Runtime boundary

Observed:

- `fuapay-staging.service`: active;
- systemd enable state: disabled;
- active staging release: `8a9983f938581363899ef8465ec406adf116c749`;
- running executable resolved to the same release;
- Kestrel listener: `127.0.0.1:5081`;
- Nginx listener: `:8443`;
- staging environment SHA-256: `0799a4f6b205524cc266b320dcf151e4ade45b8da2be57b0d23c7237eb432ca1`.

Safe feature flags observed:

```text
Csob__Enabled=false
Database__ApplyMigrationsOnStart=false
Entra__Enabled=false
Payments__Provider=None
PrintCredentials__Enabled=true
PrintPayments__Enabled=true
Receipts__Enabled=true
Receipts__PreviewMode=true
StagingTestMode__Enabled=true
StagingTestMode__InteractiveSignInEnabled=true
StagingTestMode__ResetDataOnStart=false
StagingTestMode__SeedDataEnabled=false
StagingTestMode__SimulatedPaymentsEnabled=false
```

Both staging probes returned `Healthy`:

- `/health/live`;
- `/health/ready`.

## Production guard

Observed unchanged against the documented production baseline:

- production release: `/opt/fuapay/releases/774b324c48d8f874db21f115479f3b317c2a73d0`;
- `/etc/fuapay/production.env` SHA-256: `a2c615cec52e9f9f3498b01212c0d12a38057619dd9676049746a4ca73f9dba5`;
- production Nginx site SHA-256: `491a7228c460ce5c8674a98e6c4c0d0433e0b4fd990360f29cf2d9c9280a91d9`;
- production `/health/ready`: `Healthy`;
- `https://fuapay.fa.tul.cz/`: HTTP 301 to `https://fuapay.tul.cz/`.

No claim is made that production business-data bytes were static; this guard confirms that the audited staging work did not change the production release/config/Nginx boundary.

## Staging firewall boundary

UFW was active. Port `8443/tcp` was admitted only from two explicit IPv4 sources:

- `147.230.21.129` — FUA Print staging acceptance;
- `147.230.72.120` — temporary FUA Pay staging administration.

There was no general `8443/tcp` allow in the observed output. The temporary staging-admin allow remains intentionally present while staging administration/browser work continues and must be removed at final staging-window closeout.

## Database/schema gate

The read-only session confirmed `current_database() = fuapay_staging`.

EF migration state:

- migration count: `26`;
- latest migration: `20260925105051_SupportCardJobPartialRefunds`.

This matches the documented post-2026-09-25 staging schema checkpoint.

## Alfa credit/account evidence

Observed staging identity:

- display name: `Testovací zákazník Alfa`;
- e-mail: `customer.alpha@example.invalid`;
- external identity: `development / fua-pay-local / customer-primary`;
- credit account balance: `119100` minor units = 1,191 CZK.

The latest ledger movement balance was also `119100`; the explicit `balance_consistent` probe returned true.

Recent Alfa print reservations observed:

| Created UTC | Amount | Terminal state |
| --- | ---: | --- |
| 2026-09-27 11:30:34 | 800 | Captured |
| 2026-09-27 10:15:34 | 1800 | Released |
| 2026-09-27 09:21:53 | 200 | Captured |
| 2026-09-26 16:29:55 | 900 | Captured |

The current PC9 acceptance evidence therefore contains three captured charges totalling `1900` minor units = 19 CZK and one 18 CZK reservation that was released without a debit.

The corresponding recent ledger movements are exactly the 8 CZK, 2 CZK and 9 CZK print debits, with each movement description referencing its captured print-reservation ID.

## Global print-reservation integrity

Observed global print-reservation counts:

- `Captured`: 7 reservations, total `9100` minor units;
- `Released`: 3 reservations, total `5400` minor units;
- no `Reserved` rows were present;
- no `ResolutionRequired` rows were present.

Two explicit anomaly queries both returned zero rows:

1. captured reservation without a matching debit movement, or with mismatched account/type/amount;
2. non-captured reservation carrying a debit operation/movement.

This is a direct PASS for the audited capture/debit consistency invariants.

## Audit trail and journal

Recent `audit.events` for actor process `fua-print-payments` showed the expected durable lifecycle pairs:

- `print-reservation.reserved` -> `print-reservation.captured` for successful prints;
- `print-reservation.reserved` -> `print-reservation.released` for the cancelled/released print.

The audit entries carry the same reservation IDs, job UUIDs and amounts as the reservation rows, and captured entries include the corresponding debit-operation IDs.

`journalctl -u fuapay-staging.service -p warning..alert` for the current print-testing window returned `-- No entries --`.

## Conclusion

The observed PC9 Paid canary traffic reached the intended FUA Pay lifecycle:

`Reserve -> physical-print outcome -> Capture/Release`

with these audited properties:

- successful prints create exactly one matching credit debit;
- released prints create no debit;
- Alfa account balance agrees with the latest ledger balance;
- no print reservation is left Reserved or ResolutionRequired;
- staging health is Healthy;
- the staging journal contains no warning/error entries for the inspected window;
- the staging schema is exactly at the expected 26-migration checkpoint;
- the documented production release/config/Nginx guard remains unchanged.

Result: **FUA Print Paid -> FUA Pay staging financial/runtime audit PASS**.

This does not approve broad classroom Paid rollout. PC9 remains the isolated Paid canary while the remaining classroom PCs stay on the accepted Free path until the separate FUA Pay funding/card-payment and rollout gates are complete.
