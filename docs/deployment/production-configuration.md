# Produkční konfigurace a provoz

Repozitář připravuje aplikaci k řízenému nasazení, ale neobsahuje ani nemění
živý server `fuapay.tul.cz`. Production startuje fail-closed.

## Povinná konfigurace

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

Payments__Provider=Csob
Csob__Enabled=true
Csob__ApiBaseUrl=https://api.platebnibrana.csob.cz/
Csob__MerchantId=<production merchant ID>
Csob__PrivateKeyPath=/var/lib/fuapay/secrets/csob-private.pem
Csob__GatewayPublicKeyPath=/var/lib/fuapay/secrets/csob-gateway-public.pem
Csob__ReturnUrl=https://fuapay.tul.cz/payments/csob/return

Receipts__Enabled=false
Receipts__PreviewMode=false
Receipts__Issuer__LegalName=<schválený právní název vystavitele>
Receipts__Issuer__UnitName=<schválená součást / fakulta>
Receipts__Issuer__AddressLine1=<ulice a číslo>
Receipts__Issuer__AddressLine2=<PSČ a obec>
Receipts__Issuer__Country=<země>
Receipts__Issuer__RegistrationNumber=<ověřené IČO>
Receipts__Issuer__VatNumber=<ověřené DIČ>
Receipts__Issuer__ContactEmail=<kontakt pro doklad>
Receipts__VatRatePercent=<schválená sazba>
Receipts__RegularFontPath=/var/lib/fuapay/fonts/<regular-font>.ttf
Receipts__BoldFontPath=/var/lib/fuapay/fonts/<bold-font>.ttf
```

Adresář Data Protection musí existovat před startem, být trvalý mimo release a
čitelný/zapisovatelný pouze účtem služby. ČSOB privátní klíč má být trvalý mimo
release a pouze čitelný tímto účtem. Změna/odstranění Data Protection klíčů
zneplatní sessions a antiforgery cookies.

Doklady zůstávají v Production vypnuté, dokud nejsou účetní údaje a pravidlo
DPH schválené. Při zapnutí musí být `PreviewMode=false`, nesmí zůstat preview
IČO/DIČ a oba font soubory musí existovat mimo release. Podrobnosti jsou v
[PDF potvrzení o úhradě](../features/payment-receipts.md).

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
5. Nasadit artefakt s výše uvedenou chráněnou konfigurací a zachovat
   Data Protection key ring.
6. Ověřit `/health/live` a `/health/ready`, OIDC login/logout, role a jednu
   řízenou platební cestu. Teprve potom přepnout provoz.

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
- ČSOB reconciliation worker řeší pending/recovery stavy, položky
  `RequiresAttention` kontroluje Administrator;
- notifikační outbox uchovává transakčně vzniklé zprávy, ale repozitář
  neobsahuje externí e-mailový transport.

Provozovatel musí mimo repozitář nastavit sběr/retenci logů, alert na opakované
503 a reconciliation chyby, incidentní kontakt a rotaci Entra secretu i ČSOB
klíčů.
