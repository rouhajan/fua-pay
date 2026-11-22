# FUA Pay — závazná pravidla vývoje (AGENTS.md)

> **Platí pro celý repozitář a všechny změny.** Účel: chránit fungující produkční finanční systém a zajistit, aby na projektu mohl bez ztráty kontextu navázat jiný chat, Codex nebo správce.
>
> **Začni vždy přečtením** [aktuálního projektového stavu](docs/PROJECT-STATE.md), příslušného GitHub issue a relevantních technických dokumentů. Tento soubor je trvalá pravidlová vrstva; `docs/PROJECT-STATE.md` je průběžně aktualizovaný stav. Detailní zdrojové důkazy patří do issues, nikoli opakovaně do statusu.

## 1. Základní dogmata

1. **Správnost peněz před rychlostí, jednoduchým „PASS“ a pohodlím.** Nikdy nevytvářet falešné potvrzení platby, kreditu, tisku, refundu nebo dokladu. Výsledek banky neodhadovat z browseru, HTTP timeoutu, souhrnného statusu či lokálního přání.
2. **Neopravovat nepodložené problémy.** Rozlišovat potvrzený defekt (reprodukce nebo průkazná chybná cesta v kódu), neotestovaný scénář, externí neznámou skutečnost, provozní omezení a nový požadavek. Absence testu sama není důkaz chyby.
3. **Fungující chování je kontrakt.** Žádné přepisování architektury, preventivní refaktory, změny API, UI, DB schématu či jiného modulu mimo odsouhlasenou příčinu; pokud je průřezový zásah nevyhnutelný, nejdříve posoudit všechny návaznosti.
4. **Žádné obcházení pojistek.** Nesnižovat bezpečnost podpisů ČSOB, idempotence, autorizace, validací, databázových transakcí, zámků, unikátních indexů, kontroly disponibilního kreditu, auditu, tiskových rezervací ani ochrany před opakováním bankovních PUT.
5. **Pravdivé výsledky.** Uvádět přesný zdroj, revizi, provedené a neprovedené testy, předpoklady a limity. Nikdy nevydávat kontrolu zdrojáku za spuštěné testy, test za živou bankovní akceptaci ani checksum zálohy za úspěšnou obnovu.
6. **Autor rozhoduje.** Správce projektu je jediný vývojář/provozovatel. Codex může navrhovat a implementovat; ChatGPT může nezávisle revidovat; žádné AI rozhodnutí nenahrazuje výslovné schválení změny a oddělené produkční GO.

## 2. Finanční invarianty — nesmějí regredovat

- Jen důvěryhodné, serverově a kryptograficky ověřené bankovní podklady smějí autorizovat ČSOB settlement; nerozhodovat podle browser returnu či nejednoznačného HTTP výsledku.
- Jedna oprávněná úhrada → **nejvýše jeden** kanonický finanční účinek (CreditMovement nebo uhrazení Job) a odpovídající jedinečný doklad; nesmí vzniknout kredit/doklad bez zdroje. Opakování, více workerů, restart, timeout ani souběh nemění výsledek.
- Změny Payment, kreditu/Job, dokladu a souvisejícího auditu tvoří schválenou atomickou databázovou hranici. Při selhání bez částečného lokálního zaúčtování; ověřená bankovní evidence přitom nesmí zmizet bez auditovatelného bezpečného zotavení.
- Kreditní účet, historické pohyby a disponibilní zůstatek musejí souhlasit. Zámky a idempotency klíče chrání souběžné platby zakázky, debity, refundové blokace a tisk.
- FUA Print: Reserve pouze blokuje disponibilní kredit; Capture je právě jeden atomický debit; Release nesmí debitovat; ResolutionRequired drží blokaci do bezpečného objasnění. Zachovat source/command identity a stávající API kontrakt.
- Refund/reverse: nejprve ověřit původ a disponibilitu peněz; provider attempt zaznamenat před odesláním. **Nejasný PUT nikdy slepě neopakovat; stav celkové platby nedokazuje konkrétní partial refund.** Neautorizovat ruční opravy bez důkazů.
- Neměnné historické doklady, identity původních finančních operací, auditní stopa a účetní provenience musejí zůstat zachovány. Diagnostické exporty s více attempt řádky nelze sčítat jako jednotlivé příjmy.

## 3. Povinný proces každé změny

1. **Připnout pravdu:** samostatně zjistit GitHub `main`, auditovaný SHA, běžící produkční release a stav lokálního pracovního stromu. Tyto tři zdroje se nesmějí zaměňovat. Neověřené hodnoty označit `UNKNOWN`.
2. **Předem vymezit scope:** issue, potvrzená příčina, stav „před“, požadované „po“, nezměněné kontrakty, rizika, závislosti, akceptační kritéria, případná migrace. Překryv oprav řešit v jednom návrhu *před* patchováním.
3. **Izolovat práci:** pouze vyhrazená větev/worktree, žádná přímá editace chráněné `main`; respektovat již připravené a nenasazené lokální změny, neodstraňovat je a nedělat implicitní merge.
4. **RED → minimální PATCH → GREEN:** nejprve deterministický reprodukční test tam, kde je chyba testovatelná; doplnit negativní/fail-closed a fault-injection testy, souběh, restart, timeout, rollback, idempotenci a všechny dotčené moduly. Testy v izolované DB PostgreSQL produkční řady a potřebných mockech. Žádné finanční testy na produkčních datech nebo bankovní mutující endpoint bez samostatného svolení.
5. **Dvojí kontrola bez fiktivního druhého zaměstnance:** Codex doloží diff a testy; ChatGPT nezávisle čte **skutečný** diff, zdroje a návaznosti, kriticky prověřuje tvrzení i testy; vlastník dostane srozumitelné **před/po**, změněné soubory, výsledky testů a rizika. Schválení vlastníkem je nutné.
6. **Společná integrace:** seskupit pouze jednotlivě schválené opravy do kontrolované integrační větve; znovu prověřit výsledný diff a souběžné testy všech funkcí, nejen „zelené“ izolované opravy. Povinné CI, CodeQL, Release build, EF model/migrace, PostgreSQL 16 kompatibilita, bezpečnost a reprodukovatelný artefakt.
7. **Staging před produkcí:** nasadit a otestovat přesně tentýž build v izolovaném stagingu. Uživatel osobně ověří UI a finanční účinky; pro tisk také fyzický výstup/odečet v *prokazatelně izolovaném* testovacím toku. Platební a tiskové acceptance fáze oddělit. Živý FUA Print nepřesměrovávat na staging bez samostatně schváleného plánu.
8. **Produkční GO je oddělené rozhodnutí:** aktuální záloha a připravený návrat, finanční read-only preflight, source/binary fingerprint, migrační kompatibilita a explicitní svolení. Nasazení je řízené; rollback kódu **nevrací** transakce banky ani forward-only DB migraci. Po nasazení read-only finanční a provozní acceptance.

## 4. Vnější hranice a citlivá data

- `rouhajan/fua-pay` je veřejný: nikdy nezapisovat produkční identifikátory zákazníků/plateb, emaily, orderNo/payId určené k dohledání konkrétních operací, bankovní klíče, bearer tokeny, podpisy, signed URLs, kompletní env, osobní údaje ani raw bankovní logy do issues, PR, commitů nebo test fixtures. Veřejně jen redigované syntetické důkazy; citlivé podklady chránit mimo repo.
- FUA Pay je **jediná autorita kreditu**. FUA Print má vlastní produkční repozitář, historii a samostatný release proces. Bez výslovného scope neměnit tiskový klient, režim FREE/PAID, server ani CUPS; neplést vyčištěný source snapshot s živým vydáním.
- Zálohování/PITR/DR a přenositelnost na jiné fakulty jsou **samostatné projekty**. Jejich požadavky respektovat při návrhu změn, ale nepřimíchávat neodsouhlasený backup/HA/multitenant refaktor do platební opravy.
- Pokud externí autorita (ČSOB, Entra, účetní schválení, infrastruktura) nebyla ověřena, uvést blokaci. Nepřidávat bypass jen pro úspěšný test.

## 5. Kontinuita, dokumentace a zastavení práce

- Při každém novém vlákně/cyklu číst **`AGENTS.md` → `docs/PROJECT-STATE.md` → příslušné issues → aktuální soubory/commity**. Nepokračovat podle paměti asistenta místo ověření GitHubu a živých stavů.
- `docs/PROJECT-STATE.md` udržovat jako krátký rozcestník: **ověřený stav, otevřené blokace, další přesný krok, odpovědnost, krátkodobý a dlouhodobý plán**. Po významném auditu, změně plánu, schváleném patchi, stagingu a releasu aktualizovat; nezdvojovat rozsáhlou evidenci z issues.
- Detailní technické nálezy patří do odpovídajícího issue; finanční audit má rozcestník [#108](https://github.com/rouhajan/fua-pay/issues/108). Nový issue vytvářet jen pro odlišný, doložený úkol. Nepřepisovat historii výsledků.
- **STOP** před rizikovou změnou, pokud není jasný produkční SHA, bankovní autorita, regresní dopad, izolace testu nebo potřebné svolení. **Před produkčním GO** musí být samostatně doložena obnovitelnost nebo výslovně posouzeno a vlastníkem přijato její neověřené riziko; samotný odložený restore test nesmí zablokovat read-only audit či izolované testy. Zastavení je bezpečný výsledek, nikoli podnět k obcházení.
