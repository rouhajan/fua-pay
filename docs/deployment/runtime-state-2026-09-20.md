# Runtime checkpoint 2026-09-20

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

## 9. ČSOB staging hranice a otevřené kroky

Aktuální staging stále bezpečně používá:

```text
Payments__Provider=None
Csob__Enabled=false
```

Při budoucím integračním testu se má výhradně ve staging env nastavit:

```text
Payments__Provider=Csob
Csob__Enabled=true
Csob__ApiBaseUrl=https://iapi.iplatebnibrana.csob.cz/
Csob__ReturnUrl=https://fuapay.fa.tul.cz:8443/payments/csob/return
```

FUA Pay v eAPI `payment/init` posílá `Csob__ReturnUrl` jako součást
podepsaného požadavku. Browserový návrat na uvedenou adresu proto cílí na
staging `:8443`, nikoli na Production `:443`.

Před prvním skutečným payment testem je však ještě nutné empiricky ověřit, že
ČSOB integration prostředí akceptuje HTTPS `returnUrl` s explicitním portem
`:8443`. Ve veřejných eAPI 1.9 příkladech je `returnUrl` normální parametr
`payment/init` s maximální délkou 300 znaků; tento checkpoint ale neoznačuje
nestandardní port za bankou potvrzený, dokud neprojde skutečný integration
`payment/init`.

Browser return není finanční autorita. Autoritativní stav dál potvrzuje
podepsaný serverový `payment/status` a reconciliation. Production ČSOB zůstává
`Payments__Provider=None`, `Csob__Enabled=false` a její return URL ani
payment runtime se tím nemění.

Otevřené staging kroky:

1. připravit přesnou staging ČSOB integration konfiguraci včetně
   `:8443` return URL;
2. před změnou znovu ověřit Production health/config guard;
3. zapnout ČSOB pouze ve stagingu a ověřit startup + GET/POST echo;
4. provést první kontrolovaný `payment/init` a tím ověřit akceptaci
   `:8443` return URL;
5. následně dokončit vlastní integration acceptance scénáře;
6. po integračním okně staging opět zastavit; systemd zůstává `disabled`;
7. po ověření odstranit/archivovat starý staging env a staré staging secret
   kopie pod production účtem a omezit přístup production DB role k
   historické `fuapay_demo`.

Žádný z těchto kroků nevyžaduje změnu Production URL, Production DB, Production
Entra konfigurace ani Production ČSOB provideru.
