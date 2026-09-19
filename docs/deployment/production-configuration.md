# Produkční konfigurace a provoz

Repozitář připravuje aplikaci k řízenému nasazení, ale neobsahuje ani nemění
živý server `fuapay.tul.cz`. Production startuje fail-closed.

## Cílový provozní model: jeden server

FUA Pay nemá mít samostatný dlouhodobě provozovaný staging server ani druhou
trvale běžící aplikační instanci. Cílový model je jeden produkční VM/server a
jedna aktivní `fuapay.service`.

Bezpečnost nasazení se neopírá o druhý server, ale o vrstvené ověření před
aktivací a o rychlý code rollback na stejném hostiteli:

- CI, CodeQL, repository verification a PostgreSQL integration testy proběhnou
  před nasazením nad izolovanými testovacími databázemi;
- release se jednou sestaví, zabalí a kryptograficky ověří;
- na serveru se nový release instaluje side-by-side do
  `/opt/fuapay/releases/<SHA>`, zatímco aktivní release zůstává beze změny;
- před aktivací se ověří artefakt, konfigurace, migrace, backup a oprávnění;
- `/opt/fuapay/current` se přepne atomicky na nový release a restartuje se
  jediná `fuapay.service`;
- po aktivaci musí projít bounded health a smoke gate; při chybě se vrátí
  `/opt/fuapay/current` na předchozí ověřený release;
- databázové migrace jsou forward-only a code rollback je nesmí automaticky
  vracet.

Současné staging/demo prostředí je přechodný stav během vývoje, nikoli cílová
druhá infrastruktura. Pro první produkční spuštění je rozhodnutý čistý cutover:
současná demo databáze se archivuje a produkce začne nad novou PostgreSQL
databází vytvořenou z aktuálního migration chainu. Demo kredit, zakázky,
platby, tiskové credentialy, auditní acceptance historie ani demo finanční
doklady se automaticky nepřenášejí do produkce.

Úplný postup a zbývající gate jsou v
[plánu čistého produkčního cutoveru](production-cutover-plan.md).

## První produkční profil bez karetních plateb

Hodnoty níže mají přijít ze service environment/secret store. Skutečná hesla,
client secret a privátní klíč nesmějí být v Git repozitáři.

```text
ASPNETCORE_ENVIRONMENT=Production
AllowedHosts=fuapay.tul.cz
ConnectionStrings__FuaPay=<produkční PostgreSQL connection string>
DataProtection__KeyRingPath=/var/lib/fuapay/data-protection
Database__ApplyMigrationsOnStart=false

DevelopmentSignIn__Enabled=false
DevelopmentData__Enabled=false
DevelopmentData__ResetOnStart=false
StagingTestMode__Enabled=false

Entra__Enabled=true
Entra__TenantId=<TUL tenant GUID>
Entra__ClientId=<app registration GUID>
Entra__ClientSecret=<secret value>
Entra__CallbackPath=/signin-oidc
Entra__SignedOutCallbackPath=/signout-callback-oidc

Payments__Provider=None
Csob__Enabled=false

PrintPayments__Enabled=false
PrintCredentials__Enabled=false

Receipts__Enabled=false
Receipts__PreviewMode=false
Receipts__Issuer__LegalName=<legacy preview vystavitel>
Receipts__Issuer__UnitName=<legacy preview součást / fakulta>
Receipts__Issuer__AddressLine1=<ulice a číslo>
Receipts__Issuer__AddressLine2=<PSČ a obec>
Receipts__Issuer__Country=<země>
Receipts__Issuer__RegistrationNumber=<ověřené IČO>
Receipts__Issuer__VatNumber=<ověřené DIČ>
Receipts__Issuer__ContactEmail=<kontakt pro doklad>
Receipts__VatRatePercent=<legacy preview sazba>
Receipts__RegularFontPath=/var/lib/fuapay/fonts/<regular-font>.ttf
Receipts__BoldFontPath=/var/lib/fuapay/fonts/<bold-font>.ttf
```

Tento profil je podporovaný základ pro přípravu prvního produkčního go-live.
Hodnoty `PrintPayments__Enabled=false` a `PrintCredentials__Enabled=false` v
tomto bloku jsou záměrný bezpečný pre-activation baseline. Před otevřením
systému uživatelům se po PASS samostatného FUA Print production gate oba
přepnou společně na `true` a doplní se níže uvedená produkční print
konfigurace.

Karetní část tohoto profilu nevyžaduje
ČSOB merchant ID, privátní ani gateway public key, API URL ani return URL.
Zákaznické UI ani HTTP handlery nenabízejí vytvoření karetního dobití nebo přímé
karetní platby, ČSOB return processing není mapovaný a ČSOB reconciliation worker
ani jeho externí klient neběží. CSP nepovoluje externí payment `form-action`
origin a zůstává na `form-action 'self'`.

Historické payment read modely a detaily zůstávají čitelné, pokud by v databázi
nějaké záznamy existovaly. Dostupnost karetních plateb není podmínkou pro
administrátorské ruční dobití a jeho kanonický `FinancialDocument`, úhradu
zakázky z existujícího kreditu ani FUA Print Reserve / ResolutionRequired /
Capture / Release a tiskové credentialy.

Adresář Data Protection musí existovat před startem, být trvalý mimo release a
čitelný/zapisovatelný pouze účtem služby. Změna/odstranění Data Protection klíčů
zneplatní sessions a antiforgery cookies.

## Pozdější aktivace produkčního ČSOB

ČSOB se aktivuje jako samostatný pozdější provozní milník až po dokončení jeho
bankovního, konfiguračního, bezpečnostního, payment a reconciliation acceptance
gate. Databázové schéma ani persistované hodnoty provideru se kvůli přechodu
nemění. Produkční profil se přepne na:

```text
Payments__Provider=Csob
Csob__Enabled=true
Csob__ApiBaseUrl=https://api.platebnibrana.csob.cz/
Csob__MerchantId=<production merchant ID>
Csob__PrivateKeyPath=/var/lib/fuapay/secrets/csob-private.pem
Csob__GatewayPublicKeyPath=/var/lib/fuapay/secrets/csob-gateway-public.pem
Csob__ReturnUrl=https://fuapay.tul.cz/payments/csob/return
```

V tomto profilu zůstávají beze změny všechny dosavadní požadavky na produkční
URL, klíče, podpisy, return endpoint, trusted process URI, CSP a reconciliation.
ČSOB privátní klíč musí být trvalý mimo release a pouze čitelný účtem služby.
Neúplná nebo konfliktní konfigurace musí zastavit startup; `Development` provider
není fallback.

Persistentní tiskové credentialy se zapínají pouze společně s PrintPayments:

```text
PrintPayments__Enabled=true
PrintPayments__Sources__0__PrintSourceId=<non-empty GUID>
PrintPayments__Sources__0__CredentialSha256=<SHA-256 service credentialu>
PrintCredentials__Enabled=true
PrintCredentials__PepperBase64=<base64 alespoň 32 náhodných bytů>
```

Pepper patří do production secret store mimo Git a release artefakt. Musí být
trvalý přes restarty i nasazení; jeho ztráta nebo rotace zneplatní všechny
existující tiskové PIN verifiery. Bezpečný provozní postup je credential feature
vypnout, nastavit nový pepper a vyžádat od zákazníků nové nastavení PINu. Tento
dokument neprohlašuje credential feature za nasazenou na stagingu ani v produkci.
Plaintext PIN ani pepper se nesmějí zapisovat do aplikačního logu nebo auditu.

Tyto `Receipts__*` hodnoty řídí jen legacy preview potvrzení a nejsou zdrojem
issueru ani DPH pro `FinancialDocuments`. Nové finanční dokumenty snapshotují
schválený issuer a 21% daňový rozpad z kanonického profilu v modulu
`FinancialDocuments`. Oba PDF renderery používají stejné logo a procesní font
manager; na Linuxu musí oba font soubory existovat mimo release.

## TLS a reverzní proxy

Při přímém TLS v Kestrelu:

```text
Hosting__UseForwardedHeaders=false
```

Při TLS terminovaném na reverzní proxy:

```text
Hosting__UseForwardedHeaders=true
Hosting__KnownProxies__0=127.0.0.1
```

Uvádějí se jen konkrétní IP adresy skutečné proxy; wildcard a obecné sítě
aplikace odmítá. Proxy musí přepsat `X-Forwarded-For`, `X-Forwarded-Host` a
`X-Forwarded-Proto`. `AllowedHosts` nesmí být `*` ani `+`. Pokud aplikace běží
pod prefixem, nastaví se například `Hosting__PathBase=/fuapay` a stejné cesty
se promítnou do Entra/ČSOB registrací.

## Migrace a nasazení

Doporučený řízený postup:

1. Spustit `scripts/verify.ps1` a PostgreSQL testy nad izolovanou databází.
2. Publikovat a zabalit jednou ověřený Release artefakt výhradně podle
   [kanonického postupu](release-artifacts.md); `appsettings.Development.json`
   se do publish výstupu nekopíruje.
3. Zálohovat databázi a ověřit, že je dostupný odpovídající restore postup.
4. Vygenerovat, zkontrolovat a BOM-aware připravit EF migration SQL podle
   stejného kanonického postupu; execution artefakt aplikovat jako samostatný
   oprávněný databázový krok s `ON_ERROR_STOP=1`.
5. Nainstalovat nový release side-by-side vedle aktivního release, zachovat
   chráněnou konfiguraci a Data Protection key ring a provést pre-activation
   kontroly.
6. Atomicky přepnout `/opt/fuapay/current`, restartovat jedinou
   `fuapay.service` a ověřit bounded `/health/live`, `/health/ready`,
   OIDC login/logout, role a řízený smoke scénář. Při selhání vrátit pouze kód
   na předchozí ověřený release; databázovou migraci automaticky nevracet.

Automatické migrace při startu jsou v produkčním vzoru vypnuté. Rollback kódu
nesmí automaticky vracet databázovou migraci; kompatibilitu a případný forward
fix posoudí provozovatel podle konkrétního release.

## Backup a restore

Kód sám PostgreSQL zálohy neprovádí. Provozovatel TUL musí stanovit RPO/RTO,
retenci, šifrování a přístup k zálohám. Minimum před schema změnou je
konzistentní backup (například PostgreSQL custom-format dump nebo platformní
snapshot/PITR) a pravidelně ověřený restore do izolovaného prostředí.

Restore se nesmí testovat nad živou databází. Po obnově se ověří stav migrací,
počty/finanční součty, `/health/ready` a přístupové role; obnovené osobní údaje
musí mít stejnou ochranu jako produkce.

## Provozní kontrola

- `/health/live` potvrzuje běh procesu;
- `/health/ready` ověřuje spojení s PostgreSQL a při chybě vrací 503;
- strukturované ASP.NET logy jdou do standardního outputu hostitele;
- administrativní a finanční audit je v databázi;
- při aktivním ČSOB profilu reconciliation worker řeší pending/recovery stavy a
  položky `RequiresAttention` kontroluje Administrator; při
  `Payments__Provider=None` a `Csob__Enabled=false` worker neběží;
- notifikační outbox uchovává transakčně vzniklé zprávy, ale repozitář
  neobsahuje externí e-mailový transport.

Provozovatel musí mimo repozitář nastavit sběr/retenci logů, alert na opakované
503 a incidentní kontakt. Alerty na reconciliation chyby a rotace ČSOB klíčů se
vyžadují od okamžiku pozdější aktivace ČSOB; rotace Entra secretu platí vždy.
