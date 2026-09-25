# ČSOB production readiness checklist

Status: 2026-09-24

Tento soubor je jediný aktuální checklist pro postup od dnešního integračního
stavu až k bezpečné aktivaci produkčního ČSOB provozu. Není checklistem prvního
produkčního spuštění FUA Pay bez karetních plateb. Stabilní technický kontrakt je
v [`csob.md`](csob.md); historická staging deployment evidence je v
[`../deployment/demo-staging.md`](../deployment/demo-staging.md). Incident
2026-09-13 je zachycen v
[`csob-incident-2026-09-13.md`](csob-incident-2026-09-13.md) a následná úspěšná
expiry acceptance v
[`../testing/csob-expiry-acceptance-2026-09-14.md`](../testing/csob-expiry-acceptance-2026-09-14.md).

Oficiální ČSOB activation checklist:
https://github.com/csob/paymentgateway/wiki/Activation-of-the-production-environment

## Dva nezávislé milníky

1. FUA Pay může přejít do produkce s `Payments__Provider=None` a
   `Csob__Enabled=false`. Nové karetní platby, ČSOB return processing a
   reconciliation worker jsou vypnuté; tento checklist první go-live neblokuje.
2. Produkční ČSOB traffic se aktivuje později až po dokončení všech relevantních
   bodů tohoto checklistu a přepnutí na `Payments__Provider=Csob` a
   `Csob__Enabled=true` s produkčními credentials.

Pozdější integration acceptance s plným browser/return tokem používá od
2026-09-20 samostatně připravený izolovaný staging runtime na stejném VM.
Staging má vlastní OS účet, release root, PostgreSQL databázi a role, Data
Protection keyring i integration secret root a standardně zůstává
`disabled`/`inactive`. Nikdy nesmí použít produkční DB ani její restore.

Mimo integrační okno zůstává staging fail-closed a standardně zastavený. Během
řízeného acceptance okna 2026-09-22 byl integration provider aktivován pouze ve
stagingu (`Payments__Provider=Csob`, `Csob__Enabled=true`), simulated payments
zůstaly vypnuté a Production zůstala na `Payments__Provider=None`,
`Csob__Enabled=false`. Po dokončení acceptance byl staging znovu uzavřen:
`fuapay-staging.service = inactive/disabled`, aktivní `staging.env` byl bitově
vrácen na původní fail-closed SHA-256
`3cc93de56b0a4329e331fe2ecf2e476cd90c269e1946650654786434fe2c69d4`, UFW
nemá žádné `8443` allow a backend `127.0.0.1:5081` neposlouchá. Integration key
files zůstávají izolované mimo Git/release.

HTTPS/browser edge je už ověřený jako
`https://fuapay.fa.tul.cz:8443` -> Nginx -> `127.0.0.1:5081`.
Nebyla potřeba DNS změna ani nový certifikát; existující HARICA certifikát už
obsahoval SAN `fuapay.fa.tul.cz`. Production `fuapay.tul.cz:443` ani
production alias `fuapay.fa.tul.cz:443` se nezměnily. Mimo testovací okno UFW
nemá žádné `8443` allow; během otevřeného staging okna se `:8443` povoluje pouze
z explicitní klientské IPv4.

Při aktivaci integration provideru musí staging používat přesně
`https://fuapay.fa.tul.cz:8443/payments/csob/return`. Tato return URL je
součástí podepsaného `payment/init`; nesmí se použít production
`https://fuapay.tul.cz/payments/csob/return`. Akceptace explicitního portu `:8443` byla živě potvrzena 2026-09-22: první
kontrolovaný `payment/init` byl přijat, browser byl přesměrován na integrační
platební stránku a následné cancel/success/expiry návraty skutečně prošly přes
`https://fuapay.fa.tul.cz:8443/payments/csob/return`. Aktuální runtime evidence je v
[`../deployment/runtime-state-2026-09-20.md`](../deployment/runtime-state-2026-09-20.md).

## A. Aktuálně prokázaný stav

- [x] Integration Merchant ID `M1EPAY2213` získán.
- [x] Merchant public key registrován u ČSOB; request #7778.
- [x] Merchant private key a integration gateway public key bezpečně nainstalovány
      mimo Git/release.
- [x] GET `echo`: historicky ověřeno HTTP 200, validní podpis, `resultCode=0`.
- [x] POST `echo`: historicky ověřeno HTTP 200, validní podpis, `resultCode=0`.
- [x] Historický pre-production staging používal živý ČSOB integration provider
      se simulovanými platbami vypnutými a provedl níže uvedené live acceptance
      scénáře.
- [x] Nový izolovaný staging má ověřený HTTPS/browser edge
      `https://fuapay.fa.tul.cz:8443`, statické testovací identity a mimo
      acceptance okno zůstává standardně zastavený.
- [x] Integration provider byl 2026-09-22 aktivován pouze ve stagingu s return
      URL `https://fuapay.fa.tul.cz:8443/payments/csob/return`; kontrolovaný
      `payment/init` i skutečné browser návraty potvrdily, že integration gateway
      explicitní port `:8443` akceptuje.
- [x] Fresh GET i POST `echo` 2026-09-22: `resultCode=0`, `resultMessage=OK` a
      podpis obou odpovědí kryptograficky ověřen integration gateway public key.
- [x] Úspěšné dobití kreditu 100 Kč ověřeno 2026-09-07.
- [x] PR #35 nasazen 2026-09-08; return vede na routed payment detail bez 404.
- [x] Druhé úspěšné dobití kreditu 100 Kč ověřeno 2026-09-08.
- [x] Zrušení platby zákazníkem na bráně ověřeno 2026-09-08; lokální stav
      `Cancelled`.
- [x] Reconciliation worker po deployi `Healthy`; public HTTPS smoke PASS.
- [x] Staging revision `768aec26c72bc77ca43d554c8e8bab20f60678b6`
      ověřila celý browser perimeter: CardJob `PLT-2026-000006` přešel jedním
      kliknutím přes `payment/process` až na platební stránku a úspěšný návrat se
      bez F5 během několika sekund promítl do právě jedné `Succeeded` platby.
- [x] Živý `payment/reverse` této platby vrátil podepsané `resultCode=0`,
      `paymentStatus=5`; provider attempt count byl přesně 1.
- [x] Expiry hardening z PR #44 je v `main` jako
      `bc868276aead1b350033303ce8e80bc6ce9a5894`; lokální verification,
      forward-only migrace a staging deployment mechanika prošly.
- [x] Live acceptance expiry hardeningu proběhla 2026-09-14 po obnovení merchant
      eAPI: fresh `payment/init` prošel, browser return po `1806.001 s` nesl
      `resultCode=130`, `paymentStatus=6`, následný podepsaný serverový status
      vrátil `0/6`, interní stav skončil `Expired` a nevznikl žádný kreditní
      pohyb ani jiný finanční efekt.
- [ ] Production traffic není aktivní a nesmí být aktivován před dokončením
      zbytku tohoto checklistu.

Historický staging runtime po deploymentu 2026-09-15 běžel na revision
`9ecee2d9c57d88a2969d42094e49597b41f1642c`. Databáze tehdy měla 21 aplikovaných EF
migrací. Readiness, reconciliation worker, running-executable check i veřejný
HTTP/HTTPS smoke po tomto deploymentu prošly. Incident 2026-09-13 tedy již není
aktivní availability blocker; jeho konkrétní root cause ale zůstává neprokázaný.

## B. Jeden cílený implementační pass před dalšími bankovními testy

Toto je vhodný rozsah pro Codex: nejdřív cíleně porovnat současný ČSOB lifecycle
s oficiální eAPI 1.9/activation dokumentací a s existující FUA Pay architekturou,
poté implementovat jen potvrzené mezery. Neprovádět generický audit celého repa
ani neotvírat uzavřené M0/M1/M2/C-01/C-02 oblasti bez konkrétního defektu.

- [x] Async stav po návratu z brány bez reloadu celé stránky.
      - detail po returnu smí krátce začít jako `Pending`;
      - return pouze urychlí server-side reconciliation a browser není finanční
        autorita;
      - nedůvěryhodný UI marker zapne polling jen pro post-return `Pending`
        detail a sám nespouští provider call ani zápis;
      - klient periodicky načte pouze potřebná lokální data přes owner-scoped
        read-only status handler;
      - aktualizuje status badge, relevantní text/akce a kredit v shellu;
      - polling skončí při terminálním stavu nebo po bounded timeoutu;
      - chyba pollingu nesmí změnit finanční stav a ponechá bezpečný refresh
        fallback.
- [x] Přímý redirect na ČSOB po úspěšném `payment/init` + okamžitém ověření.
      - po zadání částky uživatel nemá zbytečný meziklik;
      - `Details` zůstane recovery cesta pro existující `Pending` platbu a dál
        nabízí `payment/process` odkaz.
- [x] Opravit expired lifecycle: oficiální kombinace ČSOB
      `resultCode=130`, `paymentStatus=6` musí bezpečně skončit jako interní
      `Expired`, nikoli `RequiresAttention` jen kvůli nenulovému resultCode.
      Browser return se nejdřív striktně parsuje a kryptograficky ověří; pouze
      přesná čerstvá kombinace `130/6` se durabilně uloží jako evidence pro stejné
      `payId`. Finální přechod smí provést až pozdější podepsaný serverový
      `payment/status`, který autoritativně potvrdí terminální stav `6`.
- [x] Reconciliation konfigurace nesmí vyčerpat retry pokusy před
      `PaymentTtlSeconds` + provider/worker rezervou; výchozí konfigurace
      podporuje TTL 900 i 1800 sekund bez lokálního odvozování expirace.
- [x] Implementovat POST `echo` se stejnou signing/response-verification/freshness
      hranicí jako GET echo a přidat opt-in integrační test.
- [x] Implementovat skutečné `payment/reverse` pro ČSOB na existujícím
      provider-neutral základě nebo jiným minimálním způsobem, který zachová
      idempotenci, audit a bezpečné řešení nejasného timeoutu. Bez throwaway
      bypassu jen pro aktivaci.
- [x] Doplnit cílené unit testy pro Stage 1 lifecycle, Stage 2 UX endpointy a
      Stage 3 reverse protokol/orchestrace; PostgreSQL testy pokrývají souběh a
      restart z durabilního `InProgress` bez druhého PUT.
- [ ] Samostatný fresh GET/POST echo gate bezprostředně před production activation
      zopakovat. Fresh staging gate 2026-09-22 je PASS, ale pozdější production
      activation musí znovu ověřit merchant availability v daném okamžiku.

Stage 2 používá owner-scoped read-only status handler a bounded polling
post-return stavu (2 sekundy, nejvýše 30 pokusů). Happy-path byl živě ověřen na
revision `768aec26c72bc77ca43d554c8e8bab20f60678b6` bez ručního F5. Expiry
hardening z PR #44 byl po předchozím incidentu samostatně live-accepted
2026-09-14 na `bc868276...`. Nasazený aplikační release Production i izolovaného stagingu je
`774b324c48d8f874db21f115479f3b317c2a73d0`. Staging integration provider byl
2026-09-22 dočasně aktivován pouze pro řízené acceptance okno a po jeho
uzavření byl aktivní `staging.env` vrácen na `Payments__Provider=None` a
`Csob__Enabled=false`.

Stage 3 ukládá `SettlementReturn` i Reverse attempt jako `InProgress` před
externím PUT a nepřenáší databázovou transakci přes HTTP. Po okamžiku, kdy PUT
mohl odejít, je každý replay/restart pouze statusový. Administrace pro recovery
použije původní uložené request ID a po dokončení, zamítnutí nebo při
nekonzistentním stavu nový reverse nenabídne. Přímá odpověď reverse rozlišuje
`0/5`, dokumentované `150` s nereverzibilním stavem a všechny ostatní
fail-closed kombinace; úspěšné statusové recovery `0/8`, `0/9` nebo `0/10`
je samostatná autoritativní hranice. Živý reverse scénář byl na stagingu ověřen
2026-09-12 výsledkem `0/5`.

### Rozhodnutí po production-hardening auditu 2026-09-23

Read-only Codex audit nad clean `main` `9897ef58bcc4caf90d148fe5d4f0bf32864a3a25`
nenašel žádný BLOCKER ani HIGH v payment/settlement/authorization cestě.
Release build a 1076 aplikačních/webových testů prošly; PostgreSQL testy audit
pouze inspektoval, protože pro jejich lokální spuštění nebyl udělen explicitní
DB opt-in.

Jediný potvrzený implementační nález byl MEDIUM fail-closed mezera v
environmentálním svázání `Csob__ReturnUrl`. Narrow hardening 2026-09-23 ji
uzavírá: Production s aktivním ČSOB přijme pouze
`https://fuapay.tul.cz/payments/csob/return`, Staging pouze
`https://fuapay.fa.tul.cz:8443/payments/csob/return` a neznámé prostředí s
aktivním ČSOB startup odmítne. Return boundary navíc explicitně odmítá userinfo,
query a fragment. Pozitivní i negativní testy pokrývají správné URL, vzájemnou
záměnu Production/Staging i chybné hosty, porty a cesty.

Lokální verification stejné změny 2026-09-23 prošla Release buildem,
formátováním, 1092/1092 webovými a aplikačními testy a kontrolou EF modelu bez
pending změny. PostgreSQL integrační testy ani živé ČSOB sandbox testy tento
lokální gate nespouštěl.

Produktové rozhodnutí 2026-09-23:

- bezpečné ČSOB `payment/refund` je součást cílového rozsahu před otevřením
  produkčních karetních plateb; podrobnosti a invarianty jsou v
  [payment-returns.md](../features/payment-returns.md);
- FUA Pay má od začátku podporovat běžné CardJob reverse/refund scénáře a
  bezpečný návrat nevyčerpaného karetně dobitého kreditu na původní kartu;
- nejasný externí výsledek se nikdy nesmí řešit slepým druhým PUT;
- nezavádět websocket/SSE jen kvůli jednomu stavu platby. Pro současný rozsah je
  preferovaný jednoduchý bounded polling malého status endpointu.

## C. Povinné ČSOB integration activation scénáře

Podle oficiální wiki upravené 2026-06-30:

- [x] GET echo: fresh live PASS 2026-09-22, HTTP 200, `resultCode=0`,
      `resultMessage=OK`, validní gateway podpis.
- [x] POST echo: fresh live PASS 2026-09-22, HTTP 200, `resultCode=0`,
      `resultMessage=OK`, validní gateway podpis.
- [x] Successful authorised payment: 2026-09-22 na izolovaném stagingu 10 Kč,
      testovací karta `4000007000010006`, CVC `100`; lokálně právě jedna
      `Succeeded` platba, právě jeden kreditní pohyb +10 Kč a právě jeden
      `CardWalletTopUp` finanční dokument.
- [x] Payment cancelled by customer: fresh 2026-09-22 přes skutečný
      `:8443` browser return; gateway `resultCode=0`, `paymentStatus=3`, lokálně
      `Cancelled`, reconciliation `Completed`, bez finančního efektu.
- [x] Expired payment: fresh scénář 2026-09-22 na platbě
      `8b48a88a-7c30-4c9c-9df0-8f0914e12ab9`; browser return nesl ověřenou
      evidenci `resultCode=130`, `paymentStatus=6`, následný serverový status
      potvrdil `0/6`, interní stav `Expired`, reconciliation `Completed`, bez
      kreditního pohybu a bez finančního dokumentu. Jde o náhradu neúplného historického
      pokusu z 2026-09-12, který neuměl zachytit celý podepsaný browser payload.
- [x] Payment reversal: po úspěšné autorizaci zavolat `payment/reverse`, ověřit
      HTTP 200, podpis, `resultCode=0`, `paymentStatus=5` a odpovídající lokální
      stav/return evidence; ověřeno 2026-09-12 nad CardJob `PLT-2026-000006`.
- [ ] Po finálním fresh acceptance runu předat internímu centrálnímu správci TUL
      jasné GO se souhrnem testů. POS Merchant ani bankovní výpisy nejsou
      provozní odpovědností FUA Pay; centrální správce provede potřebný bankovní
      krok za univerzitu.
- [ ] Počkat na potvrzení ČSOB, že production environment je aktivovaný.

## D. FUA Pay vlastní integrační acceptance před aktivací produkčního ČSOB

Tyto scénáře nejsou náhradou bankovního checklistu; ověřují naši aplikaci jako
celek.

- [x] Přímá platba zakázky kartou: CardJob `PLT-2026-000006` -> `Succeeded`,
      zakázka uhrazena přesně jednou; ověřeno 2026-09-12.
- [x] Declined authorization attempt: 2026-09-22 karta
      `4000007000010006` s CVC `200` zobrazila na ČSOB srozumitelné zamítnutí
      vydavatelem, ale autoritativní `payment/status` zůstal `0/2`; FUA Pay
      správně ponechal platbu `58b18f4e-ed26-4972-aca2-9f2e15880c33` jako
      `Pending`, bez kreditního pohybu a bez dokladu. ČSOB session dál nabízela
      jinou kartu. Následné explicitní `Zrušit platbu a vrátit se do obchodu`
      skončilo `0/3`, lokálně `Cancelled`, reconciliation `Completed`, stále bez
      finančního efektu.
- [x] Terminální `Failed` (`resultCode=0`, `paymentStatus=6`) je uzavřený jako
      NO-DEFECT / automated-covered evidence bod, nikoli jako fresh live browser
      PASS. Standardní decline testovací kartou session neukončuje stavem 6 a
      není znám dokumentovaný deterministický Basic Payment simulator recipe.
      `CsobPaymentReconciliationServiceTests` přímo ověřují `0/6` bez verified
      expiry evidence -> interní `Failed`, provenance
      `CsobResult0Status6`, audit a žádný settlement. Fresh live simulátorový
      důkaz se proto nepovažuje za production GO blocker; pokud ČSOB poskytne
      deterministický recipe, lze jej doplnit jako dodatečnou live evidenci.
- [x] Duplicate browser return: 2026-09-22 byl čerstvý autentický podepsaný
      `application/x-www-form-urlencoded` POST z ČSOB pro platbu
      `d707aa08-681f-4557-8298-f46e5ee6cbd9` / `106199d5aa1e@LI` zopakován beze
      změny payloadu v rámci freshness okna. Původní browser POST v `14:21:21Z`
      i duplicate replay v `14:22:07Z` vrátily HTTP 303 na tentýž payment detail;
      platba i reconciliation už před prvním returnem byly terminální a replay
      nezměnil jejich timestamps/version ani nevytvořil druhý settlement efekt.
      Po replayi existoval právě jeden kreditní pohyb +10 Kč a právě jeden
      finanční dokument.
- [x] Lost browser return: 2026-09-22 byl inbound `8443` pro testovací klientskou
      cestu záměrně uzavřen a browser return skončil `ERR_CONNECTION_TIMED_OUT`.
      Platba `559d5f36-9e20-4a6e-8f03-b09baeaba069` / `d69b8ceb13ce@LI` přesto
      workerem přešla z autoritativního `payment/status` `0/7` do `Succeeded`;
      `last_browser_return_at` zůstal `NULL`, vznikl právě jeden kreditní pohyb
      +10 Kč a právě jeden dokument `FUA-2026-000006`.
- [x] Restart během otevřené/pending platby: 2026-09-22 staging proces skutečně
      změnil PID `328579 -> 331018`; stejná platba
      `7f6a771a-ce4b-4337-825e-685f7bbbdfdc` / `41d4289c6538@LI` přežila restart
      jako `Pending`, worker znovu naběhl `Healthy` a následné dokončení stejné
      platby vytvořilo právě jeden kreditní pohyb +10 Kč a právě jeden dokument
      `FUA-2026-000005`.
- [ ] Opakovaný provider `payment/status` nad již úspěšnou platbou: live scénář
      nebyl uměle vyvolán. Nasazená reconciliation cesta přijímá i již
      `Succeeded` platbu a settlement je navržen idempotentně; konkrétně
      `PaymentSettlementService.CompleteAsync()` nad již `Succeeded` platbou
      vrací `false` před novým finančním efektem. PostgreSQL persistence testy
      opakují settlement dvakrát a samostatné concurrent ČSOB testy ověřují, že
      vznikne právě jeden pohyb/dokument/job settlement. Přesto aplikace nemá
      veřejný/operator endpoint, který by nad terminální platbou bezpečně vynutil
      nový skutečný provider status call, takže provider replay není fresh live
      PASS. UI odkaz `Obnovit stav ručně` pouze znovu načte lokální detail a
      provider `payment/status` nevolá; kvůli jedné acceptance fajfce se
      neprováděl ruční DB zápis ani pomocný bypass.
      Minor UX rest: tento odkaz je aktuálně viditelný i na terminální
      `Cancelled` platbě, kde je funkčně neškodný, ale zbytečný/matoucí.
- [x] Opuštěná `Pending` platba bez vstupu karty: fresh 2026-09-22 platba
      `8b48a88a-7c30-4c9c-9df0-8f0914e12ab9` po TTL skončila `Expired`; ověřená
      browser evidence `130/6` + následný serverový `0/6`, reconciliation
      `Completed`, bez finančního efektu.
- [x] Return UX: fresh 2026-09-22 happy, cancel i expired tok skončil na routed
      payment detailu bez 404 a bez ručního F5; explicitní `:8443` return funguje.
- [x] Mobile + desktop smoke hlavního payment flow: desktop happy path byl
      živě ověřen 2026-09-22; následně skutečný telefonní browser přes povolenou
      klientskou IPv4 `147.230.72.120` dokončil nový 10 Kč top-up
      `ef92e52d-dffc-465b-a2c8-c97dadacb8f4` / `398b8c18f4ce@LI` jako
      `Succeeded`. Reconciliation skončila `Completed` s `paymentStatus=7`,
      `resultCode=0`; vznikl právě jeden kreditní pohyb +10 Kč a právě jeden
      finanční dokument, kredit Testovacího zákazníka Beta přešel 230 -> 240 Kč.
      Mobilní UI bylo při průchodu použitelné bez odlišné funkční chyby proti
      desktopu.
- [x] Multi-user CardJob playtest po prvním closeoutu: staging byl znovu otevřen
      pouze pro stejnou povolenou klientskou IPv4 a dva lidé přes oddělené
      testovací identity provedli dvě přímé karetní platby zakázek z různých
      pracovišť. `3D-2026-000004` (`3D tisk - Pardubice`, 120 Kč) skončila přes
      payment `5b3faa6e-292b-47ee-91b5-d7417eb192aa` / `095401bf4bf9@LI` jako
      `Succeeded`, reconciliation `Completed`, zakázka `Paid` a dokument
      `FUA-2026-000010`. `PLT-2026-000002` (`Tisk závěrečné prezentace`, 520 Kč)
      skončila přes payment `0f00a7b4-e746-4800-be30-415c7666b2ff` /
      `0d973b5d9984@LI` stejně jako `Succeeded` / `Completed`, zakázka `Paid` a
      dokument `FUA-2026-000011`. U každé zakázky existovala právě jedna payment,
      právě jedna `Succeeded`, žádná `Pending`; `settlement_reference_id`,
      document `source_id`, částka i provider reference se přesně shodovaly.
      Nginx zaznamenal oba browser returny jako HTTP 303 a staging journal od
      začátku playtestu neměl warning/error.
- [ ] Live access-isolation browser probe napříč zákazníky a requester
      pracovišti nebyl v tomto okně dokončen. Kód používá owner-scoped
      `FindForCustomerAsync` a management scope přes `FindForManagementAsync`,
      ale tento konkrétní ruční URL negativní test zůstává vhodný jako další
      hardening evidence.
- [ ] Live souběžné/double-click vytvoření dvou CardJob payment pokusů pro jednu
      nezaplacenou zakázku nebylo v tomto okně provedeno. Databázový model má
      unikátní blocking-job hranici a oblast je vhodná pro cílený Codex review a
      případný pozdější staging concurrency probe; není tím označen bankovní
      activation blocker.

Fresh acceptance okno 2026-09-22 bylo po testech bezpečně uzavřeno a staging
byl následně ještě jednou krátce otevřen pro multi-user CardJob playtest. Také
před druhým/final stopem bylo v `fuapay_staging` ověřeno `Pending=0` a due
reconciliation `=0`. Finální stav dne je znovu `inactive/disabled`, aktivní env
je bitově zpět na fail-closed baseline SHA-256
`3cc93de56b0a4329e331fe2ecf2e476cd90c269e1946650654786434fe2c69d4`, UFW nemá
žádné `8443`, `127.0.0.1:5081` neposlouchá a dočasný playtest backup byl po
ověření odstraněn. Production zůstala `Healthy` se stejným Nginx site SHA-256
`491a7228c460ce5c8674a98e6c4c0d0433e0b4fd990360f29cf2d9c9280a91d9` a alias
na 443 dál vrací HTTP 301 na `https://fuapay.tul.cz/`.

## E. Finální GO plán 2026-09-23

Tento plán je kanonický sled kroků před oznámením GO centrálnímu správci TUL.
Cílem je, aby bankovní submission nebyl postaven na několik dní starých testech.

1. **Dokončeno 2026-09-23:** environment-bound ČSOB return URL hardening,
   negativní cross-environment a URI-boundary testy a zpřesnění dokumentace;
   key/fingerprint preflight a fresh signed echo zůstávají samostatnou
   deployment/activation hranicí.
2. Dokončit veřejné anonymní stránky `/Privacy` a `/Terms` bez nové zbytečné
   právní stránky:
   - `poverenec@tul.cz` ponechat jako DPO/GDPR kontakt TUL;
   - provozní/platební kontakt FUA Pay má být `fuapay@tul.cz`, ale před
     zveřejněním se musí potvrdit, že alias/mailbox skutečně existuje a je
     monitorovaný;
   - Terms musí výslovně pokrýt provozovatele, CZK, charakter fakultních služeb,
     reklamace/vratky, způsob poskytnutí služby, bezpečné karetní zpracování a
     fakt, že FUA Pay neukládá číslo karty ani CVC;
   - doplnit pouze oficiální schválená loga sjednaných platebních služeb/karetních
     schémat.
3. Dokončit production-grade returns scope z
   [payment-returns.md](../features/payment-returns.md), zejména refund a
   CardTopUp návrat nevyčerpaného kreditu.
4. Doplnit samostatný účetní/reconciliation export pro párování s centrálním
   ČSOB výpisem. Minimální párovací pole: `orderNo`, `payId`, FUA payment ID,
   datum, částka, měna, účel, job number/service unit, finanční dokument a
   reverse/refund stav/částka/čas. Osobní údaje pouze pokud je účetní proces
   skutečně potřebuje.
5. Entra logout neobcházet cookie-only hackem. Nejprve znovu urgovat tenant
   správce, aby app registration správně obsahovala samostatný
   `/signout-callback-oidc`; známý stav je popsán v
   [entra-id.md](entra-id.md).
6. Automatické hardening testy identifikované auditem:
   - [x] Concurrent CardJob creation: PR #76 je mergnutý v
     `8e45f248f295d1d9af665e3214dfce2ab2eef399`. PostgreSQL test
     `ConcurrentDirectPayments_CreateSinglePayment` ověřuje stejný `Payment.Id`
     pro dva souběžné požadavky a právě jeden payment i initiation řádek;
     [evidence a výsledky](../testing/card-job-concurrent-creation-verification-2026-09-23.md).
     Nejde o živý ČSOB double-click test ani počítání provider HTTP init callů;
     příslušný live scénář v kroku 7 zůstává otevřený.
   - [ ] Celá `Succeeded + nový status 7/8` reconciliation cesta.
   - [ ] HTTP-level object-isolation probe.
7. Z jednoho clean commitu/release artefaktu udělat izolovaný staging deploy a
   v jediném souvislém testovacím okně zopakovat:
   - fresh GET echo a POST echo;
   - success, cancel a expiry;
   - fresh reverse `0/5` na aktuálním release;
   - refund scénáře odpovídající implementovanému scope;
   - repeated provider `payment/status` nad již `Succeeded`;
   - cross-customer/cross-requester URL isolation;
   - skutečný double-click/concurrent CardJob creation;
   - účetní export a exactly-once read-only DB evidence.
8. Před closeoutem musí být `Pending=0`, due reconciliation `=0`; staging se
   vrátí fail-closed a Production guard musí být beze změny.
9. Dokumentační evidence se doplní bez přepisování historie a bez tvrzení, že
   neprovedený scénář je PASS.
10. GO mail centrálnímu správci TUL odeslat tentýž den, ideálně bezprostředně po
    closeoutu (řádově hodiny, ne dny). Mezi finálním runem a GO nesmí nastat
    změna release artefaktu ani payment konfigurace; pokud nastane, relevantní
    acceptance se opakuje.
11. Po bankovním potvrzení nainstalovat production merchant konfiguraci/klíče,
    provést fresh production preflight a jeden řízený malý reálný payment před
    otevřením karetního toku běžným uživatelům.

Fresh live `payment/status` replay nad již `Succeeded` zůstává do kroku 7
otevřenou evidence položkou. Terminální `0/6 Failed` je naopak uzavřený
automated-covered non-blocking bod, viz sekce D.

### Repository checkpoint 2026-09-24

PR #77 přidal pouze runtime regresní test stránkování administrace plateb a
[evidenci CodeQL nálezů #2 a #3](../testing/codeql-xss-pagination-verification-2026-09-24.md).
Merge commit je `4497133577b004b0f64f84116f9cc2706cd578cd`; post-merge
[CI #402](https://github.com/rouhajan/fua-pay/actions/runs/35996673068) a
[CodeQL #407](https://github.com/rouhajan/fua-pay/actions/runs/35996673119)
skončily `SUCCESS` na tomto přesném commitu. Detail closeoutu, včetně původu
potvrzení dismissalů a úklidu větve, je v odkazované evidenci.

Tento checkpoint nedokládá nový deployment ani nový live ČSOB test. Nemění
pořadí ani zbývající rozsah kroků 2–11 a není produkční GO. Issue #37 zůstává
otevřené; refund, CardTopUp návrat kreditu, účetní export a zbývající acceptance
se tímto dokumentačním krokem neoznačují jako hotové.

## F. Historický přechodný staging release/deploy gate

Následující checklist zachovává pravidla a evidence historického
pre-production demo/staging runtime. Od 2026-09-20 už není návrhem budoucího
runtime modelu: další integrační okna používají výše popsaný trvale připravený,
ale standardně zastavený izolovaný staging.

- [ ] clean `main` / přesný merge commit;
- [ ] canonical `scripts/verify.ps1` PASS;
- [ ] PostgreSQL integration gate PASS, pokud je relevantní DB kód;
- [ ] EF pending-model check; migrace jen pokud model skutečně změněn;
- [ ] self-contained `linux-x64` artifact přes
      `FuaPay.DeploymentArtifacts`;
- [ ] lokální i serverový SHA-256/size/archive verification;
- [ ] instalace vedle aktivního release, ownership/modes/executable PASS;
- [ ] atomický `/opt/fuapay/current` switch;
- [ ] `/health/ready` s bounded retry;
- [ ] ČSOB worker health s warm-up: `NotStarted` je přechodný stav, čekat;
      `Healthy` PASS; `Failed`/`Stale` nebo timeout FAIL;
- [ ] running executable musí odpovídat novému release;
- [ ] canonical HTTPS 200 + redirect smoke;
- [ ] funkční payment smoke podle scope patchu;
- [ ] starý release ponechat jako immediate rollback do dokončení acceptance.

### Historický výsledek gate 2026-09-13 pro `bc868276...`

Artifact, backup, dvě forward-only migrace, instalace, atomická aktivace,
readiness, worker health, running-executable check a HTTPS smoke prošly. Funkční
payment smoke tehdy neprošel kvůli opakovanému 30sekundovému `payment/init`
timeoutu. Stejný timeout po rollbacku na `768aec26...` a současné signed echo
timeouty neprokázaly kódovou regresi a vedly ke code-only rollbacku.

Tento blocker byl následně uzavřen čerstvou expiry acceptance 2026-09-14 po
obnovení merchant eAPI. Historický staging runtime od 2026-09-15 běžel na revision
`9ecee2d9c57d88a2969d42094e49597b41f1642c` se schématem 21 migrací a zdravým
post-activation gate. Historická diagnostika z 2026-09-13 zůstává zachována jako
evidence, nikoli jako aktuální runtime stav.

## G. Pozdější aktivace produkčního ČSOB až po bankovním schválení

- [ ] Potvrdit, že produkční FUA Pay běží nad čistou produkční databází a že
      aktivace ČSOB nevyžaduje schema redesign ani přenos demo/seed dat.
- [ ] Doplnit do production secret/config store ČSOB production
      signing/verification material podle aktivace ČSOB; existující DB, Data
      Protection a Entra hranice zůstávají oddělené.
- [ ] Přepnout API pouze na `https://api.platebnibrana.csob.cz/`.
- [ ] Ověřit production merchant private key a production gateway public key;
      žádné fixed IP assumptions.
- [ ] Vypnout development/staging paths, seed/simulated payment behavior a další
      preview režimy, které do produkce nepatří.
- [ ] Backup/restore, RPO/RTO, log retention, alerting na readiness/reconciliation,
      incident contact a key/secret rotation musí být explicitně připravené.
- [ ] Final production acceptance: live/ready, Entra login/logout/roles, jeden
      kontrolovaný reálný ČSOB payment path a následná kontrola finančního efektu.
- [ ] Teprve potom otevřít produkční platební tok běžným uživatelům.
