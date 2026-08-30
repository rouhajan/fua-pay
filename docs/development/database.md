# PostgreSQL, migrace a testy

FUA Pay používá PostgreSQL a EF Core. Historie migrací je v
`app.__ef_migrations_history`; tabulky jsou rozdělené do schémat `access`,
`service_units`, `jobs`, `credits`, `payments`, `audit` a `notifications`.
Aplikační účet nemá být PostgreSQL superuser.

## Lokální databáze

Connection string patří do User Secrets nebo proměnné prostředí:

```bash
dotnet user-secrets set \
  --project src/FuaPay.Web/FuaPay.Web.csproj \
  'ConnectionStrings:FuaPay' \
  'Host=localhost;Database=fuapay_dev;Username=fuapay_app;Password=...'
```

Migrace se spravují forward-only:

```bash
dotnet tool restore
dotnet ef database update \
  --project src/FuaPay.Web/FuaPay.Web.csproj \
  --startup-project src/FuaPay.Web/FuaPay.Web.csproj \
  --context FuaPayDbContext

dotnet ef migrations has-pending-model-changes \
  --project src/FuaPay.Web/FuaPay.Web.csproj \
  --startup-project src/FuaPay.Web/FuaPay.Web.csproj \
  --context FuaPayDbContext
```

Nová změna modelu dostane novou migraci; již aplikované migrace se
nepřepisují. Produkční migrace má být samostatný řízený krok po záloze, ne
tichý vedlejší efekt startu (`Database__ApplyMigrationsOnStart=false`).

## Testy

Rychlé testy nevyžadují PostgreSQL:

```bash
dotnet test tests/FuaPay.Web.Tests/FuaPay.Web.Tests.csproj
```

PostgreSQL integrační testy jsou destruktivní jen uvnitř vyhrazené testovací
databáze. Safety guard vyžaduje současně:

- `FUA_PAY_DATABASE_TESTS_ALLOWED=1`;
- host pouze `localhost`, `127.0.0.1` nebo `::1`;
- název databáze `fuapay_test`, `fuapay_test_*`, `fuapay_audit` nebo
  `fuapay_audit_*`;
- explicitní `ConnectionStrings__FuaPay` nebo odpovídající User Secret.

```powershell
$env:FUA_PAY_DATABASE_TESTS_ALLOWED = "1"
$env:ConnectionStrings__FuaPay = "Host=localhost;Database=fuapay_test;Username=fuapay_app;Password=..."
./scripts/verify.ps1 -RunDatabaseTests
```

Testy ověřují reálné PostgreSQL constraints, souběh, rollback a exact-once
settlement top-up/job plateb. Bez opt-in se databázový projekt záměrně ani
nespustí.

## Čerstvé schéma a deployment migrace

Před release se na nové prázdné izolované databázi aplikuje připravený
execution artefakt popsaný níže, následně se spustí PostgreSQL testy a
`has-pending-model-changes`. Pro řízené nasazení se předem vygeneruje a
zkontroluje idempotentní SQL:

```bash
dotnet ef migrations script --idempotent \
  --project src/FuaPay.Web/FuaPay.Web.csproj \
  --startup-project src/FuaPay.Web/FuaPay.Web.csproj \
  --context FuaPayDbContext \
  --configuration Release \
  --no-build \
  --output /secure/release/fuapay-migrations.sql
```

Původní soubor se neupravuje. Oddělený execution artefakt s bezpečným
odstraněním pouze vedoucího UTF-8 BOM a deterministickým `SET ROLE` připravuje
a přesně proti originálu ověřuje repozitářový nástroj. Následný explicitně
zacílený `psql` krok používá `ON_ERROR_STOP=1`. Kompletní kanonický postup je v
[Release a databázové artefakty](../deployment/release-artifacts.md).

Skutečnou produkční databázi ani její kopii se skutečnými osobními údaji
nepoužívejte pro automatizované testy.

## Windows PowerShell

Pravidla pro Windows PowerShell 5.1 a kódování ověřovacích
skriptů jsou popsána v [samostatné vývojové poznámce](powershell.md).
