# FUA Pay — projektový stav a plán (kanonický rozcestník)

> **Aktualizováno 23. 11. 2026 (převzetí a nezávislé ověření C7-A/B/C).** Tento dokument je stručný **živý handoff**, nikoli seznam všech důkazů ani automatické potvrzení dnešního živého provozu. Po každém významném kroku jej aktualizuj na schválené větvi/PR. **Závazná pravidla vývoje:** [`AGENTS.md`](../AGENTS.md). **Detailní finanční evidence:** [audit #108](https://github.com/rouhajan/fua-pay/issues/108).
>
> **Ztratil se chat?** Nejdřív přečti tento soubor, `AGENTS.md` a linked issues; ověř aktuální `main`, reálný release a lokální změny. **Žádné úpravy kódu ani produkce na základě samotného tohoto snapshotu.**

## 1. Kdo, co a podle čeho

- **Správce/rozhodovatel:** jediný vývojář a provozovatel FUA TUL. Změny finančního chování a provozní nasazení schvaluje osobně.
- **Codex:** jednotlivé vymezené úkoly, izolované větve/worktrees, reprodukční testy a minimální patch.
- **ChatGPT:** nezávislá kontrola skutečného kódu/diffu, vztahů, výsledků a GitHub issue evidence. Není druhá lidská schvalovací osoba. AI testy nejsou náhradou provozního ověření.
- **Zdroj pravdy podle typu:** GitHub pro commitnutý kód a issues; lokální worktree pro nedodanou práci; VM/DB/ČSOB/POS Merchant pro běžící software a skutečné finanční stavy. README, stará dokumentace ani `main` automaticky neprokazují produkční release.
- **Úroveň tvrzení:** `VERIFIED-SOURCE` (přečtený kód), `TESTED` (skutečně spuštěný test + prostředí), `OPERATOR-REPORTED` (informace správce), `LIVE-VERIFIED` (aktuální read-only důkaz VM/DB/banky), `UNKNOWN` (bez důkazu). Stav z tohoto souboru je historický k uvedenému datu.

## 2. Výchozí stav k 22. listopadu 2026

| Oblast | Poslední doložená informace | Co nesmíme zaměnit |
| --- | --- | --- |
| FUA Pay finanční audit | Analyzovaná produkční baseline `a7438d5d0fb168bffbf116392eb8a39c600906aa` | Před dalším produkčním krokem znovu ověřit skutečně běžící proces a binární artefakt; formální GATE 0 v #108 zůstává otevřen |
| GitHub `main` | `83181232a6bfec0492830680cc22357f19036355` | Není automaticky release na VM; porovnání změn vůči baseline bez tichého „povýšení“ |
| Provozní stav FUA Pay | Správce 22. 11. nepozoruje nové transakční problémy; poslední detailní read-only data jsou z 19. 11. | Dobrá provozní zpráva není uzavření všech incidentů ani nová úplná DB kontrola |
| FUA Print živý Linux | `rouhajan/fua-print@3fa41968a10fddef71d184778be6f15491bcbde7` (ověřená evidence z 15. 11.) | Správce potvrzuje **kód beze změn, aktuální PAID** po pozdějším přepnutí FREE → PAID; nové živé readback dosud neprovedeno |
| FUA Print klienti | 12/12 `FUA PRINT`, nativní Xerox GPD PostScript; reálný PAID papír a debit doložen při přejímce | `rouhajan/fua-print-production` `main@207103b...` je očištěný **source snapshot, nikoli** nasazený release; historické větve `fua-print` nejsou aktuální produkce |
| Zálohy | Poslední doložený checkpoint 19. 11.: šifrované off-VM DB + konfigurace/klíče, dvě kopie, hash/integrita zkontrolované | **NEPROVEDEN** izolovaný restore test; automatický PITR/WAL a druhé DC nejsou hotové. Zálohování řešit v samostatném vlákně/projektu |
| ČSOB/refundy | Běžné karetní platby fungují; **reverse/refund zatím podle správce v reálu nepoužity** | Nepotvrzuje to bezchybnost všech bankovních okrajových stavů; #105 bankovní stav zůstává UNKNOWN |

## 3. Aktuální práce — platební stabilizace (23.–29. 11., bez časově vynuceného nasazení)

**Priorita je dokončit celou mapu problémů PŘED další opravou** a pak připravit **jeden koordinovaný FUA Pay release**. Nezasahovat do FUA Print kódu ani do odděleného backup/DR projektu.

### Audit / aktuální checkpoint

- **#108** je hlavní auditní rozcestník s nezávislými zdrojovými revizemi a operátorskými checkpointy. Detailní závěry ponechat v issues, v tomto souboru jen aktuální navigaci.
- **C7-A (Codex, read-only):** hotový zdrojový coverage report pro vznik platby, init/reconciliation/settlement. Nezávislé potvrzení dalšího pre-status race v #104. Neznamená, že jsou všechny varianty potvrzenou produkční chybou.
- **C7-B (Codex, read-only): PŘEVZAT 23. 11. a podstatné nálezy nezávisle ověřeny proti skutečnému kódu.** Bez spuštění testů. Žádné nové P0, žádný důkaz přečerpání tiskového kreditu; nově potvrzena riziková cesta #111: přímý podepsaný refund `0/10` může po rollbacku lokálního settlementu skončit jen v obecné nejistotě bez trvale svázaného důkazu. Výskyt na produkci **UNKNOWN**. Viz [ověření v #111](https://github.com/rouhajan/fua-pay/issues/111#issuecomment-7096251923). Soubor s relevantní službou je blob-identický na auditní baseline i aktuálním `main`.
- **C7-C (Codex read-only, 23. 11.): ZDROJOVÉ POKRYTÍ DOKONČENO, nejdůležitější nálezy nezávisle ověřeny ChatGPT.** Bez nových P0/P1; dvě P2: `AllowedHosts` propouští ASP.NET Core wildcard `0.0.0.0` / `[::]` a ČSOB status URL obsahující podpis může protéct do `IHttpClientFactory` Information logu. Produkční skutečný dopad **UNKNOWN**. Auth/role/owner scope, audit/outbox a health kontrolovány zdrojově, další mezery jsou výslovně označené `TEST GAP` nebo `EXTERNAL UNKNOWN`. Viz [nezávislý C7-C checkpoint v #108](https://github.com/rouhajan/fua-pay/issues/108#issuecomment-7097067287). Tento průchod **nespouštěl testy a neprokazuje živý stav VM**.
- **#104 C5/C6:** správce hlásí izolované **lokální** testovací a servisní commity `fcfc885fa26bd54eb21f3568f47d9edd625a0bce`, `42d9b78494600734b6023732b0233101b178cdb4` ve worktree `C:\Projects\fua-pay-audit-race-104`. Oprava C5 pro *jeden* race byla nezávisle zkontrolována, **nepushována, nenasazena, nepokrývá C7-A nový pre-status race**. Zachovat commity bez přepisování.
- Codex/ChatGPT read-only zdrojové audity **neměnily** code, DB, bankovní operace ani produkci. Testovací PASS z jiného cyklu nezaměňovat za právě nově spuštěný test.

### Rizika / backlog, jeden issue = jeden problém

| Oblast | Stav / účel | Vazba |
| --- | --- | --- |
| [#108](https://github.com/rouhajan/fua-pay/issues/108) | **Source audit C7-A/B/C dokončen jako mapa pokrytí; P2 `AllowedHosts` a bankovní status URL v HTTP logování**. Live dopad a konfigurace neověřeny, testy/opravy teprve čekají. | Zaznamenat přesný izolovaný RED test + minimální patch, nemíchat s bankovní state machine | 
| [#104](https://github.com/rouhajan/fua-pay/issues/104) | **Potvrzená** chyba souběhu inicializace/reconciliation, reálný incident 18. 11. finančně vyřešen; zbývající souběhy k reprodukci a cílené opravě | Init + recovery |
| [#109](https://github.com/rouhajan/fua-pay/issues/109) | **Potvrzená riziková cesta v kódu:** při rollbacku lokálního settlementu není durable bankovní úspěch dostatečně zachycen; výskyt v produkci neprokázán | Recovery + settlement; nesmí se obejít atomicita |
| [#105](https://github.com/rouhajan/fua-pay/issues/105) | **Skutečný nejasný pokus** inicializace 500 Kč bez `payId`; bankovní výsledek **UNKNOWN**, příčina HTTP odpojení nepotvrzena | Bankovní autorita zvlášť od případné kódové opravy |
| [#110](https://github.com/rouhajan/fua-pay/issues/110) | Preventivní fail-closed kontrola *původu* původního CardTopUp kreditu před refundem; riziko při nekonzistentních historických datech | Kredit/return hold/Print dostupnost |
| [#111](https://github.com/rouhajan/fua-pay/issues/111) | Nejasný refund PUT; **nově zdrojově ověřená P1 ztráta vazby přímé podepsané refund odpovědi `0/10` při rollbacku lokálního potvrzení**. Bankovní/produkční výskyt neprokázán. | Společný návrh evidence s #109, ale jiná bankovní operace; bez opakování PUT |
| [#92](https://github.com/rouhajan/fua-pay/issues/92) | Samostatné dlouhodobější riziko recyklovaného školního e-mailu při tiskovém credentialu; fail-closed správně brání záměně účtů | **Ne** příčina známých ČSOB incidentů |
| [#106](https://github.com/rouhajan/fua-pay/issues/106) | Hromadné stažení již existujících neměnných dokladů | Samostatná funkce, ne finanční hotfix |
| [#107](https://github.com/rouhajan/fua-pay/issues/107) | Finální měsíční účetní výkaz podle schválené **golden fixture** | Samostatná funkce; stávající diagnostický export nesčítat jako příjmy |

### Následující konkrétní kroky (přečíst v novém chatu)

1. **Před pokračováním vždy ověřit v GitHubu, že PR #112 je skutečně merged** a že kořenový `AGENTS.md` a `docs/PROJECT-STATE.md` existují na `main`; samotné schválení nebo čekající CI nestačí. Vlastník schválil čisté dokumentační sloučení 23. 11. podmíněně po posledním úspěšném CI a diff review. C7-B a C7-C jsou již zdrojově nezávisle ověřené v #111 a #108.
2. **Auditní coverage C7-A/B/C je dokončená na zdrojové úrovni; neplánovat opakované plošné audity bez nového důvodu.** Sestavit závěrečnou akceptační matici `CONFIRMED / TEST GAP / EXTERNAL UNKNOWN / NOT A BUG` včetně dvou P2 z C7-C; odlišit skutečný defekt od chybějícího testu, vybrat společný minimální scope a teprve poté autorizovat implementační větve.
3. Pro #104 doplnit RED testy pre-status mixed-read, pozdní signed 0/7–8 a případně expiry; prověřit ochranu C5, navrhnout společnou nejmenší bezpečnou opravu. Nesmí vzniknout dvě protichůdné opravy init/reconciliation.
4. #109 a #111 společně navrhnout **nezávisle trvale uchovávanou, přesně přiřazenou bankovní evidenci** přes rollback/restart (ale nezaměnit úspěch platby `0/7,0/8` s konkrétním refundem `0/10`); #105 oddělit prevenci client-abort od nezjištěného bankovního stavu; #110 doplnit původní ledger provenance guard a opravit nekanonickou testovací fixture. Samostatně dva úzké P2: `AllowedHosts` zakázat všechny ASP.NET host wildcardy; u ČSOB HTTP klienta zastavit únik podepsané URL do logů bez ztráty bezpečné diagnostiky. Žádné auto-confirm z agregovaného refund statusu, opakovaný PUT, zásah do Print nebo neprokázaný bankovní debit.
5. Každý jednotlivý patch: izolovaná větev/worktree → deterministické RED → minimální změna → GREEN plus negativní testy → **nezávislé skutečné diff review** → srozumitelná tabulka před/po → schválení správce.
6. Teprve po schválení složit **jeden integrační kandidát**; provést kompletní CI/CodeQL, full regression, PostgreSQL **16.x parity** (vedle současného CI na 18.x), bankovní mock/error tests a Print Reserve/Capture/Release cross-feature testy.
7. Přesně jeden build → **izolovaný staging před produkcí**; zvlášť ČSOB platební acceptance, zvlášť bezpečně izolované finanční API/FAKE/physical Print acceptance; správce vyzkouší UI, doklady, kredit, fyzický papír a přesně jeden debit tam, kde to testovací infrastruktura bezpečně dovolí. Žádné přesměrování živých studentů na staging.
8. Před Production **znovu načíst live FUA Pay/Print SHA a mód**, čerstvou zálohu a read-only finanční invarianty, vyhodnotit restore/RPO riziko a kompatibilitu migračního/rollback plánu. Nasadit pouze po výslovném GO; po nasazení provést bounded read-only finanční kontrolu a zdokumentovat reálný výsledek.

**Definition of Done tohoto balíku:** doložené relevantní defekty vyřešené bez regresí, s testy a staging/operátorskou přejímkou; nedoložené nebo externě blokované skutečnosti výslovně `UNKNOWN`, nikoli předstírané `PASS`. Žádné vynucené dokončení do konkrétního dne.

## 4. Oddělené, ale související dlouhodobé směry

1. **Zálohy / disaster recovery** *(samostatné vlákno)*: z ručních šifrovaných záloh přejít na automatizaci, retenci, WAL/PITR, off-site/druhé geograficky oddělené univerzitní DC, opakovaný izolovaný restore a dokumentované RPO/RTO. Záložní VM/HA posoudit zvlášť; replika není záloha. Při platebních změnách chránit obnovitelnost bankovních důkazů a kompatibilitu DB, ale nepřidávat backup architekturu do platebního patche.
2. **Provozní stabilita:** samostatný read-only přehled finančních výjimek (`Created/Uncertain`, staré `Pending`, `RequiresAttention`, lease/retry, bank vs ledger/doc, tiskové rezervace), bezpečné operátorské runbooky, opakované kontroly a alerty. Zdravý worker ≠ finanční reconciliation PASS.
3. **Účetní pohodlí:** #107 golden fixture měsíčního výkazu, #106 ZIP existujících dokladů, bez změn finančních účinků a historických dat; samostatné schválení.
4. **Přenositelnost pro jiné fakulty / vlastní server:** až po stabilizaci vytvořit samostatný milník **Independent Deployment**. Nyní **není hotovo**: vydavatel/DPH dokladů jsou v kódu TUL-specifické, ČSOB Production/Staging return URL je bezpečnostně připnutá k FUA doménám, appsettings/branding a návody předpokládají TUL. Cíl: bezpečně konfigurovatelný institucionální issuer/tax policy a doména bez oslabení fail-closed; neutrální branding, vlastní Entra tenant/merchant, kompletní instalační/restore runbook a zkouška čisté instalace správcem bez asistence autora. **Bez nutnosti multitenancy**: samostatná instalace a DB pro každou fakultu.
5. **Udržitelnost jednoho správce:** reprodukovatelný release a obnova, automatické testy a dohled, dokumentovaný incident handling, aktualizace závislostí a srozumitelný předávací postup tak, aby systém nebyl závislý na paměti autora.

## 5. Jak aktualizovat, aniž by vznikl nepořádek

- **Tady:** jen krátký snapshot a nejbližší akce. Po významném auditu, výběru scope, schváleném patchi, stagingu či releasu aktualizovat čas, SHAs, skutečné výsledky a další krok. Nezapisovat všechny logy a dlouhé testovací výpisy.
- **GitHub issues:** veškeré konkrétní důkazy, file:line, RED/GREEN, incidenty, varianty a odůvodnění. Odkaz z tohoto dokumentu je dostačující.
- **`AGENTS.md`:** pouze stabilní zásady; měnit výjimečně, samostatně zdůvodněným review. Krátkodobé úkoly ani běžné provozní údaje sem nepatří.
- **Ostatní repo (`fua-print`, `fua-print-production`, `fua-classroom`) a backup/DR:** vlastní zdroj pravdy; zde jen ověřená integrační hranice a odkazy.
- **Veřejný GitHub:** žádná skutečná PII, finanční ID konkrétních osob, tajemství, syrové bankovní logy nebo neveřejné produkční archivní cesty s citlivým obsahem.

### Jednověté zadání pro nové vlákno

> Navazujeme na `rouhajan/fua-pay`. Přečti aktuální `AGENTS.md`, `docs/PROJECT-STATE.md` a issues #108, #104, #105, #109–#111; nezaměňuj auditní SHA s GitHub main či živým procesem. Nejprve ověř stav a další checkpoint; žádný kód/produkce/ČSOB bez předchozího scope, testů, nezávislé revize a mého schválení.
