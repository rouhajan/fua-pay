# Runtime checkpoint 2026-09-20

Acceptance update: 2026-09-22

Tento dokument zachycuje přímo ověřený stav Production a nového izolovaného
staging runtime po prvním produkčním nasazení FUA Pay. Je to provozní checkpoint,
nikoli náhrada release-artifact, cutover ani integračních runbooků.

Source of truth pro tento checkpoint je skutečný runtime na serveru a přesný
release commit `774b324c48d8f874db21f115479f3b317c2a73d0`.

## 1. Production

Ověřený stav:

- canonical URL: `https://fuapay.tul.cz`;
- `https://fuapay.fa.tul.cz` je pouze production alias a přesměrovává na
  canonical URL;
- systemd: `fuapay.service`;
- OS účet: `fuapay:fuapay`;
- release:
  `/opt/fuapay/releases/774b324c48d8f874db21f115479f3b317c2a73d0`;
- `/opt/fuapay/current` ukazuje na tento přesný release;
- Kestrel: `127.0.0.1:5080`;
- environment: `Production`;
- konfigurace: `/etc/fuapay/production.env`;
- databáze: `fuapay`;
- runtime DB role: `fuapay_app`;
- owner/migrator role: `fuapay_migrator`;
- Data Protection:
  `/var/lib/fuapay/data-protection-production`;
- `Database__ApplyMigrationsOnStart=false`;
- Microsoft Entra ID: zapnuto;
- `Payments__Provider=None`;
- `Csob__Enabled=false`;
- `PrintPayments__Enabled=false`;
- `PrintCredentials__Enabled=false`;
- `Receipts__Enabled=false`;
- `/health/ready`: Healthy.

Production databáze byla založena jako čistá produkční databáze a má 25 EF
migrací, poslední
`20260919112723_AddLegacySafeQCreditTransfers`. Historická demo finanční data
nebyla do Production přenesena.

První reálný uživatel vznikl legitimním Entra JIT loginem. Bootstrap
Administratora byl proveden řízeně a následné přidání role Requester proběhlo
přes Admin UI. JIT login sám neumí povýšit uživatele na Requester ani Admin.

K 2026-09-20 nejsou v Production ještě založena reálná ServiceUnits. To je
očekávaný čistý business stav, nikoli chyba runtime. Bez aktivních ServiceUnits
nemá Requester view co přiřadit a nenabízí vytvoření nové zakázky. Admin view
po založení aktivních ServiceUnits získá správu všech aktivních pracovišť;
Requester view je omezen na explicitně přiřazená pracoviště.

Karetní brána je v Production záměrně vypnutá. To nevypíná zakázky ani lokální
kreditní settlement: publikovanou zakázku lze uhradit existujícím FUA Pay
kreditem. Zákaznické karetní dobití a přímá karetní platba se při provideru
`None` nenabízejí.

## 2. Cílový provozní model po rozhodnutí 2026-09-20

Cílový model je jeden VM se dvěma oddělenými runtime hranicemi:

1. Production je aktivní dlouhodobě.
2. Staging je dlouhodobě připravený, ale standardně `disabled` a `inactive`.
   Spouští se pouze pro acceptance a integrační testy.

Staging není druhý fyzický server a nesdílí s Production:

- OS účet;
- aplikační release adresář;
- environment file;
- PostgreSQL databázi;
- PostgreSQL runtime ani migrator roli;
- Data Protection keyring;
- staging integration secret files.

Stejný release artefakt lze nejprve přijmout na stagingu a po acceptance
promovat do Production bez rebuildování binárního release.

## 3. Izolovaný staging runtime

Ověřený stav:

- systemd: `fuapay-staging.service`;
- služba je standardně `disabled` a `inactive`;
- OS účet: `fuapay-staging:fuapay-staging`;
- release root: `/opt/fuapay-staging/releases/<SHA>`;
- current symlink: `/opt/fuapay-staging/current`;
- aktuálně připravený release:
  `774b324c48d8f874db21f115479f3b317c2a73d0`;
- executable je byte-identický s Production executable;
- Kestrel: pouze `127.0.0.1:5081`;
- environment: `Staging`;
- konfigurace: `/etc/fuapay-staging/staging.env`;
- databáze: `fuapay_staging`;
- runtime DB role: `fuapay_staging_app`;
- owner/migrator role: `fuapay_staging_migrator`;
- Data Protection:
  `/var/lib/fuapay-staging/data-protection`;
- integration secret root:
  `/var/lib/fuapay-staging/secrets`;
- `Database__ApplyMigrationsOnStart=false`.

Filesystem, peer-auth a database access byly negativně ověřeny oběma směry:
Production OS/runtime identita nemá přístup ke staging secretům, release ani DB
roli a staging identita nemá přístup k production env, release ani production
DB roli.

Runtime ACL jsou explicitní, nikoli založené na default privileges. Staging
runtime má stejné explicitní aplikační grants jako Production; migration history
není runtime roli čitelná. Nová
`credits.legacy_safeq_credit_transfers` má záměrně pouze potřebná práva.

## 4. Staging autentizace

Nový staging je záměrně nezávislý na TUL Entra App Registration:

```text
StagingTestMode__Enabled=true
StagingTestMode__InteractiveSignInEnabled=true
StagingTestMode__SeedDataEnabled=false
StagingTestMode__ResetDataOnStart=false
StagingTestMode__SimulatedPaymentsEnabled=false
Entra__Enabled=false
```

Entra tenant/client/secret material bylo z nového staging env odstraněno.
Aktivace interactive staging signin zpřístupní pouze pevně definované testovací
identity přes `/Development/SignIn`. Entra OpenID Connect handler se při
`Entra__Enabled=false` vůbec neregistruje.

Tento model proto pro nový staging nevyžaduje doplnění staging redirect URI v
TUL Entra tenant administraci.

## 5. Staging payment a další integrační baseline

Aktuální bezpečný staging baseline:

```text
Payments__Provider=None
Csob__Enabled=false
StagingTestMode__SimulatedPaymentsEnabled=false
PrintPayments__Enabled=false
PrintCredentials__Enabled=false
Receipts__Enabled=true
Receipts__PreviewMode=true
```

ČSOB integration key files jsou v odděleném staging secret adresáři připravené,
ale provider ani gateway nejsou v tomto checkpointu aktivní. Produkční ČSOB
traffic zůstává vypnutý.

## 6. Historický demo dataset v novém stagingu

Původní `fuapay_demo` zůstává historickým zdrojem/evidence. Před importem byly
vytvořeny tři root-only PostgreSQL custom-format artefakty:

- úplný backup `fuapay_demo`;
- rollback backup čistého `fuapay_staging`;
- data-only transport dump aplikačních schémat bez
  `app.__ef_migrations_history`, ownerů a ACL.

Transport dump byl ověřen SHA-256
`192f88c892c374bba67e791911fdad9e1ef90f5051f08d23db89b76bbae4b8c4`.

Historický dataset měl 24 migrací. Nový staging měl před importem čisté schéma
s 25 migracemi. Migrace 25 pouze přidává
`credits.legacy_safeq_credit_transfers`, takže historická data byla vložena do
již připraveného 25-migračního schématu bez přenosu migration history, ownerů a
ACL.

Import proběhl atomicky v jedné PostgreSQL transakci. Ověřeno:

- 24 historických aplikačních tabulek bylo přeneseno;
- 23 tabulek po jediné záměrné identity úpravě mají přesnou logickou paritu se
  zdrojem;
- `credits.movements_id_seq` zachovala přesný stav;
- staging zůstal na 25 migracích;
- `credits.legacy_safeq_credit_transfers` zůstala prázdná;
- runtime ACL a ownership zůstaly beze změny;
- hlavní počty po importu:
  11 users, 256 audit events, 3 credit accounts, 14 credit movements,
  16 jobs, 19 payments, 3 financial documents, 18 notification outbox rows,
  4 service units.

Historický demo dataset obsahoval jednu legacy vazbu, kde testovací
`development/fua-pay-local/administrator` identita ukazovala na reálný
Entra-linked staging user record. Tato jediná vazba byla při importu do nového
stagingu záměrně odstraněna. Historický Entra-linked user record a jeho role/data
zůstaly zachované. První budoucí přihlášení statickým profilem Administrator
proto vytvoří samostatného testovacího uživatele místo přepisování profilu
historického Entra-linked uživatele.

Zdrojová `fuapay_demo` nebyla tímto importem změněna.

## 7. Lokální staging acceptance

Nad importovaným datasetem bylo ověřeno:

- staging startuje jako `fuapay-staging:fuapay-staging`;
- executable běží z přesného release
  `774b324c48d8f874db21f115479f3b317c2a73d0`;
- listener je pouze `127.0.0.1:5081`;
- `/health/live`: Healthy;
- `/health/ready`: Healthy;
- ČSOB reconciliation health: Disabled;
- `/Development/SignIn`: HTTP 200;
- vykresleno všech 9 statických testovacích profilů;
- runtime DB role umí číst historická aplikační data;
- startup ani anonymní načtení signin UI nezměnily dataset;
- journal neobsahoval application error signature;
- jediný očekávaný warning je explicitní warning aktivního Staging test mode;
- Production zůstala při souběžném běhu stagingu Healthy;
- po acceptance byl staging znovu zastaven a zůstal disabled.

Výsledek:

`IMPORTED STAGING LOCAL ACCEPTANCE: PASS`.

## 8. Ověřený staging HTTPS edge

K 2026-09-20 je staging dostupný na odděleném HTTPS portu:

```text
https://fuapay.fa.tul.cz:8443
        -> Nginx
        -> http://127.0.0.1:5081
        -> fuapay-staging.service
```

Toto uspořádání záměrně nemění existující Production chování:

- `https://fuapay.tul.cz:443` zůstává canonical Production;
- `https://fuapay.fa.tul.cz:443` zůstává beze změny HTTP 301 ->
  `https://fuapay.tul.cz/`;
- Production Nginx site `/etc/nginx/sites-available/fuapay` měl před i po
  staging edge změně přesně SHA-256
  `491a7228c460ce5c8674a98e6c4c0d0433e0b4fd990360f29cf2d9c9280a91d9`;
- staging používá nový samostatný Nginx site
  `/etc/nginx/sites-available/fuapay-staging`;
- port `5081` zůstává pouze loopback a po zastavení staging služby neposlouchá;
- staging Nginx listener je pouze IPv4 `0.0.0.0:8443`;
- unknown Host/SNI cesta na `:8443` fail-closed končí prázdnou odpovědí
  přes default server `444`.

Staging používá existující HARICA certifikát lineage `fuapay.tul.cz`, protože
jeho SAN už před staging změnou obsahoval oba názvy:

- `DNS:fuapay.tul.cz`;
- `DNS:fuapay.fa.tul.cz`.

Nebyla provedena žádná změna DNS, žádné vydání nového certifikátu ani změna
Production ACME/renewal modelu. `fuapay.fa.tul.cz` i `fuapay.tul.cz` již
předem resolvovaly na stejný VM.

UFW nepovoluje `8443/tcp` obecně. Přístup byl pro browser acceptance povolen
jen z konkrétní IPv4 aktuálního SSH/browser klienta. Tím staging s bezheslovými
testovacími identitami není otevřený celému internetu.

Dlouhodobý hostname/cookie invariant: HTTP cookies nejsou oddělené portem.
Staging session/antiforgery cookies pro host `fuapay.fa.tul.cz` se proto mohou
odeslat i na stejný host na portu 443. Současné uspořádání je bezpečné právě
proto, že `fuapay.fa.tul.cz:443` pouze provede Nginx 301 na
`fuapay.tul.cz` a request nikdy neproxyuje do Production aplikace. Tento
alias na 443 se nesmí později změnit na aplikační proxy bez nové revize
hostname/cookie izolace. Canonical Production session zůstává host-only pro
`fuapay.tul.cz`.

Lokální HTTPS edge acceptance ověřila:

- `/health/ready` přes Nginx na `:8443`: Healthy;
- `/Development/SignIn`: HTTP 200;
- staging zůstal systemd `disabled`;
- Production zůstala současně Healthy;
- Production alias na `:443` zůstal HTTP 301;
- Production Nginx konfigurace byla byte-identická.

Následná browser acceptance z povolené klientské IP prošla bez Entra loginu.
Statický profil `administrator` vytvořil samostatného testovacího
Administratora. Přesně ověřený DB delta byl:

- `access.users`: 11 -> 12;
- `access.external_identities`: 11 -> 12;
- `access.role_assignments`: 20 -> 22;
- `audit.events`: 256 -> 257;
- credit accounts/movements, jobs, payments, FinancialDocuments, notification
  outbox, ServiceUnits, migration history a SafeQ transfer data beze změny.

Nová identita je přesně
`development | fua-pay-local | administrator`, má pouze aktivní role Customer
a Admin, grant procesy `first-login` a `development-sign-in` a právě jeden
`access.user-provisioned` audit event. Historické tři
`microsoft-entra` identity z importovaného datasetu zůstaly beze změny a nejsou
aktuálním staging login mechanismem.

Výsledek:

`STAGING ADMIN BROWSER LOGIN ACCEPTANCE: PASS`.

## 9. ČSOB staging hranice po acceptance 2026-09-22

Mimo řízené testovací okno staging bezpečně používá:

```text
Payments__Provider=None
Csob__Enabled=false
```

Při řízeném integračním testu se výhradně ve staging env dočasně nastavuje:

```text
Payments__Provider=Csob
Csob__Enabled=true
Csob__ApiBaseUrl=https://iapi.iplatebnibrana.csob.cz/
Csob__ReturnUrl=https://fuapay.fa.tul.cz:8443/payments/csob/return
```

FUA Pay v eAPI `payment/init` posílá `Csob__ReturnUrl` jako součást
podepsaného požadavku. Browserový návrat na uvedenou adresu proto cílí na
staging `:8443`, nikoli na Production `:443`.

Akceptace explicitního HTTPS portu `:8443` už není otevřená otázka:
2026-09-22 ji přímo potvrdil skutečný integration `payment/init` a následné
browser returny success/cancel/expiry. Browser return přitom není finanční
autorita; autoritativní stav dál potvrzuje podepsaný serverový
`payment/status` a reconciliation. Production ČSOB zůstává
`Payments__Provider=None`, `Csob__Enabled=false` a její return URL ani
payment runtime se tím nemění.

Postup, který byl 2026-09-22 skutečně proveden a zůstává kanonickým vzorem pro
další staging okna:

1. ověřit fail-closed staging baseline a Production health/config guard;
2. vytvořit hash-ověřený staging env backup;
3. zapnout ČSOB pouze ve stagingu, ponechat službu systemd `disabled`;
4. ověřit startup, readiness/worker a fresh GET/POST echo;
5. povolit `8443/tcp` pouze z konkrétní klientské IPv4;
6. provést požadované integration/browser scénáře;
7. před stopem ověřit, že nezůstaly `Pending` ani due reconciliation položky;
8. zastavit staging, obnovit přesný fail-closed env baseline, odstranit dočasné
   UFW allow a ověřit, že `127.0.0.1:5081` neposlouchá;
9. znovu ověřit Production health, nezměněný Production Nginx hash a 301 alias;
10. odstranit pouze přesně identifikované dočasné staging artefakty.

Starší housekeeping bod ohledně historických staging kopií/rolí se nemá míchat
s payment acceptance a řeší se samostatně podle aktuálního runtime auditu.

## 10. Kanonický provozní režim staging testovacího okna

Staging interactive signin používá záměrně bezheslové statické testovací identity
včetně profilu Administrator. Tyto identity nejsou samy o sobě přístupovou
ochranou. Bezpečnostní hranice je proto před aplikací:

1. `fuapay-staging.service` je mimo testovací okno vždy `inactive` a
   `disabled`;
2. UFW mimo testovací okno nemá žádné `ALLOW` pravidlo pro `8443/tcp`;
3. Kestrel staging backend `127.0.0.1:5081` mimo testovací okno neposlouchá;
4. Nginx listener `0.0.0.0:8443` může zůstat trvale připravený, ale bez UFW
   allow pravidla není zvenku dostupný;
5. při testu se `8443/tcp` povolí pouze z konkrétní aktuální klientské IPv4,
   ze které probíhá browser/SSH acceptance;
6. teprve potom se ručně spustí `fuapay-staging.service`;
7. po testu se staging služba zastaví a dočasné UFW pravidlo se odstraní;
8. po každém otevření i zavření testovacího okna se znovu ověří Production
   `/health/ready` a nezměněný 301 alias
   `https://fuapay.fa.tul.cz:443 -> https://fuapay.tul.cz/`.

Basic Auth ani další aplikační heslo se do tohoto modelu nyní nepřidává.
Testovací identity nejsou veřejně dostupné, protože staging runtime i firewall
jsou mimo testovací okno zavřené. Toto je záměrné také kvůli ČSOB browser return:
během integračního testu se browser vrací na staging z téže povolené klientské
cesty, zatímco serverová ČSOB volání `echo`, `payment/init`,
`payment/status` a `payment/reverse` jsou odchozí ze staging runtime.

Production se při staging testovacím okně nesmí měnit. Zejména se nesmí měnit:

- `/etc/fuapay/production.env`;
- `fuapay.service`;
- Production release/current symlink;
- Production DB `fuapay` ani role `fuapay_app` / `fuapay_migrator`;
- Production Entra konfigurace;
- Production `Payments__Provider` / `Csob__Enabled`;
- Production ČSOB klíče ani return URL;
- Production Nginx site `/etc/nginx/sites-available/fuapay`;
- chování portů 80/443.

Při ČSOB integration acceptance se mění pouze staging konfigurace. Staging
return URL je:

```text
https://fuapay.fa.tul.cz:8443/payments/csob/return
```

Budoucí Production return URL je oddělená:

```text
https://fuapay.tul.cz/payments/csob/return
```

Staging nesmí při integration testu použít Production return URL. Akceptaci
explicitního portu `:8443` již 2026-09-22 potvrdil skutečný kontrolovaný
integration `payment/init` a následné browser returny; tato vlastnost je proto
pro současný staging model ověřená.

### Ověřený uzavřený stav po browser acceptance 2026-09-20

Po úspěšném Admin browser acceptance byl staging znovu bezpečně uzavřen:

- `fuapay-staging.service = inactive`;
- `fuapay-staging.service = disabled`;
- UFW: žádné `ALLOW` pro `8443/tcp`;
- `127.0.0.1:5081`: neposlouchá;
- Nginx `0.0.0.0:8443`: připravený;
- Production `https://fuapay.tul.cz/health/ready`: `Healthy`;
- Production alias `https://fuapay.fa.tul.cz:443`: stále HTTP 301 na
  `https://fuapay.tul.cz/`.

Výsledek:

`STAGING CLOSED / PRODUCTION UNCHANGED: PASS`.

Tento uzavřený stav je výchozí stav, ze kterého se má zahajovat každé další
staging testovací okno.


## 11. Fresh ČSOB acceptance checkpoint 2026-09-22

Tento oddíl doplňuje přímo ověřené skutečnosti z řízeného ČSOB integration
okna 2026-09-22. Nemění kanonické pravidlo, že staging je mimo testovací okno
`inactive` / `disabled` a bez veřejného UFW allow pro `:8443`.

Před aktivací integration provideru bylo znovu ověřeno:

- Production `fuapay.service = active/enabled`, staging
  `fuapay-staging.service = inactive/disabled`;
- Production i staging symlink ukazovaly na release
  `774b324c48d8f874db21f115479f3b317c2a73d0`;
- obě nasazené `FuaPay.Web` binárky měly shodný SHA-256
  `98b26fac12a0ba68ade76297307890a902efad9e50ba4af0fa2aac6752086fb9`;
- systemd staging čte přesně `/etc/fuapay-staging/staging.env`;
- staging env je `root:fuapay-staging` mode `0640`;
- integration private key i gateway public key jsou
  `fuapay-staging:fuapay-staging` mode `0600` a oba se parsují jako validní;
- staging `AllowedHosts=fuapay.fa.tul.cz`,
  `Hosting__UseForwardedHeaders=true` a
  `Hosting__KnownProxies__0=127.0.0.1`;
- HARICA certifikát pro `fuapay.tul.cz` obsahuje SAN
  `fuapay.tul.cz` i `fuapay.fa.tul.cz` a byl v době testu platný do
  2027-02-28;
- Production Nginx site SHA-256 zůstal
  `491a7228c460ce5c8674a98e6c4c0d0433e0b4fd990360f29cf2d9c9280a91d9`;
- staging Nginx site SHA-256 byl
  `848900183f3ce0b5651530ea023f1135e40b0f462dc95ca97aa06551910b9c37`.

Pro acceptance okno byl pouze staging přepnut na:

```text
Payments__Provider=Csob
Csob__Enabled=true
Csob__ApiBaseUrl=https://iapi.iplatebnibrana.csob.cz/
Csob__MerchantId=M1EPAY2213
Csob__ReturnUrl=https://fuapay.fa.tul.cz:8443/payments/csob/return
```

Production po celou dobu zůstala na `Payments__Provider=None` a
`Csob__Enabled=false`; její health byl před/po jednotlivých testech
`Healthy` a Production Nginx hash se nezměnil.

Fresh GET i POST `echo` vrátily `resultCode=0`, `resultMessage=OK` a podpis
obou odpovědí byl ověřen integration gateway public key. První kontrolovaný
`payment/init` potvrdil, že integration gateway přijímá explicitní
`:8443` v `returnUrl`; skutečné browser návraty cancel/success/expiry následně
prošly přes tuto staging URL.

Live acceptance dále ověřila:

- cancel bez finančního efektu;
- úspěšný 10 Kč top-up s právě jedním kreditním pohybem a jedním finančním
  dokumentem;
- expiry po TTL s ověřenou browser evidencí `130/6` a následným serverovým
  `0/6`, bez finančního efektu;
- restart staging procesu během `Pending` platby (PID
  `328579 -> 331018`) s durabilní recovery a právě jedním finančním efektem po
  následném úspěšném dokončení stejné platby;
- ztracený browser return: po dočasném odstranění pouze stagingového inbound
  UFW allow pro `:8443` browser skončil `ERR_CONNECTION_TIMED_OUT`, zatímco
  worker bez browser returnu dokončil platbu z autoritativního
  `payment/status 0/7` právě jednou. UFW allow byl po důkazu vrácen pouze pro
  aktuální testovací klientskou IPv4;
- mobile smoke na skutečném telefonním browseru přes stejnou povolenou klientskou
  IPv4: nový 10 Kč top-up `ef92e52d-dffc-465b-a2c8-c97dadacb8f4` /
  `398b8c18f4ce@LI` skončil `Succeeded`, reconciliation `Completed` s `0/7`,
  právě jeden kreditní pohyb +10 Kč, právě jeden finanční dokument a kredit Beta
  230 -> 240 Kč. Uživatelský průchod potvrdil použitelné mobilní UI;
- duplicate browser return: nový successful top-up
  `d707aa08-681f-4557-8298-f46e5ee6cbd9` / `106199d5aa1e@LI` měl originální
  browser POST na `/payments/csob/return` v `14:21:21Z`; tentýž čerstvý,
  autentický podepsaný form payload byl bez cookies replayován v `14:22:07Z` a
  znovu vrátil HTTP 303 na stejný payment detail. Platba/reconciliation už byly
  dokončené před prvním returnem a duplicate replay nezměnil jejich stav,
  timestamps ani version; zůstal právě jeden kreditní pohyb +10 Kč a právě jeden
  dokument;
- declined authorization attempt: karta `4000007000010006` s CVC `200` byla na
  platební stránce zamítnuta vydavatelem, ale autoritativní status zůstal `0/2`.
  Platba `58b18f4e-ed26-4972-aca2-9f2e15880c33` zůstala `Pending` bez pohybu a
  dokladu, dokud uživatel explicitně nezvolil `Zrušit platbu a vrátit se do
  obchodu`; potom skončila `Cancelled`, reconciliation `Completed`, gateway
  `0/3`, stále bez finančního efektu. Tím se potvrdilo, že odmítnutí jedné
  autorizace není totéž co terminální `Failed` celé payment session.

Terminální `Failed` (`0/6`) nebyl fresh live browser scénářem vyvolán. Také nebyl
uměle vyvolán opakovaný provider `payment/status` nad již `Succeeded` platbou;
UI odkaz `Obnovit stav ručně` provider nevolá. Na terminální `Cancelled` platbě
je tento odkaz aktuálně stále zobrazen; jde o neblokující UX rest, nikoli o
security/financial problém.

### Finální closeout acceptance okna 2026-09-22

Před zastavením stagingu bylo read-only ověřeno `Pending=0` a due reconciliation
`=0`. Poté byl proveden pouze staging closeout:

- `fuapay-staging.service = inactive` a `disabled`;
- aktivní `/etc/fuapay-staging/staging.env` byl vrácen přesně na původní
  fail-closed obsah, SHA-256
  `3cc93de56b0a4329e331fe2ecf2e476cd90c269e1946650654786434fe2c69d4`,
  `root:fuapay-staging`, mode `0640`;
- aktivní hodnoty jsou znovu `StagingTestMode__Enabled=true`,
  `Entra__Enabled=false`, `Payments__Provider=None`, `Csob__Enabled=false`;
- jediné dočasné UFW allow `147.230.72.120 -> 8443/tcp` bylo odstraněno a žádné
  další `8443` UFW pravidlo nezůstalo;
- `127.0.0.1:5081` už neposlouchá; Nginx listener `0.0.0.0:8443` zůstává podle
  kanonického modelu připravený, ale bez UFW allow není zvenku dostupný;
- dnešní dočasné soubory
  `staging.env.before-csob-20260922` a
  `staging.env.csob-candidate-20260922` byly po hash guardu odstraněny;
- zůstaly tři historické root-only backupy z 2026-09-20:
  `staging.env.before-test-signin-20260920T114003Z` a dvě
  `staging.env.before-8443-*` kopie; nejsou aktivní konfigurací a dnešní cleanup
  jejich scope záměrně nerozšiřoval;
- Production `/health/ready` zůstala `Healthy`, Production Nginx site SHA-256
  zůstal
  `491a7228c460ce5c8674a98e6c4c0d0433e0b4fd990360f29cf2d9c9280a91d9` a
  `https://fuapay.fa.tul.cz:443` dál vrací HTTP 301 na
  `https://fuapay.tul.cz/`.

Výsledek: `STAGING CLOSED / PRODUCTION UNCHANGED: PASS`.

### Multi-user CardJob playtest a druhý finální closeout 2026-09-22

Po prvním bezpečném closeoutu byl staging ještě jednou krátce otevřen stejným
kanonickým postupem: z přesně ověřeného fail-closed env baseline vznikl dočasný
backup, ČSOB byl aktivován pouze ve stagingu, `fuapay-staging.service` zůstala
systemd `disabled`, UFW otevřel `8443/tcp` pouze pro klientskou IPv4
`147.230.72.120`, staging readiness i reconciliation worker byly `Healthy` a
Production guard před/po otevření prošel s nezměněným Nginx hashem.

Dva lidé pak přes oddělené statické testovací identity provedli dvě běžné
přímé karetní platby zakázek:

- `3D-2026-000004` / `3D tisk - Pardubice`, 120 Kč:
  payment `5b3faa6e-292b-47ee-91b5-d7417eb192aa`, ČSOB payId
  `095401bf4bf9@LI`, lokálně `Succeeded`, reconciliation `Completed`, poslední
  gateway stav `0/7`, zakázka `Paid`, settlement type `DirectPayment`, document
  `FUA-2026-000010`;
- `PLT-2026-000002` / `Tisk závěrečné prezentace`, 520 Kč:
  payment `0f00a7b4-e746-4800-be30-415c7666b2ff`, ČSOB payId
  `0d973b5d9984@LI`, lokálně `Succeeded`, reconciliation `Completed`, poslední
  gateway stav `0/7`, zakázka `Paid`, settlement type `DirectPayment`, document
  `FUA-2026-000011`.

Read-only consistency kontrola pro obě zakázky prokázala právě jednu payment,
právě jednu `Succeeded` a žádnou `Pending` na každou zakázku. U obou se přesně
shodoval payment ID se `jobs.settlement_reference_id` i
`financial_documents.documents.source_id`; částka a provider reference se
shodovaly mezi payment a dokumentem. Nginx zaznamenal oba čerstvé browser
returny jako HTTP 303. Staging journal od začátku playtestu neobsahoval žádný
warning/error a před finálním stopem bylo znovu `Pending=0`, due reconciliation
`=0`.

Druhý finální closeout pak znovu prokázal:

- `fuapay-staging.service = inactive/disabled`;
- aktivní staging env přesně SHA-256
  `3cc93de56b0a4329e331fe2ecf2e476cd90c269e1946650654786434fe2c69d4` a
  `Payments__Provider=None`, `Csob__Enabled=false`;
- žádné UFW `8443` pravidlo a žádný listener na `127.0.0.1:5081`;
- dočasný `staging.env.before-playtest-20260922` byl po hash guardu odstraněn;
- Production `/health/ready = Healthy`, Production Nginx site SHA-256 stále
  `491a7228c460ce5c8674a98e6c4c0d0433e0b4fd990360f29cf2d9c9280a91d9` a
  production alias dál HTTP 301 na `https://fuapay.tul.cz/`.

Výsledek: `MULTI-USER CARDJOB PLAYTEST PASS / STAGING CLOSED / PRODUCTION UNCHANGED`.

Live negativní access-isolation browser probe a live double-click/concurrent
CardJob creation probe nebyly před druhým closeoutem provedeny; zůstávají
explicitními hardening follow-up body, nikoli implicitně splněnou evidencí.
