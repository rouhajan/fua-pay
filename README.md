# FUA Pay

FUA Pay je malá interní webová aplikace Fakulty umění a architektury TUL pro
správu uživatelů, pracovišť, zakázek, kreditu a plateb. Zachovává oddělený
výrobní stav zakázky a finanční stav úhrady.

## Co aplikace umí

- tři aplikační role: `Administrator`, `Requester` a `Customer`;
- jednoduchá pracoviště (`ServiceUnits`) a přiřazení zadavatelů;
- zakázky od konceptu po dokončení nebo zrušení;
- kreditní účty, auditované ruční korekce a úhradu zakázky kreditem;
- přímou platbu zakázky a dobití kreditu přes zvoleného poskytovatele;
- Microsoft Entra OIDC, JIT vytvoření Customer účtu a bezpečné ruční
  propojení existujícího účtu podle `tenant ID + object ID`;
- ČSOB eAPI 1.9 s podpisy, serverovým ověřením stavu, reconciliation a
  přesně jedním lokálním finančním účinkem;
- audit a autorizované CSV exporty;
- PDF potvrzení již ověřené úhrady zakázky bez změny finančního stavu.

Řešení je modulární monolit v ASP.NET Core na .NET 10 s PostgreSQL a EF Core.
Doménová logika není svázaná s transportem Microsoft Entra ani ČSOB.

## Lokální spuštění

Je potřeba .NET SDK podle `global.json` a PostgreSQL. Connection string patří
do User Secrets, ne do Git repozitáře:

```bash
dotnet user-secrets set \
  --project src/FuaPay.Web/FuaPay.Web.csproj \
  'ConnectionStrings:FuaPay' \
  'Host=localhost;Database=fuapay_dev;Username=fuapay_app;Password=...'
dotnet tool restore
dotnet ef database update \
  --project src/FuaPay.Web/FuaPay.Web.csproj \
  --startup-project src/FuaPay.Web/FuaPay.Web.csproj
dotnet run --project src/FuaPay.Web/FuaPay.Web.csproj
```

Vývojové prostředí používá explicitní testovací přihlášení a simulovaný
platební provider. Produkční startup tyto cesty odmítá.

## Ověření

```powershell
./scripts/verify.ps1
```

Skript provede locked restore, Release build, kontrolu formátování, webové a
aplikační testy a kontrolu souladu EF modelu s migracemi. PostgreSQL testy jsou
záměrně opt-in přes `-RunDatabaseTests`; živý ČSOB `echo` přes
`-RunCsobSandboxTests` a vlastní bezpečnostní proměnné.

## Dokumentace

- [Přehled a architektura](docs/architecture.md)
- [Microsoft Entra ID](docs/integrations/entra-id.md)
- [ČSOB eAPI](docs/integrations/csob.md)
- [PDF potvrzení o úhradě](docs/features/payment-receipts.md)
- [Databáze a testy](docs/development/database.md)
- [Produkční konfigurace a provoz](docs/deployment/production-configuration.md)
- [Release a databázové artefakty](docs/deployment/release-artifacts.md)
- [Bezpečnostní hranice](SECURITY.md)

## Licence a branding

Zdrojový kód a projektová dokumentace jsou dostupné pod [MIT licencí](LICENSE).
Institucionální názvy, loga a další prvky vizuální identity TUL/FUA nejsou touto
licencí poskytovány; podrobnosti jsou v [BRANDING.md](BRANDING.md). Licence
třetích stran zůstávají zachované u příslušných komponent.

Kód je připravený pro doplnění skutečných Entra a ČSOB hodnot. Integrace
nejsou bez těchto údajů živě externě ověřené a repozitář sám není produkčním
nasazením na `fuapay.tul.cz`.
