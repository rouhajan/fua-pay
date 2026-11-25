# FUA Pay — projektový stav a plán

> **Checkpoint 24. 11. 2026 po C22.** Závazná pravidla jsou v [AGENTS.md](../AGENTS.md), podklady a rozhodnutí v [auditu #108](https://github.com/rouhajan/fua-pay/issues/108) a v [checkpointu C22](https://github.com/rouhajan/fua-pay/issues/108#issuecomment-7137530275).

Správce FUA TUL schvaluje změny finančního chování i nasazení. Codex připravuje vymezené změny a testy, ChatGPT nezávisle kontroluje diff a důkazy. Za provozní přejímku odpovídá správce. Před pokračováním ověř aktuální `main`, pracovní větev a poslední schválený checkpoint. Produkční revize a stav bankovních operací vyžadují samostatný read-only důkaz. Rozlišujeme `VERIFIED-SOURCE` (kontrola zdroje), `TESTED` (provedený test), `OPERATOR-REPORTED` (zpráva správce), `LIVE-VERIFIED` (živé ověření) a `UNKNOWN` (chybějící důkaz).

## Ověřený stav po C22

| Oblast | Stav k checkpointu |
| --- | --- |
| GitHub `main` | `457c5c1eba364105a152c1ce7d38e88749bdc65f`. Obsahuje dokumentační PR [#112](https://github.com/rouhajan/fua-pay/pull/112). |
| Stabilizační kandidát | [Draft PR #114](https://github.com/rouhajan/fua-pay/pull/114) je otevřený, nesloučený a má vypnutý auto-merge. Head `integration/104-113-precommit@ee1e9ef05cad6ed695a1303462077875d25c218f` obsahuje dva commity a tři soubory. |
| FUA Pay v produkci | Zdrojový audit C7-A/B/C byl proveden na baseline `a7438d5d0fb168bffbf116392eb8a39c600906aa`. Aktuální otisk běžící aplikace je **UNKNOWN**. Poslední detailní read-only provozní podklady v auditu jsou z 19. 11. Produkční preflight a přejímka kandidáta dosud neproběhly. [Audit #108](https://github.com/rouhajan/fua-pay/issues/108) |

- **C20 (TESTED):** Release build bez chyb a varování, locked restore, kontrola formátování a EF modelu prošly. Prošlo 1 193 webových testů a 337 databázových testů na každé z izolovaných verzí PostgreSQL 16.15 a 18.4. Obě databázové sady obsahovaly stejných 337 testů. [Nezávislé review C20](https://github.com/rouhajan/fua-pay/issues/104#issuecomment-7136476723)
- **C21 (VERIFIED-SOURCE):** Schválený diff C20 se rozdělil na commit testu [#113](https://github.com/rouhajan/fua-pay/commit/02356ec6cc1a88fc2c7be7ab8455b3d279e202db) a commit opravy a regresí [#104](https://github.com/rouhajan/fua-pay/commit/ee1e9ef05cad6ed695a1303462077875d25c218f). Společný diff zůstal bajtově shodný. [Nezávislé review C21](https://github.com/rouhajan/fua-pay/issues/104#issuecomment-7137253998)
- **C22 (TESTED):** GitHub CI na testovacím merge commitu se shodným stromem prošlo s 1 193 webovými a 337 databázovými testy na PostgreSQL 18.4. Prošly i všechny tři analýzy CodeQL (C#, JavaScript/TypeScript, Actions). Stav bezpečnostních alertů vyžaduje samostatné vyhodnocení. [CI](https://github.com/rouhajan/fua-pay/actions/runs/45435054687), [CodeQL](https://github.com/rouhajan/fua-pay/actions/runs/45435054680), [nezávislé review C22](https://github.com/rouhajan/fua-pay/issues/104#issuecomment-7137524919)

Historický incident za 93 Kč z 18. 11. byl finančně vyřešený. Oprava příčiny souběhu inicializace s reconciliation dosud nebyla nasazena. [#104](https://github.com/rouhajan/fua-pay/issues/104)

[#113](https://github.com/rouhajan/fua-pay/issues/113) ověřuje správný SQLSTATE pro `RESTRICT` na obou PostgreSQL řadách se stejnou kontrolou cizího klíče. Obě opravy jsou ověřené a připravené pro širší stabilizační integraci. Issues zůstávají otevřené a kandidát dosud nebyl nasazen ani na staging.

## Otevřené otázky stabilizace

| Issue | Doložený stav a další krok |
| --- | --- |
| [#105](https://github.com/rouhajan/fua-pay/issues/105) | Neuzavřený pokus o dobití 500 Kč s nejasnou inicializací bez `payId`. Výsledek banky i příčina přerušení HTTP jsou **UNKNOWN**. Zjištění u banky oddělit od případné prevence přerušení požadavku v aplikaci. |
| [#109](https://github.com/rouhajan/fua-pay/issues/109) | Zdrojově potvrzené riziko ztráty podepsané bankovní evidence úspěchu platby (`0/7` nebo `0/8`) při rollbacku lokálního zaúčtování. Výskyt v produkci je **UNKNOWN**. |
| [#110](https://github.com/rouhajan/fua-pay/issues/110) | Před refundem CardTopUp ověřit původní kreditní pohyb `Credit.operation_id = Payment.Id` a shodu vlastníka, typu a částky. Při nesrovnalosti refund zablokovat a ověřit dopad na disponibilní kredit. |
| [#111](https://github.com/rouhajan/fua-pay/issues/111) | Zdrojově potvrzené riziko ztráty vazby přímé podepsané odpovědi `0/10` na konkrétní operaci refundu při rollbacku. Výskyt v produkci je **UNKNOWN**. |
| [#108](https://github.com/rouhajan/fua-pay/issues/108) | Zdrojový audit C7-A/B/C je dokončený. Otevřené P2 zahrnují povolení wildcardů v `AllowedHosts` a možné logování podepsané URL ČSOB. Skutečný produkční dopad je **UNKNOWN**. |

U zjištění C7-A/B/C používat klasifikaci `CONFIRMED` / `TEST GAP` / `EXTERNAL UNKNOWN` / `NOT A BUG`. K plošnému auditu se vrátit pouze s novým důvodem.

## Další krok a společné vydání

1. **Nejdřív společný read-only návrh pro #109 a #111 nad ověřeným kandidátem #114.** Určit, jak uchovat ověřený bankovní důkaz přes rollback a restart a jak jej svázat s konkrétní platbou nebo pokusem o refund. Zachovat atomické lokální účtování. Refund potvrdit pouze s důkazem svázaným s konkrétní operací. Samotný agregovaný status k potvrzení nestačí a nejasný PUT se neopakuje. Dokud chybí důkaz opravňující uvolnění, blokace zůstává zachovaná.
2. Návrh předat k nezávislé kontrole a schválení správcem. Až poté připravit cílené RED regresní testy pro rollback, restart, souběh a idempotenci, minimální opravy a GREEN ověření. #110, bankovní šetření #105 a obě P2 z #108 mají samostatný rozsah a schválení.
3. Zachovat **jedno koordinované stabilizační vydání FUA Pay**. Spojit pouze schválené opravy a zopakovat kompletní regresi, CI a CodeQL, PostgreSQL 16.x a 18.x, EF migrace, scénáře se simulovanou bankou a ověření hranic Print Reserve/Capture/Release.
4. Pro tentýž build provést izolovaný staging. Správce ověří platby, doklady a kredity. Izolovaná přejímka tisku má vlastní testovací tok a souhlas. Produkce vyžaduje samostatné GO, ověření běžící revize, čerstvou zálohu, posouzení obnovitelnosti a migračního návratu a read-only kontrolu finančních invariantů před nasazením i po něm. Termín nasazení závisí na splnění kontrol a souhlasu správce.

## Oddělené projekty

- **FUA Print:** samostatný repozitář a release. Poslední doložené vydání je `rouhajan/fua-print@3fa41968a10fddef71d184778be6f15491bcbde7` (15. 11.). Přejímka pokryla 12/12 PC s frontou `FUA PRINT` a nativním Xerox GPD PostScript. Doložila placený výtisk i odpovídající zaúčtování. `rouhajan/fua-print-production` na `main@207103b…` je očištěný snapshot zdrojů. Režim PAID po přepnutí hlásil správce (`OPERATOR-REPORTED`). Aktuální živá revize a režim jsou `UNKNOWN`. Finanční hranici ve FUA Pay testovat izolovaně od živého tisku. Samostatná identitní otázka je v [#92](https://github.com/rouhajan/fua-pay/issues/92).
- **Zálohy a DR:** poslední doložený checkpoint z 19. 11. uvádí dvě šifrované off-VM kopie databáze, konfigurace a klíčů s ověřenou integritou. Izolovaný restore test dosud neproběhl. Retence, automatizace, PITR/WAL, druhé DC a cíle RPO/RTO patří do samostatného projektu.
- **Účetní funkce:** hromadné stažení neměnných dokladů a měsíční výkaz podle schválené referenční sestavy mají vlastní schválení. [#106](https://github.com/rouhajan/fua-pay/issues/106), [#107](https://github.com/rouhajan/fua-pay/issues/107)
- **Přenositelnost:** vydavatel dokladů a DPH jsou vázané na TUL, návratové URL ČSOB pro produkci a staging na domény FUA. Konfigurace, branding i návody předpokládají TUL. Samostatný milník Independent Deployment má zpřístupnit bezpečné nastavení vydavatele, daní, domén, Entra a bankovního obchodníka. Cílem je vlastní instalace a databáze pro každou instituci bez nutnosti multitenancy.
- **Provozní dohled a udržitelnost:** samostatně rozvíjet read-only přehled finančních výjimek, alerty, postupy pro správce a reprodukovatelný build, vydání a obnovu.
