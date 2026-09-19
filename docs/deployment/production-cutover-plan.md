# Čistý produkční cutover FUA Pay

Status: 2026-09-18

Tento dokument je souhrnný plán přechodu z dnešního vývojového/demo provozu na
čistý produkční FUA Pay. Ne nahrazuje technickou
[`production-configuration.md`](production-configuration.md), ČSOB checklist
ani integrační dokumentaci; spojuje jejich aktuální rozhodnutí do jednoho
cutover plánu.

Cíl je jednoduchý: na `https://fuapay.tul.cz` poběží jedna čistá produkční
instance FUA Pay bez demo/test dat, bez vývojových funkcí a pouze s
produkčními integračními credentials.

## 1. Základní rozhodnutí

- Produkce bude mít jeden VM/server a jednu aktivní `fuapay.service`.
- Nebude existovat druhý trvale běžící staging server ani paralelní produkční
  instance.
- Současné demo/staging prostředí je přechodné vývojové prostředí. Jeho
  databáze, uživatelé, kredit, zakázky, platební pokusy, auditní historie,
  tiskové rezervace, tiskové PINy a demo finanční doklady nejsou automaticky
  produkční data.
- Před otevřením produkce se současná demo databáze archivuje a produkce začne
  nad novou čistou PostgreSQL databází vytvořenou z kanonického migration chainu.
- Demo/test data se do čisté produkce nekopírují, pokud pro konkrétní záznam
  nebude předem schválen samostatný migrační postup.
- Zdrojový kód může zůstat veřejný. Bezpečnost nesmí záviset na utajení repa;
  hesla, klíče, tokeny a pepper zůstávají výhradně mimo Git a release artefakt.
- Produkční databáze je finanční a auditní autorita. Žádná integrační služba
  nesmí zapisovat přímo do jejích tabulek.

## 2. Rozsah první čisté produkční verze

První produkční FUA Pay má obsahovat současný ověřený produktový rozsah:

- Microsoft Entra ID login přes TUL tenant;
- role Customer, Requester a Administrator;
- pracoviště a zakázky;
- kreditní účet a neměnnou historii pohybů;
- administrátorské ruční dobití kreditu;
- oddělené administrativní korekce kreditu;
- ČSOB karetní dobití kreditu;
- ČSOB přímou karetní úhradu zakázky;
- reconciliation/recovery plateb;
- plnou CardJob reverse cestu, která je již implementovaná;
- kanonické `FinancialDocuments v2` a PDF `Doklad o úhradě`;
- FUA Print service API pro Reserve / ResolutionRequired / Capture / Release;
- zákaznický e-mail + šestiznakový tiskový PIN;
- audit, health endpointy a provozní recovery hranice.

Legacy `Receipts` není finanční source of truth. V produkci může zůstat
vypnutý; nové formální doklady vlastní výhradně `FinancialDocuments`.

## 3. Čistá produkční databáze

Cutover nepoužije reset nebo ruční mazání finančních tabulek dnešní demo DB.
Místo toho:

1. vytvořit konzistentní archiv/backup současné demo databáze;
2. ověřit, že archiv lze identifikovat podle data a posledního nasazeného SHA;
3. vytvořit novou produkční PostgreSQL databázi a produkční databázové role;
4. aplikovat celý aktuální migration chain kanonickým deployment postupem;
5. ověřit, že není žádná pending EF model změna;
6. zkontrolovat prázdný výchozí finanční stav;
7. teprve potom provést řízený bootstrap produkčních rolí a pracovišť.

Do čisté produkce se nepřenášejí demo:

- kreditní zůstatky a pohyby;
- testovací/manual top-up historie;
- ČSOB integration platby a jejich `payId`;
- zakázky;
- tiskové rezervace;
- tiskové credentialy/PIN verifiery;
- auditní acceptance historie;
- FinancialDocuments ani jejich demo číselná řada.

Uživatelé se mohou znovu vytvořit legitimním Entra JIT loginem. Před cutoverem
je ale nutné přesně definovat a ověřit bootstrap prvního produkčního
Administratora a počátečních reálných ServiceUnits. Production nesmí používat
`DevelopmentData` ani development seeder jako bootstrap mechanismus.

## 4. Migrace legacy SafeQ kreditu

Stávající SafeQ identity a zůstatky se nebudou hromadně automaticky spojovat s
Entra účty podle jména nebo loginu. Produkční migrace použije adminem potvrzený,
idempotentní claim-on-demand proces popsaný v
[plánu migrace SafeQ kreditu](legacy-safeq-credit-migration.md).

Analyzovaný historický SafeQ report obsahuje 628 unikátních legacy účtů a je
vhodný jako párovací podklad, ale neobsahuje autoritativní aktuální zůstatek.
Finální částky proto musí přijít z nového SafeQ balance snapshotu po freeze
starého systému. Raw exporty s osobními údaji zůstávají mimo veřejný Git.

Legacy převod se bude evidovat jako existující **Administrativní korekce** s
jasným důvodem `Převod zůstatku ze SafeQ`. Nejde o nové ruční dobití ani nový
externí příjem, proto při této operaci nevzniká nový `FinancialDocument`.
Párování identity zůstává read-only a každý konkrétní SafeQ -> FUA Pay pár musí
před finančním zápisem potvrdit administrátor.

## 5. Microsoft Entra ID

Entra integrace už na `https://fuapay.tul.cz` reálně funguje a není potřeba
znovu navrhovat registraci aplikace.

Produkce musí mít:

```text
DevelopmentSignIn__Enabled=false
DevelopmentData__Enabled=false
DevelopmentData__ResetOnStart=false
StagingTestMode__Enabled=false

Entra__Enabled=true
```

Tenant ID, Client ID a Client Secret jsou deployment secrets. Identita se dál
váže výhradně podle stabilního `tid + oid`, ne podle e-mailu nebo jména.
Microsoft Graph ani directory-wide scopes FUA Pay nepotřebuje.

Před otevřením čisté produkce se znovu ověří:

- login;
- logout;
- JIT vytvoření Customer účtu;
- lokální role;
- ruční přiřazení Requester/Administrator role;
- zablokování/odebrání role bez čekání na starou session autorizaci.

## 6. ČSOB: z integration do production

Současný ověřený ČSOB provoz používá integration prostředí. Produkce nesmí
použít integration merchant, integration signing klíče ani
`https://iapi.iplatebnibrana.csob.cz/`.

Před production aktivací stále platí checklist
[`csob-production-readiness.md`](../integrations/csob-production-readiness.md).

Zbývající povinné kroky zahrnují zejména:

- bezprostředně před activation zopakovat fresh GET i POST echo;
- v POS Merchant potvrdit všechny povinné activation scénáře;
- odeslat je ke kontrole ČSOB;
- počkat na potvrzení aktivace production environment;
- nainstalovat produkční merchant konfiguraci a produkční klíče;
- přepnout na `https://api.platebnibrana.csob.cz/`;
- ověřit produkční signing/verification hranici;
- provést řízený produkční payment smoke.

FUA Pay vlastní acceptance před cutoverem musí navíc uzavřít dosud otevřené
scénáře z aktuálního checklistu, zejména decline/failed, duplicate browser
return, lost browser return, restart během pending platby, opakovaný status,
opuštěnou pending platbu a desktop/mobile smoke.

Browser nikdy není finanční autorita. Finanční efekt může vzniknout pouze po
autoritativním serverovém ověření.

## 7. Kredit a ruční operace

Produkce zachová jediný kanonický credit ledger.

- Ruční dobití kreditu je kladná provozní cesta a vytváří auditovaný
  `FinancialDocument`.
- Administrativní korekce je samostatná opravná operace `+/-` a finanční
  dokument nevytváří.
- Výdej kreditu za zakázku nebo tisk nevytváří nový doklad o příjmu peněz.
- Kredit nesmí být resetován ruční manipulací s ledgerem.

Před go-live musí projít alespoň jeden řízený test role/validace této cesty nad
čistou produkční konfigurací bez používání demo seederu.

## 8. FinancialDocuments v2

Produkční doklad je neměnný databázový snapshot. PDF je pouze opakovatelný
render a není zdrojem pravdy.

Platí:

- nový doklad vzniká pouze při skutečném příjmu peněz:
  - ruční dobití kreditu;
  - úspěšné karetní dobití;
  - úspěšná přímá karetní platba zakázky;
- administrativní korekce ani utracení dříve nabitého kreditu doklad nevytváří;
- PDF se generuje on-demand;
- PDF se neukládá do `wwwroot` ani na server jako autoritativní soubor;
- response je private/no-store;
- schválený issuer a 21% daňový snapshot jsou součástí immutable dokumentu;
- provider IDs, interní IDs a jiné technické reference se zákazníkovi v PDF
  nezobrazují;
- produkční číselná řada je `FUA-YYYY-NNNNNN`.

Protože čistá produkce začíná novou DB, demo finanční dokumenty ani jejich
číselná řada se nepřenášejí. Před prvním produkčním finančním příjmem musí být
explicitně potvrzeno, že produkční řada startuje v nové produkční databázi a
demo čísla nejsou účetní produkční historie.

Linux PDF runtime musí mít schválené regular/bold fonty mimo release artefakt.

## 9. FUA Print integrace

Cílová produkce FUA Pay má mít aktivní:

```text
PrintPayments__Enabled=true
PrintCredentials__Enabled=true
```

a produkční:

- stabilní `PrintSourceId`;
- service bearer credential, jehož SHA-256 je v FUA Pay konfiguraci;
- tajný `PrintCredentials__PepperBase64` o alespoň 32 náhodných bytech.

Pepper a service credential nesmějí být v Git/release ani v logu. Pepper musí
po go-live zůstat stabilní; jeho ztráta zneplatní uložené PIN verifiery.

Čistá produkce nepřenese dnešní demo tiskové PINy. Uživatel si po prvním
produkčním přihlášení nastaví vlastní nový šestiznakový kód.

Před zapnutím produkční tiskové cesty se dokončí cross-repo FUA Pay ↔ FUA Print
audit a acceptance:

- e-mail + PIN autentizace;
- Reserve;
- insufficient-credit chování;
- CUPS job zůstane held před autorizovaným release;
- Capture po skutečném tisku;
- Release bez finančního debetu, pokud tisk neproběhne;
- lost/ambiguous response recovery;
- restart/retry/idempotence;
- duplicate release/capture ochrana;
- vazba na správný `jobUuid` a `PrintSourceId`.

Dne 2026-09-18 byl v současném prostředí poprvé reálně pozorován dokončený tisk
s Capture a správným odečtením 18 Kč z kreditu. Jde o důležitý integrační důkaz,
ale ne o náhradu finálního produkčního gate.

Customer UI nyní zobrazuje zachycený tiskový debit lidským popisem
`Úhrada tisku` místo technického textu typu
`Capture print reservation <GUID>`. Rozpoznání je úmyslně omezené na Debit,
přesný existující prefix a platný reservation GUID; uložený ledger description
se nemění a interní reservation/job/operation identifikátory zůstávají pro
audit a diagnostiku. Ověření je zdokumentováno v
[print-credit movement verification](../testing/print-credit-movement-description-verification-2026-09-19.md).

## 10. Produkční runtime a secrets

Production startuje fail-closed. Minimální stav:

```text
ASPNETCORE_ENVIRONMENT=Production
AllowedHosts=fuapay.tul.cz
Database__ApplyMigrationsOnStart=false
DevelopmentSignIn__Enabled=false
DevelopmentData__Enabled=false
StagingTestMode__Enabled=false
Entra__Enabled=true
Payments__Provider=Csob
Csob__Enabled=true
```

Mimo release musí zůstat minimálně:

- PostgreSQL connection string / hesla;
- Entra Client Secret;
- ČSOB merchant private key;
- ČSOB gateway public key;
- FUA Print service credential/hash konfigurace;
- PrintCredentials pepper;
- Data Protection key ring;
- PDF fonty.

Data Protection key ring musí být po produkčním go-live persistentní přes
všechny další release. Jeho ztráta nebo nahrazení zneplatní sessions a
antiforgery cookies.

## 11. Nginx, HTTPS a HARICA

Kanonicý veřejný endpoint zůstává `https://fuapay.tul.cz`.

Cílová topologie je:

```text
Internet
  -> Nginx :443
  -> loopback Kestrel / jedna fuapay.service
  -> PostgreSQL
```

Produkční cutover nemá zavádět druhý proxy stack ani druhý aplikační server.
Zachová se již ověřený HARICA/Certbot renewal model a před go-live se ověří, že:

- HTTPS certifikát je platný pro produkční hostname;
- HTTP přesměruje na HTTPS;
- HSTS zůstává aktivní;
- forwarded headers věří pouze skutečné lokální proxy;
- certifikátový renewal a Nginx reload jsou funkční.

## 12. Release, migrace a rollback

Každý produkční release:

1. vychází z přesného čistého `main` SHA;
2. projde CI, CodeQL, `scripts/verify.ps1` a relevantními PostgreSQL testy;
3. projde NuGet vulnerability auditem a locked restore/publish gate;
4. vytvoří jeden deterministický self-contained `linux-x64` artefakt;
5. má ověřený SHA-256 a archivní Unix metadata;
6. má samostatně zreviewovaný a hashově ověřený migration SQL artefakt;
7. se instaluje side-by-side do `/opt/fuapay/releases/<SHA>`;
8. před aktivací ověří konfiguraci, práva, backup a migration stav;
9. atomicky přepne `/opt/fuapay/current`;
10. restartuje jedinou `fuapay.service`;
11. projde bounded health a smoke gatem.

Automatické migrace při startu jsou v Production vypnuté. Databázové migrace
jsou forward-only. Code rollback smí vrátit symlink na předchozí kompatibilní
release, ale nesmí automaticky spouštět reverse migration.

## 13. Produkční backup a obnova

Před čistým cutoverem:

- archivovat poslední demo DB;
- vytvořit/ověřit produkční backup postup;
- ověřit restore pouze do izolované databáze;
- nikdy netestovat restore nad živou produkcí.

Před každou budoucí schema změnou musí existovat konzistentní backup a známý
restore postup. TUL provozovatel musí mimo repo určit produkční RPO/RTO,
retenci, šifrování a přístupová oprávnění.

## 14. Cutover pořadí

Doporučený finální sled je:

1. dokončit drobné UX nálezy a cross-repo FUA Print audit;
2. uzavřít zbývající ČSOB integration acceptance;
3. vybrat přesný release candidate SHA;
4. full repository/DB/security gate;
5. vytvořit release a migration artefakty;
6. archivovat současnou demo DB;
7. založit čistou produkční DB a aplikovat celý migration chain;
8. provést bezpečný bootstrap prvního Administratora a reálných ServiceUnits;
9. zmrazit starý SafeQ provoz a vytvořit finální autoritativní balance snapshot;
10. připravit první adminem potvrzené SafeQ -> FUA Pay páry pro legacy převod;
11. nainstalovat produkční secrets a production environment konfiguraci;
12. aktivovat Entra a ověřit login/logout/role;
13. po schválení ČSOB nainstalovat production merchant konfiguraci a provést
    production payment smoke;
14. po finálním FUA Print gate aktivovat PrintPayments + PrintCredentials;
15. atomicky aktivovat release;
16. ověřit `/health/live`, `/health/ready`, worker health a veřejné HTTPS;
17. provést pouze řízené produkční smoke scénáře;
18. otevřít systém uživatelům;
19. ponechat bezprostředně předchozí kompatibilní release jako code rollback.

Žádná demo data se při tomto pořadí „nečistí“ in-place a žádný finanční ledger
se ručně neresetuje.

## 15. Go-live acceptance

Před označením systému jako Production musí být prokázáno minimálně:

- čistá DB, očekávaný migration stav;
- žádný Development/Staging feature aktivní;
- Entra login/logout + role PASS;
- Customer izolace dat PASS;
- Requester scope PASS;
- Administrator scope/audit PASS;
- ruční dobití a korekce mají správně odlišnou finanční semantiku;
- FinancialDocument vzniká pouze tam, kde má;
- PDF doklad se opakovaným stažením nemění a nevytváří nový dokument;
- ČSOB production signing/echo PASS;
- řízená production platba PASS;
- reconciliation worker Healthy;
- FUA Print Reserve/Capture/Release E2E PASS;
- tisk neodečte kredit dvakrát;
- neprovedený tisk kredit nestrhne;
- kreditní historie používá lidské popisy bez interních GUID;
- desktop a mobilní smoke hlavních Customer toků PASS;
- backup/restore postup je známý a poslední backup identifikovatelný;
- běžící executable odpovídá přesnému schválenému SHA.

## 16. Po go-live

Po spuštění se průběžně sleduje:

- `/health/live` a `/health/ready`;
- ČSOB reconciliation worker;
- opakované 503;
- payment `RequiresAttention`;
- auditní události;
- stav záloh;
- expirace/rotace Entra secretu a ČSOB klíčů;
- HARICA certifikát a renewal.

Další release se nasazují stejným side-by-side modelem. Pro běžný vývoj se
nezakládá permanentní staging server; CI, izolované PostgreSQL testy a
kontrolované integrační acceptance slouží jako předprodukční gate.

## 17. Věci, které první produkční cutover záměrně neřeší

Bez nového explicitního rozhodnutí nejsou součástí prvního cutoveru:

- druhý permanentní staging server;
- kopie demo/test finanční historie do produkce;
- automatický reverse DB rollback;
- ukládání PDF dokladů na disk jako source of truth;
- Microsoft Graph nebo directory-wide oprávnění;
- ukládání údajů platební karty ve FUA Pay;
- částečné refundy;
- nový in-app refund workflow nad rámec již implementované CardJob reverse
  cesty;
- přímé DB propojení FUA Print -> FUA Pay;
- spoléhání na utajení veřejného repozitáře.

## 18. Konkrétní otevřené položky k uzavření

Před skutečným produkčním cutoverem zůstává explicitně:

- [x] opravit zákaznický popis tiskového debit pohybu na lidský text — ověřeno 2026-09-19; merge této změny a následný deployment/smoke jsou stále součástí release procesu;
- [ ] dokončit FUA Print ↔ FUA Pay cross-repo audit;
- [ ] dokončit finální FUA Print E2E acceptance;
- [ ] definovat a ověřit bootstrap prvního produkčního Administratora;
- [ ] připravit finální SafeQ balance export po freeze starého systému;
- [ ] dokončit read-only SafeQ -> FUA Pay matching/report nástroj a provozní postup ručně potvrzených administrativních korekcí;
- [ ] definovat počáteční produkční ServiceUnits/role assignment;
- [ ] uzavřít otevřené vlastní ČSOB acceptance scénáře;
- [ ] fresh ČSOB GET/POST echo těsně před activation;
- [ ] potvrdit activation scénáře v POS Merchant a získat schválení ČSOB;
- [ ] nainstalovat production ČSOB merchant/key konfiguraci;
- [ ] potvrdit produkční FinancialDocument číselnou řadu nad čistou DB;
- [ ] připravit a ověřit čistou produkční PostgreSQL DB + backup/restore;
- [ ] ověřit finální production environment/secrets a filesystem permissions;
- [ ] ověřit Nginx/TLS/HARICA stav;
- [ ] final release candidate full gate;
- [ ] kontrolovaný go-live smoke a provozní monitoring.

Tento seznam se má zkracovat pouze na základě konkrétního ověření/evidence,
nikoli odhadem.
