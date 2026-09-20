# Čistý produkční cutover FUA Pay

Status: 2026-09-20

Tento dokument je souhrnný cutover plán a následný closeout prvního čistého
produkčního nasazení FUA Pay. Ne nahrazuje technickou
[`production-configuration.md`](production-configuration.md), ČSOB checklist
ani integrační dokumentaci; spojuje jejich aktuální rozhodnutí do jednoho
cutover plánu.

Produkční baseline byl 2026-09-20 přímo ověřen na
`https://fuapay.tul.cz` nad přesným release
`774b324c48d8f874db21f115479f3b317c2a73d0`. Production používá čistou
databázi bez demo/test finančních dat, bez staging funkcí a pouze s credentials
pro skutečně aktivované integrace. Aktuální runtime evidence je v
[`runtime-state-2026-09-20.md`](runtime-state-2026-09-20.md).

## 1. Základní rozhodnutí

- Production běží na jednom VM/serveru jako aktivní `fuapay.service`.
- Na stejném VM je od 2026-09-20 trvale připravený, ale standardně zastavený
  a disabled izolovaný `fuapay-staging.service`.
- Staging není paralelní Production: má samostatný OS účet, release root,
  environment file, PostgreSQL databázi a role, Data Protection keyring i
  staging integration secrets.
- Historická demo databáze není produkční data. Kredit, zakázky, platební
  pokusy, auditní historie, tiskové rezervace, tiskové PINy a demo finanční
  doklady nebyly přeneseny do Production.
- Historický demo dataset byl po backupu kontrolovaně importován pouze do
  izolované `fuapay_staging` jako acceptance/test dataset.
- Demo/test data se do čisté Production nekopírují, pokud pro konkrétní záznam
  nebude předem schválen samostatný migrační postup.
- Zdrojový kód může zůstat veřejný. Bezpečnost nesmí záviset na utajení repa;
  hesla, klíče, tokeny a pepper zůstávají výhradně mimo Git a release artefakt.
- Produkční databáze je finanční a auditní autorita. Žádná integrační služba
  nesmí zapisovat přímo do jejích tabulek.
- Release workflow cílí na build once -> staging acceptance -> promotion
  stejného binárního artefaktu do Production bez rebuildování.

## 2. Rozsah první čisté produkční verze

První produkční FUA Pay má obsahovat současný ověřený produktový rozsah:

- Microsoft Entra ID login přes TUL tenant;
- role Customer, Requester a Administrator;
- pracoviště a zakázky;
- kreditní účet a neměnnou historii pohybů;
- administrátorské ruční dobití kreditu;
- oddělené administrativní korekce kreditu;
- kanonické `FinancialDocuments v2` a PDF `Doklad o úhradě`;
- úhradu publikovaných zakázek z existujícího FUA Pay kreditu;
- FUA Print service API pro Reserve / ResolutionRequired / Capture / Release a
  zákaznický e-mail + šestiznakový tiskový PIN až po jeho samostatném produkčním
  acceptance/configuration gate;
- dedikovaný `LegacySafeQCreditTransfer`, jakmile bude připraven jeho samostatný
  operátorský acceptance;
- audit, health endpointy a provozní recovery hranice.

První go-live je výslovně zamýšlen s
`ASPNETCORE_ENVIRONMENT=Production`, `Payments__Provider=None` a
`Csob__Enabled=false`. Produkce tedy může být otevřena bez vytváření nových
karetních plateb. ČSOB není blocker tohoto prvního milníku.

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

Legacy převod používá dedikovanou operaci `LegacySafeQCreditTransfer`, nikoli
ruční dobití ani obecnou administrativní korekci. Vytvoří canonical credit
movement, durable transfer record s unikátním SafeQ user ID a audit, ale žádný
`Payment` ani `FinancialDocument`. Zákaznický popis je přesně
`Převod kreditu ze SafeQ`.

Čistou produkci lze otevřít dříve, než budou vytvořeny účty všech studentů.
Interní Customer identity vzniká legitimním Entra JIT loginem. Před finálním
SafeQ freeze smí matching probíhat jen read-only; po vytvoření jediného finálního
immutable snapshotu a zaznamenání jeho SHA-256 lze lidsky potvrzené kladné
převody provádět postupně během více dnů. Všechny převody tohoto cutoveru používají
tentýž snapshot a každý SafeQ user ID lze finančně převést jen jednou za celý
život. Nulové a záporné zůstatky se řeší podle samostatné migrační politiky.

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

## 6. ČSOB: samostatný pozdější produkční milník

Je nutné rozlišovat dva nezávislé milníky:

1. FUA Pay production go-live s vypnutým vytvářením karetních plateb
   (`Payments__Provider=None`, `Csob__Enabled=false`).
2. Pozdější aktivaci produkčního ČSOB provozu po jeho vlastním bankovním,
   konfiguračním, bezpečnostním, payment a reconciliation acceptance gate.

První milník na dokončení druhého nečeká. Přepnutí na ČSOB je runtime/configuration
změna nad existujícím finančním schématem, nikoli databázová migrace.

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

FUA Pay vlastní acceptance před aktivací produkčního ČSOB musí navíc uzavřít dosud otevřené
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

## 9. Pozdější aktivace FUA Print

První produkční go-live FUA Pay probíhá s tiskovou integrací vypnutou:

```text
PrintPayments__Enabled=false
PrintCredentials__Enabled=false
```

FUA Print není podmínkou prvního produkčního cutoveru. Aktivuje se jako
samostatný pozdější provozní milník. Při jeho aktivaci se oba přepínače zapnou
společně a doplní se produkční:

- stabilní `PrintSourceId`;
- service bearer credential, jehož SHA-256 je v FUA Pay konfiguraci;
- tajný `PrintCredentials__PepperBase64` o alespoň 32 náhodných bytech.

Pepper a service credential nesmějí být v Git/release ani v logu. Pepper musí
po aktivaci FUA Print zůstat stabilní; jeho ztráta zneplatní uložené PIN
verifiery.

Čistá produkce nepřenese dnešní demo tiskové PINy. Po pozdější aktivaci FUA
Print si uživatel nastaví vlastní nový šestiznakový kód.

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
DevelopmentData__ResetOnStart=false
StagingTestMode__Enabled=false
Entra__Enabled=true
Payments__Provider=None
Csob__Enabled=false
```

V tomto prvním produkčním profilu nejsou potřeba ČSOB merchant ID, klíče, API
ani return URL; ČSOB return processing a reconciliation worker neběží a CSP
nepovoluje externí payment `form-action` origin.

Mimo release musí pro první produkční go-live zůstat minimálně:

- PostgreSQL connection string / hesla;
- Entra Client Secret;
- Data Protection key ring;
- PDF fonty.

FUA Print service credential/hash konfigurace a `PrintCredentials` pepper se
doplní až při samostatné pozdější aktivaci FUA Print.

ČSOB merchant private key a gateway public key se do produkčního secret store
doplní až pro samostatnou pozdější aktivaci ČSOB.

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

1. dokončit drobné UX nálezy;
2. vybrat přesný release candidate SHA;
3. provést full repository/DB/security gate;
4. vytvořit release a migration artefakty;
5. archivovat současnou demo DB;
6. založit čistou produkční DB a aplikovat celý migration chain;
7. provést bezpečný bootstrap prvního Administratora a reálných ServiceUnits;
8. nainstalovat produkční secrets a profil `Payments__Provider=None`,
   `Csob__Enabled=false`;
9. aktivovat Entra a ověřit login/logout/role;
10. ponechat `PrintPayments__Enabled=false` a
    `PrintCredentials__Enabled=false` pro první produkční go-live;
11. atomicky aktivovat release;
12. ověřit `/health/live`, `/health/ready` a veřejné HTTPS;
13. provést pouze řízené produkční smoke scénáře;
14. otevřít systém uživatelům a umožnit Entra JIT vznik Customer identit;
15. ponechat bezprostředně předchozí kompatibilní release jako code rollback;
16. před finálním SafeQ freeze připravovat pouze read-only matching;
17. zmrazit SafeQ, vytvořit finální immutable balance snapshot, zaznamenat jeho
    SHA-256 a postupně provádět lidsky potvrzené `LegacySafeQCreditTransfer`;
18. nezávisle dokončit ČSOB acceptance; teprve potom nainstalovat produkční
    merchant konfiguraci, přepnout provider a provést production payment smoke.

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
- `Payments__Provider=None` a `Csob__Enabled=false` startují fail-closed bez
  zákaznických karetních entry pointů, ČSOB runtime a externích CSP originů;
- ruční top-up a úhrada zakázky kreditem zůstávají na dostupnosti karet
  nezávislé;
- `PrintPayments__Enabled=false` a `PrintCredentials__Enabled=false` zůstávají
  pro první produkční go-live vypnuté;
- kreditní historie používá lidské popisy bez interních GUID;
- desktop a mobilní smoke hlavních Customer toků PASS;
- backup/restore postup je známý a poslední backup identifikovatelný;
- běžící executable odpovídá přesnému schválenému SHA.

## 16. Po go-live

Po spuštění se průběžně sleduje:

- `/health/live` a `/health/ready`;
- opakované 503;
- auditní události;
- stav záloh;
- expirace/rotace Entra secretu;
- HARICA certifikát a renewal.

Po pozdější aktivaci ČSOB se navíc sleduje reconciliation worker, payment
`RequiresAttention` a expirace/rotace ČSOB klíčů.

Další release se nasazují stejným side-by-side modelem. Vedle CI a izolovaných
PostgreSQL testů je od 2026-09-20 připravený oddělený staging runtime na stejném
VM. Staging je standardně zastavený a spouští se jen pro acceptance/integration
okna; nesdílí Production databázi, OS identitu, Data Protection ani integration
secrets. Aktuální hranice jsou v
[`runtime-state-2026-09-20.md`](runtime-state-2026-09-20.md).

## 17. Věci, které první produkční cutover záměrně neřeší

Bez nového explicitního rozhodnutí nejsou součástí prvního cutoveru:

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

Po vytvoření prvního production baseline zůstává explicitně:

- [x] opravit zákaznický popis tiskového debit pohybu na lidský text — merge,
      staging deployment i Customer smoke ověřeny 2026-09-19;
- [x] připravit čistou produkční PostgreSQL DB a aplikovat aktuální migration
      chain; Production má 25 migrací a demo finanční data nebyla přenesena;
- [x] definovat a ověřit bootstrap prvního produkčního Administratora;
- [x] ověřit production environment/secrets a filesystem permissions;
- [x] ověřit Production Nginx/TLS/HARICA stav včetně ACME webroot a renewal
      modelu;
- [x] připravit izolovaný staging runtime a lokální acceptance nad stejným
      release SHA;
- [ ] založit počáteční reálné produkční ServiceUnits a dokončit požadované
      Requester assignmenty;
- [ ] potvrdit produkční FinancialDocument číselnou řadu nad čistou DB;
- [ ] dokončit a doložit production backup/restore acceptance;
- [x] dokončit izolovaný staging HTTPS/Nginx edge a browser acceptance:
      `https://fuapay.fa.tul.cz:8443`, bez DNS změny, s existujícím SAN
      certifikátem a UFW přístupem omezeným na explicitní klientskou IPv4;
- [ ] final release/business-flow full gate včetně odloženého mobile smoke;
- [ ] dokončit kontrolovaný go-live smoke a provozní monitoring.

Samostatně před pozdější aktivací FUA Print zůstává:

- [ ] dokončit FUA Print ↔ FUA Pay cross-repo audit;
- [ ] dokončit finální FUA Print E2E acceptance;
- [ ] doplnit produkční PrintSource/service credential/hash konfiguraci a
  `PrintCredentials` pepper;
- [ ] aktivovat společně `PrintPayments__Enabled=true` a
  `PrintCredentials__Enabled=true` a provést kontrolovaný FUA Print smoke.

Samostatně před prvním skutečným SafeQ převodem zůstává:

- [ ] provést finální SafeQ freeze a připravit immutable balance snapshot včetně
  SHA-256;
- [ ] definovat operátorský postup pro explicitně potvrzené
  `LegacySafeQCreditTransfer`; případný CLI nástroj musí volat aplikační službu a
  není dosud implementovaný;
- [ ] potvrdit, že matching sám nevytváří finanční efekt a každý skutečný pár
  schvaluje člověk.

Samostatně před pozdější aktivací produkčního ČSOB zůstává:

- [ ] uzavřít otevřené vlastní ČSOB acceptance scénáře;
- [ ] fresh ČSOB GET/POST echo těsně před activation;
- [ ] potvrdit activation scénáře v POS Merchant a získat schválení ČSOB;
- [ ] nainstalovat production ČSOB merchant/key konfiguraci;
- [ ] ověřit produkční signing/verification, payment smoke a reconciliation.

Tento seznam se má zkracovat pouze na základě konkrétního ověření/evidence,
nikoli odhadem.
