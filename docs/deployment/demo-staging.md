# Demo / staging deployment

Status: 2026-09-13

Tento soubor popisuje pouze aktuální staging runtime a poslední deployment
evidence. Kanonické vytváření/installace release artefaktu je v
[`release-artifacts.md`](release-artifacts.md). Aktuální ČSOB postup až do
production readiness je v
[`../integrations/csob-production-readiness.md`](../integrations/csob-production-readiness.md).
Detailní evidence aktuálního integračního incidentu je v
[`../integrations/csob-incident-2026-09-13.md`](../integrations/csob-incident-2026-09-13.md).

## Aktuální runtime

- URL: `https://fuapay.tul.cz`
- Alternate URL: `https://fuapay.fa.tul.cz` -> canonical URL.
- Git `main`: `bc868276aead1b350033303ce8e80bc6ce9a5894`.
- Revision ověřená při poslední úspěšné live payment acceptance:
  `768aec26c72bc77ca43d554c8e8bab20f60678b6`.
- Aktivní release po řízeném code-only rollbacku 2026-09-13:
  `/opt/fuapay/releases/768aec26c72bc77ca43d554c8e8bab20f60678b6`.
- Release `bc868276aead1b350033303ce8e80bc6ce9a5894` zůstává nainstalovaný
  vedle aktivního release; artifact/schema/deployment gate prošel, ale funkční
  payment acceptance je blokována aktuálním ČSOB integration incidentem.
- Service account: `fuapay:fuapay`.
- Kestrel: `127.0.0.1:5080` behind Nginx.
- Configuration: `/etc/fuapay/staging.env`.
- Database: `fuapay_demo`.
- Databáze má po deployi PR #44 19 aplikovaných EF migrací; code-only rollback
  schema nevracel.
- Nejnovější migrace jsou
  `20260913111715_AddCsobExpiryFailureProvenance` a
  `20260912145956_AddCsobVerifiedExpiryReturnEvidence`.
- `Database__ApplyMigrationsOnStart=false`.
- `Csob__PaymentTtlSeconds=1800`.
- Microsoft Entra login: live and in use.
- Payment provider: ČSOB integration, Merchant ID `M1EPAY2213`.
- Simulated payments: disabled.
- ČSOB reconciliation worker: enabled and po rollbacku `Healthy`.
- Staging seed data: enabled.
- Receipt preview mode: enabled.
- Nginx Basic Authentication: intentionally absent.
- Production ČSOB traffic and production database workload: not active.

## 2026-09-13 PR #44 deployment a ČSOB integration incident

PR #44 byl mergnut do `main` jako:

`bc868276aead1b350033303ce8e80bc6ce9a5894`

Lokální canonical verification prošla, release artifact byl vytvořen a ověřen,
před migrací vznikl validovaný PostgreSQL backup a dvě nové forward-only migrace
byly úspěšně aplikovány. Nový release byl nainstalován vedle aktivního release a
po atomické aktivaci prošly readiness, reconciliation-worker health, kontrola
běžícího executable i canonical/alternate HTTPS smoke.

Funkční payment smoke ale dvakrát skončil přibližně po 30 sekundách timeoutem
ČSOB `POST /api/v1.9/payment/init`. Staging byl proto řízeně vrácen pouze kódem
na předchozí live-accepted release `768aec26...`; databázové schema se nevracelo.
Po rollbacku prošly readiness, worker health, running-release check a canonical
HTTPS 200.

Jeden čerstvý A/B `payment/init` na předchozím release skončil stejným
30sekundovým timeoutem. Následné signed POST i signed GET `/api/v1.9/echo` se
stejným merchantem a signing materiálem také opakovaně timeoutovaly bez jediného
bajtu odpovědi. Naproti tomu úmyslně malformed POST `{}` na stejný `/echo`
endpoint dostal HTTP 400 přibližně za 62 ms, takže DNS/TCP/TLS i základní HTTP
endpoint byly ze staging VM dosažitelné.

Aktuální pracovní závěr je externí nebo merchant-specific integrační blocker;
konkrétní root cause zatím ČSOB nepotvrdila. Další payment-init pokusy se
nepoužívají jako availability test. Expired activation scénář zůstává BLOCKED a
není PASS. Kompletní časy, artefakty, rollback a diagnostická evidence jsou v
[`../integrations/csob-incident-2026-09-13.md`](../integrations/csob-incident-2026-09-13.md).

## 2026-09-12 live ČSOB acceptance

Na staging revision `768aec26c72bc77ca43d554c8e8bab20f60678b6` prošel čerstvý
single-click CardJob `PLT-2026-000006` z FUA Pay přes integrační
`payment/process` origin a následný payment-page origin. Nebyl pozorován CSP
`form-action` blok. Po úspěšném browser returnu bounded polling bez ručního F5
během několika sekund zobrazil právě jednu `Succeeded` platbu a právě jeden
odpovídající payment/job efekt.

Následný živý `payment/reverse` stejné platby prošel s ověřeným
`resultCode=0`, `paymentStatus=5` a provider attempt count 1.

Pro expired CardJob `PLT-2026-000007` (job
`22803461-65ba-46e9-adfa-4578bf71982f`, payment
`b8e12230-d3ad-4d93-9903-b31e334e1179`, payId `c4f36a6e5988@LI`) bylo mimo
release v `/etc/fuapay/staging.env` nastaveno
`Csob__PaymentTtlSeconds=1800`; hodnota byla potvrzena v prostředí běžícího
procesu. Platba vznikla `2026-09-12 13:56:07.895386 UTC` a browser auto-return
byl pozorován `2026-09-12 14:26:15.665062 UTC`, tedy po přesně
`1807.769676` sekundy. Tehdy nasazený endpoint z historického returnu ukládal
pouze `payId`; browserový `resultCode`, `paymentStatus`, podpis ani celý podepsaný
payload proto nebyly zachyceny a pro tento konkrétní běh nelze tvrdit, že browser
vrátil `130/6`. Pozdější autoritativní podepsaný serverový `payment/status`
persistoval `resultCode=0`, `paymentStatus=6`. Nasazený runtime uzavřel platbu
jako `Failed`, zakázka zůstala neuhrazená a nevznikl settlement efekt. To není
activation PASS. Oprava expiry lifecycle je v aktuálním `main`, ale její live
acceptance je po incidentu 2026-09-13 stále blokována a musí se zopakovat až po
obnovení ČSOB integration eAPI.

## 2026-09-12 release evidence

- Artifact: `fuapay-staging-768aec26c72bc77ca43d554c8e8bab20f60678b6-linux-x64.tar.gz`.
- SHA-256: `65685702CBE08F61ABC1E9ECDA97C50011FC402798C81C610E3966BD33DA2024`.
- Velikost: `122976095` bytes.
- Archive verification: 12 adresářů mode `0770`, 403 běžných souborů mode
  `0660`, `FuaPay.Web` mode `0750`.
- Prošly serverová kontrola hashe a velikosti, `gzip`/`tar` kontrola, ownership a
  modes instalace, aktivace, kontrola přesného běžícího executable, readiness,
  health reconciliation workeru a veřejný HTTPS smoke.

## 2026-09-08 PR #35 deployment

PR #35 (`fix: complete ČSOB top-up return flow`) was merged as:

`a638cad732b210a2f949f12c05ecdf84a2bce7a8`

Scope relevant to staging:

- customer top-up is available with active ČSOB provider;
- CreateTopUp highlights `Kredit` while payment index/detail remain `Platby`;
- ČSOB browser return redirects to routed
  `/Customer/Payments/Details/{id}?view=customer` instead of the invalid query
  form that previously produced 404;
- no EF model/schema change and no migration.

### Release gate

Before packaging:

- canonical `scripts/verify.ps1`: PASS;
- Release build: PASS, 0 warnings/errors;
- formatting: PASS;
- `FuaPay.Web.Tests`: 780/780 PASS;
- EF pending-model check: no model changes since the last migration;
- PostgreSQL integration gate: 224/224 PASS on isolated `fuapay_test_*` DB;
- live ČSOB GET echo: PASS.

Release archive:

`fuapay-staging-a638cad732b210a2f949f12c05ecdf84a2bce7a8-linux-x64.tar.gz`

SHA-256:

`379ac60d7ca48f266d5863d28aa398144e6f60fb6825e1dcc349ea24cb8b53f2`

Size:

`122840907` bytes

Canonical archive verification:

- directories: 12, mode `0770`;
- ordinary files: 400, mode `0660`;
- `FuaPay.Web`: mode `0750`.

After transfer the server independently matched SHA-256 and byte size;
`gzip -t` and full tar listing passed before installation.

### Installation and activation

The release was installed beside the active release at:

`/opt/fuapay/releases/a638cad732b2`

Pre-activation checks passed:

- all release content owned by `fuapay:fuapay`;
- all directories mode `0770`;
- all ordinary files except host executable mode `0660`;
- `FuaPay.Web` mode `0750` and executable by the service account;
- `appsettings.Development.json` absent.

The first activation was rolled back unnecessarily by an incorrect deployment
check that treated the ČSOB worker's transient post-start HTTP 503/`NotStarted`
state as immediate failure. The application readiness itself was already
`Healthy`. No application or database migration rollback was involved.

The corrected activation used bounded worker warm-up. Final result:

- `/opt/fuapay/current` -> `/opt/fuapay/releases/a638cad732b2`;
- `fuapay.service`: active;
- running executable:
  `/opt/fuapay/releases/a638cad732b2/FuaPay.Web`;
- `/health/ready`: `Healthy`;
- `/health/workers/csob-reconciliation`: `Healthy`, no failed cycle reported;
- `https://fuapay.tul.cz/`: HTTP 200;
- `http://fuapay.tul.cz/`: HTTP 301;
- `https://fuapay.fa.tul.cz/`: HTTP 301.

### Canonical staging post-activation health rule

Direct Kestrel requests must include both:

```text
Host: fuapay.tul.cz
X-Forwarded-Proto: https
```

Startup health is bounded, not instantaneous:

1. retry `/health/ready` until `Healthy` or timeout;
2. then poll `/health/workers/csob-reconciliation`;
3. worker `NotStarted` immediately after restart is a warm-up state, not a
   rollback reason;
4. worker `Healthy` is PASS;
5. worker `Failed`, `Stale` or bounded timeout is FAIL and may trigger rollback;
6. verify the running executable resolves to the new release;
7. finish with canonical/alternate HTTPS smoke.

Do not infer a broken `/opt/fuapay/current` target from an unprivileged
`readlink -f` when the deployment user cannot traverse the release directory;
use an appropriately privileged read-only check.

## 2026-09-08 live ČSOB functional acceptance

Two customer scenarios were exercised against the real ČSOB integration
environment after deployment.

Successful top-up:

- amount: 100 Kč;
- provider reference: `fcd3c38f6325@LI`;
- browser returned directly to the routed payment detail without 404;
- reconciliation settled the payment to `Succeeded` / „Uhrazená“;
- exactly one additional 100 Kč credit effect was visible; after the two
  successful 100 Kč integration top-ups the test account displayed 200 Kč.

Customer-cancelled top-up:

- provider reference: `47ac34a8568f@LI`;
- the attempt ended locally as `Cancelled` / „Zrušená“;
- no credit was added.

Historický úspěšný návrat odhalil UX mezeru: detail se načetl dříve, než doběhl
asynchronní reconciliation worker, takže nový stav a kredit byly vidět až po
ručním F5. Finanční model zůstal správně serverový. Tento problém později vyřešil
bounded polling lokálního status endpointu a live acceptance 2026-09-12 jej
ověřila bez F5.

Historický payment UX také vkládal interní detail mezi úspěšný `payment/init` a
`payment/process`. Přímý redirect s detailem jako recovery cestou byl následně
implementován a live acceptance 2026-09-12 jej ověřila.

These UX/lifecycle items and the remaining production-readiness gaps are tracked
only in `docs/integrations/csob-production-readiness.md` to avoid duplicated
stale TODO lists.

## Previous rollback baseline

Before the 2026-09-08 deployment staging ran:

- revision `39293d85445bac0654b35bb2984617e273122481`;
- release `/opt/fuapay/releases/39293d85445b`;
- artifact SHA-256
  `2a3ad32ae7291ea58e51406fd267543b514eeda9ddf95cdb65b6b312032ba46d`;
- artifact size `122839371` bytes.

Tento záznam je pouze historický baseline. Aktuální rollback target musí být při
dalším deployi znovu ověřen na serveru; nesmí se odvozovat z této staré sekce.
