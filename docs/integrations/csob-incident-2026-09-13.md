# ČSOB integration incident — 2026-09-13

Status: OPEN / external integration blocker

Tento dokument zachycuje staging incident z 2026-09-13. Neurčuje root cause bez
důkazu a nemění stabilní bezpečnostní kontrakt z [`csob.md`](csob.md). Aktuální
production-readiness stav je v
[`csob-production-readiness.md`](csob-production-readiness.md) a aktuální staging
runtime v [`../deployment/demo-staging.md`](../deployment/demo-staging.md).

## Shrnutí

Release `bc868276aead1b350033303ce8e80bc6ce9a5894`, který obsahuje hardening
ČSOB expired-payment reconciliation z PR #44, prošel lokálním release gate,
aplikací dvou forward-only migrací i staging deployment mechanikou. Následný
funkční payment smoke ale opakovaně skončil timeoutem na ČSOB integration
`payment/init`.

Pro odlišení regrese aplikace od externího problému byl staging řízeně vrácen
pouze kódem na předchozí live-accepted release
`768aec26c72bc77ca43d554c8e8bab20f60678b6`; databázové schema se nevracelo.
Stejný čerstvý `payment/init` timeout se poté reprodukoval i na tomto předchozím
release. Následné signed `echo` diagnostiky se stejným merchantem a klíčem také
opakovaně timeoutovaly bez jediného bajtu odpovědi, zatímco úmyslně neplatný POST
na stejný `/api/v1.9/echo` endpoint dostal HTTP 400 za přibližně 62 ms.

Nejpravděpodobnější pracovní hypotéza je proto problém v ČSOB integration eAPI
nebo ve zpracování konkrétního integračního merchantu. Root cause zatím není
potvrzen ČSOB a dokument jej za potvrzený neprohlašuje.

## Release a databáze

Git `main` po merge PR #44:

`bc868276aead1b350033303ce8e80bc6ce9a5894`

Předchozí live-accepted release:

`768aec26c72bc77ca43d554c8e8bab20f60678b6`

Release artifact pro `bc868276...`:

- soubor: `fuapay-staging-bc868276aead1b350033303ce8e80bc6ce9a5894-linux-x64.tar.gz`;
- velikost: `123064686` bytes;
- SHA-256: `BA05CB479A6905FF749212355AB655D5363DDD5D9A6AC99673FE78FF74995957`;
- canonical archive verification PASS;
- release instalován vedle aktivního release do
  `/opt/fuapay/releases/bc868276aead1b350033303ce8e80bc6ce9a5894`.

Před migrací vznikl validovaný PostgreSQL custom dump:

`/var/backups/fuapay/fuapay_demo-before-bc868276-20260913T124118Z.dump`

SHA-256:

`4c7ea98e4b56906478b213a565f9afc4e537542e606c0b5ea4462969225fd939`

Backup prošel kontrolou `pg_restore --list` i úplnou restore-validací do
`/dev/null`.

Do staging DB byly úspěšně aplikovány dvě nové forward-only migrace PR #44.
Po migraci měla databáze 19 aplikovaných EF migrací; nejnovější byly:

1. `20260913111715_AddCsobExpiryFailureProvenance`;
2. `20260912145956_AddCsobVerifiedExpiryReturnEvidence`.

Automatické migrace při startu zůstaly vypnuté. Při pozdějším code rollbacku se
schema záměrně nevracelo.

## Deployment `bc868276...`

Deployment mechanika před funkčním smoke prošla:

- atomický switch `/opt/fuapay/current` na `bc868276...`;
- `fuapay.service` active;
- readiness po bounded retry `Healthy`;
- ČSOB reconciliation worker `Healthy`;
- běžící executable odpovídal novému release;
- canonical HTTPS HTTP 200;
- HTTP a alternate-host redirecty PASS.

Tím prošel artifact/schema/deployment gate. Funkční platební acceptance ale
neprošla a release nebyl prohlášen za live-accepted.

## Payment-init selhání na novém release

Na novém release byly provedeny dva čerstvé uživatelské pokusy. Oba skončily po
přibližně 30 sekundách generickou chybovou stránkou.

První pokus:

- CardJob `PLT-2026-000008`;
- job `fe85c629-4d65-4bfb-9620-5559a61a3226`;
- payment `8867ae06-2bdb-460c-8769-265dd7c9a9a3`;
- `payment/init` začal `2026-09-13 12:50:29.279057 UTC`;
- po přibližně 30 s skončil `CsobGatewayException` nad
  `HttpClient.Timeout` / `TaskCanceledException`;
- provider reference ani process URI nebyly lokálně potvrzeny;
- initiation skončila `Uncertain` s uloženou chybou, že výsledek zahájení platby
  u poskytovatele nebyl lokálně potvrzen.

Druhý čerstvý pokus na `bc868276...` vykázal stejný uživatelský symptom po
přibližně 30 sekundách. Po druhém selhání už nebyl na novém release vytvářen
žádný další payment-init test.

Nejasné init pokusy se nesmějí slepě opakovat; zůstávají jako audit/recovery
evidence podle existujícího ČSOB kontraktu.

## Řízený code-only rollback a A/B ověření

Staging byl atomicky vrácen pouze kódem na:

`/opt/fuapay/releases/768aec26c72bc77ca43d554c8e8bab20f60678b6`

Po rollbacku bylo ověřeno:

- `fuapay.service` active;
- readiness `Healthy`;
- reconciliation worker `Healthy`;
- `/opt/fuapay/current` i běžící executable ukazovaly na `768aec26...`;
- canonical HTTPS HTTP 200.

Databáze zůstala na 19 migracích včetně dvou nových additive migrací. Žádný
schema rollback se neprováděl.

Na tomto předchozím release byl proveden právě jeden nový A/B `payment/init`.
Server log potvrdil:

- start POST `https://iapi.iplatebnibrana.csob.cz/api/v1.9/payment/init` v
  `2026-09-13 13:00:59.164511 UTC`;
- po přibližně 30 s stejný `CsobGatewayException` nad `HttpClient.Timeout`;
- při čekání nepřišla použitelná odpověď od ČSOB.

Tento výsledek významně oslabuje hypotézu, že timeout způsobil PR #44 nebo nový
runtime release.

## Síťové a API diagnostiky

Z VM byly provedeny read-only / non-financial diagnostiky bez vytváření dalších
plateb.

Běžná HTTPS konektivita k `iapi.iplatebnibrana.csob.cz` byla rychlá a používala
IPv4. Explicitní IPv4 request měl řádově desítky milisekund; DNS v daném měření
vracelo IPv4 adresy. Explicitní IPv6 lookup pro host nevrátil použitelnou adresu,
takže incident nebyl klasifikován jako IPv6-path timeout.

Signed POST `/api/v1.9/echo` se stejným merchantem, privátním klíčem a signing
formátem jako aplikace:

- `2026-09-13 13:06:56 UTC` — timeout po `35.002765 s`, HTTP `000`, 0 bytes;
- `2026-09-13 15:11:16 UTC` — timeout po `35.002474 s`, HTTP `000`, 0 bytes.

Úmyslně neplatný POST `{}` na stejný `/api/v1.9/echo` endpoint:

- `2026-09-13 15:15:57 UTC`;
- HTTP `400`;
- connect `0.005843 s`;
- TLS `0.029272 s`;
- total `0.062074 s`.

Signed GET `/api/v1.9/echo` se stejným merchantem a klíčem:

- `2026-09-13 15:17:08 UTC`;
- timeout po `35.002971 s`;
- HTTP `000`, 0 bytes.

Kombinace rychlé HTTP 400 odpovědi na malformed request a timeoutů validně
podepsaných requestů ukazuje, že DNS/TCP/TLS i HTTP endpoint jako takový byly ze
staging VM dosažitelné. Incident se projevoval až při zpracování autentizovaných
merchant requestů. Z toho samotného ale nelze určit konkrétní interní root cause
na straně ČSOB.

## Kontrola provozu FUA Pay

Aplikační log od začátku dne obsahoval právě tři outbound `payment/init` requesty
na ČSOB:

- `12:50:29.279057 UTC`;
- `12:57:35.011938 UTC`;
- `13:00:59.164511 UTC`.

Nebylo nalezeno runaway opakování ani vysoká frekvence requestů. Dvě pozdější
signed echo diagnostiky byly ruční a nefinanční. Evidence tedy nepodporuje
hypotézu, že by FUA Pay sám vyvolal rate limit excesivním provozem.

## Eskalace

Incident byl 2026-09-13 nahlášen přes oficiální ČSOB podporu platební brány a
následně odeslán technický popis e-mailem technickému kontaktu s kopií na
oficiální support kanál ČSOB. Do veřejného repozitáře se záměrně neukládají
osobní support adresy, raw request signatures, privátní klíče, environment
secrets ani jiné autentizační údaje.

ČSOB zatím nepotvrdila konkrétní root cause.

## Aktuální rozhodnutí a recovery postup

Dokud nebude integrační eAPI opět prokazatelně fungovat:

1. nevytvářet další payment-init pokusy jen jako dostupnostní sondu;
2. používat signed `echo` jako první nefinanční availability test;
3. neprovádět schema rollback;
4. ponechat oba release stromy a incident evidence;
5. neprohlašovat PR #44 za live-accepted ani production-ready;
6. expired activation scénář zůstává BLOCKED a není PASS.

Po obnovení služby:

1. signed POST/GET `echo` musí znovu rychle projít s validní podepsanou odpovědí;
2. jeden čerstvý payment-init smoke na známém stabilním runtime ověří návrat
   provider flow bez opakování nejasných existujících attemptů;
3. nasadit tehdy aktuální `main` canonical release procesem;
4. znovu ověřit readiness, worker a funkční payment smoke;
5. teprve potom zopakovat >=30min expired activation scénář a ověřit browser
   evidence `130/6`, nový podepsaný serverový status a interní `Expired` bez
   finančního efektu.

Production traffic zůstává vypnutý.

## Read-only classification of the three durable attention rows — 2026-09-26

The isolated `fuapay_staging` database still contains exactly three
`PaymentReconciliationState.RequiresAttention` rows originating from this
incident. A read-only inspection on 2026-09-26 matched them to the three outbound
`payment/init` timeout attempts documented above:

| orderNo | Payment ID | Recovery created UTC |
| ---: | --- | --- |
| 13 | `8867ae06-2bdb-460c-8769-265dd7c9a9a3` | 2026-09-13 12:51:09.470206 |
| 14 | `a330f5b7-cc93-4136-987a-dbaf7b219246` | 2026-09-13 12:58:09.730108 |
| 15 | `10db109f-97db-4d95-9031-c7e93f6a4b3d` | 2026-09-13 13:01:32.263735 |

Each row is intentionally fail-closed:

- payment status `Created`;
- initiation state `Uncertain`;
- no payment/provider reference and no observed/candidate payId;
- reconciliation state `RequiresAttention`;
- attempt count `0`;
- no browser return and no later provider-status result;
- automatic `payment/init` retry prohibited because no safe payId is known.

The stored reconciliation reason is:

`Inicializace ČSOB je nejasná a nemá bezpečně známé payId; automatický payment/init retry je zakázán.`

These rows are therefore historical audit/recovery evidence of this incident, not
current outstanding payments. They are unrelated to the number or type of Entra
identities present in the imported staging dataset. They remain preserved and are
not deleted merely to obtain a zero attention count.
