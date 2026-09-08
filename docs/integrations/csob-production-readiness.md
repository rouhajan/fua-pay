# ČSOB production readiness checklist

Status: 2026-09-08

Tento soubor je jediný aktuální checklist pro postup od dnešního integračního
stavu FUA Pay až k bezpečnému production cutoveru. Stabilní technický kontrakt je
v [`csob.md`](csob.md); staging deployment evidence je v
[`../deployment/demo-staging.md`](../deployment/demo-staging.md).

Oficiální ČSOB activation checklist:
https://github.com/csob/paymentgateway/wiki/Activation-of-the-production-environment

## A. Aktuálně prokázaný stav

- [x] Integration Merchant ID `M1EPAY2213` získán.
- [x] Merchant public key registrován u ČSOB; request #7778.
- [x] Merchant private key a integration gateway public key bezpečně nainstalovány
      mimo Git/release.
- [x] GET `echo`: HTTP 200, validní podpis, `resultCode=0`.
- [x] Staging používá živý ČSOB integration provider; simulované platby jsou
      vypnuté.
- [x] Úspěšné dobití kreditu 100 Kč ověřeno 2026-09-07.
- [x] PR #35 nasazen 2026-09-08; return vede na routed payment detail bez 404.
- [x] Druhé úspěšné dobití kreditu 100 Kč ověřeno 2026-09-08.
- [x] Zrušení platby zákazníkem na bráně ověřeno 2026-09-08; lokální stav
      `Cancelled`.
- [x] Reconciliation worker po deployi `Healthy`; public HTTPS smoke PASS.
- [ ] Production traffic není aktivní a nesmí být aktivován před dokončením
      zbytku tohoto checklistu.

## B. Jeden cílený implementační pass před dalšími bankovními testy

Toto je vhodný rozsah pro Codex: nejdřív cíleně porovnat současný ČSOB lifecycle
s oficiální eAPI 1.9/activation dokumentací a s existující FUA Pay architekturou,
poté implementovat jen potvrzené mezery. Neprovádět generický audit celého repa
ani neotvírat uzavřené M0/M1/M2/C-01/C-02 oblasti bez konkrétního defektu.

- [ ] Async stav po návratu z brány bez reloadu celé stránky.
      - detail po returnu smí krátce začít jako `Pending`;
      - klient periodicky načte pouze potřebná data;
      - aktualizuje status badge, relevantní text/akce a kredit v shellu;
      - polling skončí při terminálním stavu nebo po bounded timeoutu;
      - chyba pollingu nesmí změnit finanční stav a ponechá bezpečný refresh
        fallback.
- [ ] Přímý redirect na ČSOB po úspěšném `payment/init` + okamžitém ověření.
      - po zadání částky uživatel nemá zbytečný meziklik;
      - `Details` zůstane recovery cesta pro existující `Pending` platbu a dál
        nabízí `payment/process` odkaz.
- [x] Opravit expired lifecycle: oficiální kombinace ČSOB
      `resultCode=130`, `paymentStatus=6` musí bezpečně skončit jako interní
      `Expired`, nikoli `RequiresAttention` jen kvůli nenulovému resultCode.
- [x] Reconciliation konfigurace nesmí vyčerpat retry pokusy před
      `PaymentTtlSeconds` + provider/worker rezervou; výchozí konfigurace
      podporuje TTL 900 i 1800 sekund bez lokálního odvozování expirace.
- [x] Implementovat POST `echo` se stejnou signing/response-verification/freshness
      hranicí jako GET echo a přidat opt-in integrační test.
- [ ] Implementovat skutečné `payment/reverse` pro ČSOB na existujícím
      provider-neutral základě nebo jiným minimálním způsobem, který zachová
      idempotenci, audit a bezpečné řešení nejasného timeoutu. Bez throwaway
      bypassu jen pro aktivaci.
- [ ] Doplnit cílené unit/integration testy pro nové větve lifecycle a UX
      endpointy.
- [ ] `scripts/verify.ps1` + PostgreSQL gate + live GET/POST echo před merge.

Zaškrtnuté Stage 1 položky výše označují implementaci a lokální automatizované
pokrytí. Živé POST echo a bankovní expired scénář zůstávají samostatně
nezaškrtnuté v sekci C, dokud skutečně neproběhnou.

### Rozhodnutí, která nejsou automaticky součástí tohoto passu

- Refund není povinný ČSOB activation scénář. In-app card refund se implementuje
  jen pokud ho FUA Pay potřebuje pro první production release; jinak se výslovně
  zdokumentuje operátorský postup.
- Nezavádět websocket/SSE jen kvůli jednomu stavu platby. Pro současný rozsah je
  preferovaný jednoduchý bounded polling malého status endpointu.

## C. Povinné ČSOB integration activation scénáře

Podle oficiální wiki upravené 2026-06-30:

- [x] GET echo: HTTP 200, validní podpis, `resultCode=0`.
- [ ] POST echo: HTTP 200, validní podpis, `resultCode=0`.
- [x] Successful authorised payment: integrační testovací karta
      `4000007000010006`, budoucí expirace, CVC `100`; návrat na požadovanou
      stránku ověřen.
- [x] Payment cancelled by customer: `resultCode=0`, `paymentStatus=3` a
      odpovídající lokální `Cancelled` ověřen uživatelským testem 2026-09-08.
- [ ] Expired payment: po >=30 min ověřit `resultCode=130`, `paymentStatus=6` a
      interní `Expired` bez finančního účinku.
- [ ] Payment reversal: po úspěšné autorizaci zavolat `payment/reverse`, ověřit
      HTTP 200, podpis, `resultCode=0`, `paymentStatus=5` a odpovídající lokální
      stav/return evidence.
- [ ] V POS Merchant potvrdit provedení všech povinných scénářů a odeslat je ke
      kontrole ČSOB.
- [ ] Počkat na potvrzení ČSOB, že production environment je aktivovaný.

## D. FUA Pay vlastní integrační acceptance před production cutoverem

Tyto scénáře nejsou náhradou bankovního checklistu; ověřují naši aplikaci jako
celek.

- [ ] Přímá platba zakázky kartou: success -> zakázka uhrazena přesně jednou.
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

## E. Staging release/deploy gate pro každý další ČSOB patch

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

## F. Production cutover až po bankovním schválení

- [ ] Připravit production PostgreSQL workload a jasnou data-transition policy;
      demo/seed data se nesmí omylem propagovat.
- [ ] Připravit production secret/config store: DB, Data Protection keyring,
      Entra config/secrets a ČSOB production signing/verification material podle
      aktivace ČSOB.
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
