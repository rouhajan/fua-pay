# Post-audit hardening

- Datum: 25. 8. 2026
- Výchozí baseline: `09f032f3ce511cd6f44428f370e278c7eb4c8bd2`
- Pracovní větev: `hardening/post-audit-findings`
- Důvod: uzavření potvrzených auditních nálezů F-01 až F-05 bez změny finančních, autorizačních a ČSOB protokolových pravidel mimo jejich scope.

## F-01 – koordinace finančních cest zakázky

Původní problém spočíval v oddělené kontrole otevřené přímé platby, kreditní úhrady a storna. Mezi kontrolou a zápisem mohla jiná cesta změnit stejnou zakázku.

Implementace zavádí `IJobPaymentCoordination`, jehož PostgreSQL implementace uzamkne řádek zakázky pomocí `FOR UPDATE`. Všechny tři konfliktní cesty nejprve ve vlastní transakci získají stejný row lock a teprve pod ním znovu ověří stav zakázky a blocking payment. Nová přímá platba se uloží ve stavu `Created` před externí inicializací; volání poskytovatele proběhne až po commitu. Kreditní úhrada a storno při otevřené přímé platbě skončí `JobPaymentInProgressException`.

Zachované invarianty:

- jedna blocking přímá platba na zakázku zůstává chráněna existujícím partial unique indexem;
- kreditní i přímé settlement zůstávají idempotentní;
- audit a outbox se zapisují ve stejné transakci jako příslušná lokální změna;
- nevznikl globální aplikační zámek ani globální `SERIALIZABLE` isolation.

Přidány byly PostgreSQL testy pro existující blocking payment proti kreditu a stornu a dva řízené souběhy vytvoření direct payment proti kreditu a stornu. Bariéra testu se uvolňuje až po skutečném získání DB locku; test nepoužívá `Sleep`. Unit testy navíc ověřují odmítnutí kreditu a storna bez lokální změny.

Status: implementováno. PostgreSQL testy jsou součástí projektu a kompilují, ale v tomto lokálním prostředí nebyly spuštěny; podrobnosti jsou v sekci Verification.

## F-02 – atomický recoverable scheduler

Původní candidate `SELECT` s `LEFT JOIN`, zámkem initiation řádku a následným samostatným načtením recovery mohl při `READ COMMITTED` vrátit stejného kandidáta dvěma transakcím. Durable recovery zůstala jedna, ale druhá transakce mohla nesprávně zvýšit count, verzi a vytvořit duplicitní audit.

`ScheduleRecoverableUncertainAsync` nyní provede v jediném SQL statementu omezený výběr s `FOR UPDATE OF i SKIP LOCKED` a `INSERT ... ON CONFLICT ... DO UPDATE ... WHERE ... RETURNING`. Candidate-row lock zabrání dvěma schedulerům současně vložit stejnou provider reference; konfliktní transakce kandidáta přeskočí. Z `RETURNING` se dále zpracují pouze řádky, u kterých skutečně vznikl nebo byl oprávněně obnoven durable scheduling stav. Vrácený count a `payment.provider-initiation.verification-scheduled` audit jsou odvozeny pouze z těchto řádků a zůstávají ve společné transakci.

Původní race test nově vedle součtu `1` ověřuje přesně jeden scheduling audit. Existující claim/lease token, stale-worker a settlement testy nebyly oslabeny.

Status: implementováno bez změny schématu. PostgreSQL provedení testu je v tomto prostředí `NOT RUN`.

## F-03 – ztracený recovery claim

`TransitionWithAuditAsync` už dříve vracel `false`, když lease token přestal patřit workeru, ale call-site tento výsledek ignoroval a vykázal úspěšný transition.

Processor nyní převádí neprovedený repository transition na `ClaimLost`. `CsobPaymentRecoveryCycleResult` obsahuje `LostClaimCount`; v takovém případě se nezvýší `CompletedCount`, `RescheduledCount` ani `RequiresAttentionCount`. Strukturovaný worker log obsahuje samostatný počet ztracených claimů. Transakční audit neprovedeného transitionu se rollbackne stejně jako dosud a nový vlastník lease může pokračovat.

Unit test simuluje `false` z dokončovacího transitionu a ověřuje jeden ztracený claim a nulový počet všech úspěšných disposition. Stávající úspěšné completion a retry testy zůstaly zelené.

Status: implementováno.

## F-04 – audit JIT provisioningu

První přihlášení atomicky vytvářelo interního uživatele, externí identitu a roli `Customer`, ale nevytvářelo centrální audit.

`AccessIdentityService` nyní před `IAccessUserRepository.AddAsync` stageuje právě jednu procesní událost `access.user-provisioned` pro entitu `access-user`. `EfAccessUserRepository.AddAsync` ukládá user, identity, role i staged audit jedním `SaveChanges`. Selhání auditu proto rollbackne celý provisioning. Při souběhu vyhraje jeden unique insert; loser vyčistí change tracker a reloaduje vítěze, takže jeho staged audit se necommitne. Login existujícího uživatele provisioning audit nevytváří.

Unit testy ověřují obsah nového auditu a absenci auditu pro existující identitu. PostgreSQL testy ověřují jednu událost po prvním loginu, jednu událost po concurrent first-login race a rollback identity při vynuceném selhání auditu.

Status: implementováno bez změny schématu. PostgreSQL provedení testů je v tomto prostředí `NOT RUN`.

## F-05 – observability reconciliation workeru

Dosavadní health endpointy rozlišovaly proces a databázi, ale úspěšný idle reconciliation cyklus nezanechal pozitivní signál.

Nový singleton `CsobPaymentReconciliationHealth` drží pouze in-memory čas posledního úspěšného cyklu, čas posledního neúspěšného cyklu a typ aktuální chyby. Worker zaznamená úspěch po každém dokončeném cyklu včetně cyklu s nulovou prací a selhání při výjimce. Endpoint `/health/workers/csob-reconciliation` vrací:

- `Disabled`, pokud reconciliation není zapnutá;
- `NotStarted`, dokud neproběhl první úspěšný cyklus;
- `Healthy` po nedávném úspěšném cyklu;
- `Failed`, pokud poslední cyklus skončil výjimkou;
- `Stale`, pokud od posledního úspěchu uplynuly více než tři poll intervaly.

`Disabled` a `Healthy` vracejí HTTP 200, ostatní stavy HTTP 503. Endpoint pracuje pouze s pamětí procesu a nekontaktuje databázi ani ČSOB. Semantika `/health/live` a `/health/ready` se nezměnila. Testy pokrývají všechny stavy, zotavení po chybě, stale detekci, pozitivní idle cyklus, DI registraci a disabled endpoint bez externí integrace.

Status: implementováno bez persistence tabulky a bez nové závislosti.

## Finanční stav po F-01

Vytvoření blocking direct payment, kreditní úhrada a storno stejné zakázky používají stejné pořadí: transakce, row lock zakázky, opětovné ověření zakázky a kontrola blocking payment. Pokud direct payment získá lock první, uloží blocking stav a kredit/storno jej po uvolnění locku odmítnou. Pokud kredit nebo storno získá lock první, direct cesta po uvolnění znovu načte nový stav a nevytvoří použitelnou konfliktní platbu. Tvrzení je omezeno na implementované PostgreSQL transakční cesty a přidané race scénáře.

## Recovery po F-02/F-03

Scheduling count a scheduling audit nyní reprezentují pouze řádky vrácené atomickou durable změnou. Ztracený lease se naopak vykazuje samostatně a nezvyšuje žádný úspěšný transition counter.

## Access audit po F-04

Skutečně nový JIT provisioning vytváří jednu událost `access.user-provisioned` ve stejném `SaveChanges` jako user, identity a `Customer` role. Reload existující identity ani loser souběžného insertu nový durable provisioning audit nevytvoří.

## Operations po F-05

Operátor může z endpointu `/health/workers/csob-reconciliation` zjistit poslední úspěšný cyklus, poslední selhání, aktuální stav a stale hranici. Signál funguje i pro idle cyklus, nezapisuje do databáze a nevytváří periodický informační log bez práce.

## Migrace a závislosti

Nevznikla žádná EF migrace, nová tabulka ani nová balíčková závislost.

## Verification

### PASS

- `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/verify.ps1`
  - locked restore: PASS;
  - Release build: PASS, 0 warningů a 0 chyb;
  - `dotnet format --verify-no-changes`: PASS;
  - `FuaPay.Web.Tests`: 552/552 PASS, 0 skipped;
  - EF pending-model check: PASS, model odpovídá poslední migraci.
- Cílené F-01 testy: 36/36 PASS; `FuaPay.DatabaseTests` Release build PASS.
- Cílené F-02 testy recovery scheduleru/processoru: 8/8 PASS; DB test project build PASS.
- Cílené F-03 processor testy: 8/8 PASS.
- Cílené F-04 access/development testy: 13/13 PASS; DB test project build PASS.
- Cílené F-05 health, hosting a registration testy: 5/5 PASS.
- `dotnet package list --project FuaPay.slnx --vulnerable --include-transitive --no-restore --format json --output-version 1`: PASS, 0 známých zranitelností.
- Locked restore pro `linux-x64`: PASS.
- Self-contained Release publish pro `linux-x64` s `TreatWarningsAsErrors=true`: PASS; ověřeny `FuaPay.Web`, `.dll`, `.deps.json` a `.runtimeconfig.json`.
- `git diff --check`: PASS.

Přímé spuštění `scripts/verify.ps1` bez bypassu bylo zastaveno lokální PowerShell execution policy ještě před prvním krokem. Jednorázové spuštění stejného skriptu s `-ExecutionPolicy Bypass` prošlo; systémová policy nebyla změněna.

### NOT RUN

- PostgreSQL integrační test suite. Safety guard správně odmítl nakonfigurovanou vývojovou databázi. Pokus vytvořit oddělenou `fuapay_test_hardening` a následně `fuapay_test` selhal na PostgreSQL `42501: permission denied to create database`, protože lokální aplikační role nemá `CREATEDB`. Žádný DB test není vykázán jako PASS; musí jej provést CI nad `fuapay_test_ci`.
- Live Microsoft Entra a live ČSOB sandbox/production testy; potřebné externí konfigurace a credentials nebyly součástí tohoto milestone.

### FAIL

- Žádný provedený build, unit/web test, formátovací check, EF model check, vulnerability audit ani Linux publish nezůstal neúspěšný.

## Co tento milestone neřeší

- live Microsoft Entra konfiguraci;
- live ČSOB konfiguraci;
- FUA Print API;
- placený tisk a print reservations/capture/release;
- Print identity mapping;
- PDF formální účetní doklady;
- produkční ani staging deployment.

## Další milestone

FUA Print, print identity, live Entra a live ČSOB integrace jsou samostatná následná práce nad novým čistým `main`. Tento dokument nepotvrzuje nasazení hardening větve; staging zůstává na dříve evidované revizi.
