# ČSOB production readiness checklist

Status: 2026-09-19

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

Pokud pozdější integration acceptance vyžaduje plný browser/return tok, použije
se pouze časově omezený non-production FUA Pay runtime na serveru s čerstvou
izolovanou dočasnou PostgreSQL databází a rolí, integration ČSOB credentials/API
a odděleným endpointem, portem nebo proxy route podle ověřených provozních
možností. Nikdy nesmí použít produkční DB ani její restore. Po testovacím okně se
runtime zastaví a odstraní; permanentní druhý staging FUA Pay se nezakládá.
Přesná veřejná return URL zůstává provozním/bankovním omezením do jejího
ověření; žádný konkrétní hostname, `PathBase` ani systemd model zde není
prohlášen za schválený.

## A. Aktuálně prokázaný stav

- [x] Integration Merchant ID `M1EPAY2213` získán.
- [x] Merchant public key registrován u ČSOB; request #7778.
- [x] Merchant private key a integration gateway public key bezpečně nainstalovány
      mimo Git/release.
- [x] GET `echo`: historicky ověřeno HTTP 200, validní podpis, `resultCode=0`.
- [x] POST `echo`: historicky ověřeno HTTP 200, validní podpis, `resultCode=0`.
- [x] Staging používá živý ČSOB integration provider; simulované platby jsou
      vypnuté.
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
      zopakovat; historické i implementační testy jsou PASS, ale production
      activation musí ověřit aktuální merchant availability v daném okamžiku.

Stage 2 používá owner-scoped read-only status handler a bounded polling
post-return stavu (2 sekundy, nejvýše 30 pokusů). Happy-path byl živě ověřen na
revision `768aec26c72bc77ca43d554c8e8bab20f60678b6` bez ručního F5. Expiry
hardening z PR #44 byl po předchozím incidentu samostatně live-accepted
2026-09-14 na `bc868276...`; současný `main` `9ecee2d...` tyto změny obsahuje a
je od 2026-09-15 zdravě nasazený na stagingu.

Stage 3 ukládá `SettlementReturn` i Reverse attempt jako `InProgress` před
externím PUT a nepřenáší databázovou transakci přes HTTP. Po okamžiku, kdy PUT
mohl odejít, je každý replay/restart pouze statusový. Administrace pro recovery
použije původní uložené request ID a po dokončení, zamítnutí nebo při
nekonzistentním stavu nový reverse nenabídne. Přímá odpověď reverse rozlišuje
`0/5`, dokumentované `150` s nereverzibilním stavem a všechny ostatní
fail-closed kombinace; úspěšné statusové recovery `0/8`, `0/9` nebo `0/10`
je samostatná autoritativní hranice. Živý reverse scénář byl na stagingu ověřen
2026-09-12 výsledkem `0/5`.

### Rozhodnutí, která nejsou automaticky součástí tohoto passu

- Refund není povinný ČSOB activation scénář. In-app card refund se implementuje
  jen pokud bude schválen pro pozdější produkční karetní provoz; jinak se
  výslovně zdokumentuje operátorský postup.
- Nezavádět websocket/SSE jen kvůli jednomu stavu platby. Pro současný rozsah je
  preferovaný jednoduchý bounded polling malého status endpointu.

## C. Povinné ČSOB integration activation scénáře

Podle oficiální wiki upravené 2026-06-30:

- [x] GET echo: HTTP 200, validní podpis, `resultCode=0`; historicky live PASS.
- [x] POST echo: HTTP 200, validní podpis, `resultCode=0`; živě ověřeno na
      staging release `b057ecf84f33908bb5c6a20d5389c025a4e712ca`.
- [x] Successful authorised payment: integrační testovací karta
      `4000007000010006`, budoucí expirace, CVC `100`; návrat na požadovanou
      stránku ověřen.
- [x] Payment cancelled by customer: `resultCode=0`, `paymentStatus=3` a
      odpovídající lokální `Cancelled` ověřen uživatelským testem 2026-09-08.
- [x] Expired payment: fresh scénář 2026-09-14 prošel po `1806.001 s`; browser
      return nesl `resultCode=130`, `paymentStatus=6`, následný podepsaný
      serverový status vrátil `0/6`, interní stav byl `Expired` a nevznikl žádný
      kreditní pohyb ani settlement efekt. Jde o náhradu neúplného historického
      pokusu z 2026-09-12, který neuměl zachytit celý podepsaný browser payload.
- [x] Payment reversal: po úspěšné autorizaci zavolat `payment/reverse`, ověřit
      HTTP 200, podpis, `resultCode=0`, `paymentStatus=5` a odpovídající lokální
      stav/return evidence; ověřeno 2026-09-12 nad CardJob `PLT-2026-000006`.
- [ ] V POS Merchant potvrdit provedení všech povinných scénářů a odeslat je ke
      kontrole ČSOB.
- [ ] Počkat na potvrzení ČSOB, že production environment je aktivovaný.

## D. FUA Pay vlastní integrační acceptance před aktivací produkčního ČSOB

Tyto scénáře nejsou náhradou bankovního checklistu; ověřují naši aplikaci jako
celek.

- [x] Přímá platba zakázky kartou: CardJob `PLT-2026-000006` -> `Succeeded`,
      zakázka uhrazena přesně jednou; ověřeno 2026-09-12.
- [ ] Decline/failed scénář: bez kreditu/settlement efektu, srozumitelný stav.
- [ ] Duplicate browser return: žádný druhý finanční efekt.
- [ ] Lost browser return: zavřít/odpojit browser; worker z autoritativního
      `payment/status` stav později bezpečně dokončí.
- [ ] Restart během otevřené/pending platby: po startu se recovery obnoví a
      vznikne nejvýše jeden finanční efekt.
- [ ] Opakovaný `payment/status` nad již úspěšnou platbou: idempotentní.
- [ ] Opuštěná `Pending` platba bez vstupu karty: po TTL přejde do očekávaného
      terminálního stavu a UI nezůstane věčně „čeká“.
- [ ] Return UX: bez 404; bez ručního F5 pro běžný happy/cancel/expired tok.
- [ ] Mobile + desktop smoke hlavního payment flow.

## E. Historický přechodný staging release/deploy gate

Následující checklist zachovává pravidla a evidence již existujícího
přechodného demo/staging runtime. Není návrhem permanentního druhého prostředí.
Budoucí integrační okna používají výše popsaný dočasný izolovaný runtime.

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

## F. Pozdější aktivace produkčního ČSOB až po bankovním schválení

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
