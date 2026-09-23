# Card job concurrent creation verification - 2026-09-23

## Scope

Ověření souběžného vytvoření přímé karetní platby jedné zakázky
(`CardJob`) proti skutečnému PostgreSQL.

Cílem bylo ověřit konkrétní concurrency gap identifikovaný při auditu:
dva souběžné požadavky na vytvoření přímé platby stejné publikované
zakázky nesmějí vytvořit dvě finanční cesty.

Toto není živý test ČSOB sandboxu ani produkční test.

## Source baseline

- repository: `rouhajan/fua-pay`
- base `main`: `673db6503f7beca4848c890323275f063cd91ce3`
- working branch: `test/card-job-concurrent-create`
- production and staging were not modified or used

## Database safety preflight

Test byl spuštěn pouze proti lokálnímu PostgreSQL:

- PostgreSQL: `18.4`
- host: `localhost`
- port: `5432`
- database: `fuapay_test_e178cdf`
- login: `fuapay_app`
- `fuapay_app`: not superuser, no `CREATEDB`
- active connections before test: `0`

Databáze splňuje repository safety policy pro PostgreSQL integrační
testy (`fuapay_test_*`, loopback host, explicit opt-in).

Před testem byl celý EF migration history porovnán s aktuálními
migračními soubory v repository:

- repository migrations: `25`
- database migrations: `25`
- latest migration: `20260919112723_AddLegacySafeQCreditTransfers`
- comparison result: `EXACT MATCH`

## Test

Do
`tests/FuaPay.DatabaseTests/JobPaymentCoordinationPersistenceTests.cs`
byl přidán test:

`ConcurrentDirectPayments_CreateSinglePayment`

Test záměrně koordinuje dvě souběžná volání vytvoření platby tak, aby druhé
pokusilo získat skutečný PostgreSQL job lock v době, kdy první volání lock
stále drží.

Ověřuje:

- oba requesty skončí nad stejným `Payment.Id`;
- přesně jeden výsledek je `ExistingPayment`;
- přesně jeden výsledek není `ExistingPayment`;
- pro zakázku existuje právě jeden řádek v `payments.payments`;
- pro zakázku existuje právě jeden řádek v `payments.payment_initiations`;
- zakázka zůstane `Published`;
- zakázka zůstane `Unpaid`;
- zakázka není zrušena;
- kredit zákazníka se nezmění.

Testovací scénář používá vlastní náhodná ID a v `finally` provádí cleanup.

## Result

Targeted PostgreSQL test:

```text
celkem: 1
selhalo: 0
úspěšné: 1
přeskočeno: 0
doba trvání: 3,2 s
```

Result: **PASS**

## Interpretation

Pro ověřený scénář dvou souběžných požadavků na přímou platbu stejné
zakázky současná PostgreSQL koordinace zabránila vytvoření druhé
finanční cesty.

Na základě tohoto výsledku nebyla pro tento konkrétní concurrency scénář
provedena změna produkční aplikační logiky.

Tento test není živým důkazem chování ČSOB gateway a přímo nepočítá
provider HTTP initialization calls. Tyto vrstvy ověření jsou samostatné.


## Full local verification gate

Po targeted concurrency testu prošel také standardní repository verification gate
s aktivovanými PostgreSQL integračními testy proti stejné ověřené lokální
testovací databázi.

- Release build: PASS
- formatting verification: PASS
- web/application tests: `1092/1092` PASS
- EF model pending changes: none
- PostgreSQL integration tests: `288/288` PASS
- `git diff --check`: PASS

Staging ani Production nebyly při tomto ověření použity nebo změněny.
