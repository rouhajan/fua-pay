# Release a databázové artefakty

Tato stránka je kanonický postup pro vytvoření nasazovacího archivu FUA Pay a
pro přípravu a spuštění EF migration SQL. Nástroj
`tools/FuaPay.DeploymentArtifacts` je součástí solution, nemá externí balíčkové
závislosti a funguje stejně na Windows i Linuxu.

Historické release archivy ani záznamy o již provedených deploymentech se tímto
postupem nemění.

## Linux release archiv

Nejdříve se provede běžné ověření, locked restore a self-contained publish pro
`linux-x64`:

```powershell
./scripts/verify.ps1

dotnet restore `
    src/FuaPay.Web/FuaPay.Web.csproj `
    --runtime linux-x64 `
    --locked-mode

dotnet publish `
    src/FuaPay.Web/FuaPay.Web.csproj `
    --configuration Release `
    --runtime linux-x64 `
    --self-contained `
    --no-restore `
    --output C:/secure/release/fuapay-linux-x64 `
    -p:TreatWarningsAsErrors=true
```

Publish adresář se nesmí balit obecným Windows `tar`. Archiv vytvoří pouze
repozitářový nástroj, který zapisuje Unix metadata přímo do tar hlaviček:

```powershell
dotnet run `
    --project tools/FuaPay.DeploymentArtifacts/FuaPay.DeploymentArtifacts.csproj `
    --configuration Release `
    --no-build `
    -- `
    release create `
    --source C:/secure/release/fuapay-linux-x64 `
    --archive C:/secure/release/fuapay-linux-x64.tar.gz
```

Nástroj nejdříve zkontroluje celý zdrojový strom. Přijímá jen běžné soubory a
adresáře a odmítne symlinky, reparse points, FIFO, sockety, zařízení, jiné
speciální objekty a cesty nereprezentovatelné v USTAR. Archiv zapisuje pod
unikátním názvem s příponou `.partial` ve stejném adresáři. Teprve po uzavření,
flush na disk a úplném ověření jej atomicky přejmenuje na požadovaný finální
název; existující finální artefakt nikdy nepřepíše.

Profil je pevný:

- kořen a všechny adresáře: `0770`;
- běžné soubory: `0660`;
- `FuaPay.Web`: `0750`;
- formát tar hlaviček: USTAR bez procesně proměnlivých PAX hlaviček;
- UID/GID: `0` jako neutrální archivní hodnota;
- USTAR user/group name: prázdné;
- gzip: bez flags/extra/name/comment, MTIME `0`, XFL `0`, OS `255`;
- timestamp všech položek: Unix epoch.

Také při samostatném ověření jsou odmítnuté jiné typy tar položek, duplicitní
nebo nebezpečné cesty a jakákoli odchylka módů či deterministických gzip/USTAR
metadat. Archivní UID/GID `0` neurčuje finálního vlastníka živého release;
normalizace na `fuapay:fuapay` je povinnou součástí níže uvedené instalace.

Po přenosu se před extrakcí vedle ověření SHA-256 spustí kontrola samotného
archivu:

```powershell
dotnet run `
    --project tools/FuaPay.DeploymentArtifacts/FuaPay.DeploymentArtifacts.csproj `
    --configuration Release `
    --no-build `
    -- `
    release verify `
    --archive C:/secure/release/fuapay-linux-x64.tar.gz
```

Na Linuxu se celý následující blok spustí jako `root` až po kontrole SHA-256 a
`release verify`. `--preserve-permissions` brání zúžení módů přes `umask` a
`--no-same-owner` ignoruje neutrální archivní UID/GID. Po extrakci se vlastnictví
vždy normalizuje a rekurzivně ověří. Všechny kontroly musí projít před atomickým
přepnutím `/opt/fuapay/current`:

```bash
set -euo pipefail

revision='<verified-commit-sha>'
archive='/secure/release/fuapay-linux-x64.tar.gz'
release_dir="/opt/fuapay/releases/${revision}"
next_link="/opt/fuapay/.current-${revision}.partial"

test ! -e "$release_dir"
test ! -e "$next_link"
test ! -L "$next_link"
install --directory --owner=root --group=root --mode=0700 "$release_dir"
tar \
  --extract \
  --gzip \
  --preserve-permissions \
  --no-same-owner \
  --file="$archive" \
  --directory="$release_dir"
chown --recursive fuapay:fuapay "$release_dir"

unexpected_owner="$(
  find "$release_dir" \
    \( ! -user fuapay -o ! -group fuapay \) \
    -print \
    -quit
)"
test -z "$unexpected_owner"

while IFS= read -r -d '' directory; do
  test "$(stat --format='%a' "$directory")" = '770'
done < <(find "$release_dir" -type d -print0)

while IFS= read -r -d '' file; do
  test "$(stat --format='%a' "$file")" = '660'
done < <(
  find "$release_dir" \
    -type f \
    ! -path "$release_dir/FuaPay.Web" \
    -print0
)

test "$(stat --format='%a' "$release_dir/FuaPay.Web")" = '750'
runuser --user=fuapay -- test -x "$release_dir/FuaPay.Web"

# Aktivace je až poslední krok po ověření vlastníků, módů a executable bitu.
ln --symbolic "$release_dir" "$next_link"
mv --force --no-target-directory "$next_link" /opt/fuapay/current
```

CI stejný profil ověřuje po extrakci přes `sudo` a po normalizaci na dočasný
lokální účet `fuapay`, včetně vlastníků/skupin, módů a spuštění hostu tímto
účtem.

## EF migration SQL

Původní idempotentní SQL se vygeneruje jako samostatný artefakt. Tento soubor
zůstává nezměněný pro review a hash evidence:

```powershell
dotnet ef migrations script `
    --idempotent `
    --project src/FuaPay.Web/FuaPay.Web.csproj `
    --startup-project src/FuaPay.Web/FuaPay.Web.csproj `
    --context FuaPayDbContext `
    --configuration Release `
    --no-build `
    --output C:/secure/release/fuapay-migrations.sql
```

Z něj se připraví oddělený execution artefakt:

```powershell
dotnet run `
    --project tools/FuaPay.DeploymentArtifacts/FuaPay.DeploymentArtifacts.csproj `
    --configuration Release `
    --no-build `
    -- `
    migrations prepare `
    --input C:/secure/release/fuapay-migrations.sql `
    --output C:/secure/release/fuapay-migrations.execution.sql `
    --role fuapay_migrator
```

Příprava provede pouze tyto změny:

1. zjistí, zda původní soubor začíná UTF-8 BOM;
2. z execution obsahu odstraní právě tento jeden vedoucí BOM;
3. odmítne BOM na jakémkoli jiném místě;
4. před nezměněné zbývající bajty vloží
   `SET ROLE "fuapay_migrator";` a jeden LF.

Oba SHA-256 hashe nástroj vypíše. Původní SQL se po přípravě znovu načte a
ověří byte-for-byte. Execution artefakt vzniká pod unikátním same-directory
`.partial` názvem a na finální název se atomicky přesune až po úplném ověření.
Existující execution soubor se nepřepisuje.

Po přenosu a před spuštěním se musí znovu prokázat přesný vztah obou souborů:

```powershell
dotnet run `
    --project tools/FuaPay.DeploymentArtifacts/FuaPay.DeploymentArtifacts.csproj `
    --configuration Release `
    --no-build `
    -- `
    migrations verify `
    --original C:/secure/release/fuapay-migrations.sql `
    --execution C:/secure/release/fuapay-migrations.execution.sql `
    --role fuapay_migrator
```

Ověření uspěje pouze pro přesný `SET ROLE` prefix následovaný původními bajty
bez jednoho volitelného vedoucího BOM. Zkrácený nebo jinak změněný execution
soubor je odmítnut.

Před spuštěním musí být hotový backup, review původního SQL a ověření obou
hashů. Heslo se načte z chráněného `PGPASSFILE` nebo explicitně nastaveného
`PGPASSWORD`; není součástí příkazové řádky. Host, port, databáze a login se
naopak vždy předají explicitně a nesmí se převzít z `PGSERVICE` ani z výchozích
libpq hodnot.

```bash
set -euo pipefail

db_host='postgres.internal.example'
db_port='5432'
db_name='fuapay'
db_login='fuapay_deployer'
execution_sql='/secure/release/fuapay-migrations.execution.sql'
target=(
  "--host=${db_host}"
  "--port=${db_port}"
  "--dbname=${db_name}"
  "--username=${db_login}"
)

identity="$(
  psql \
    "${target[@]}" \
    --no-psqlrc \
    --set=ON_ERROR_STOP=1 \
    --tuples-only \
    --no-align \
    --field-separator='|' \
    --command="SELECT current_database(), current_user, COALESCE(inet_server_addr()::text, 'local-socket'), inet_server_port();"
)"
IFS='|' read -r actual_db actual_user server_address server_port <<< "$identity"
test "$actual_db" = "$db_name"
test "$actual_user" = "$db_login"
test -n "$server_address"
test "$server_port" = "$db_port"
printf 'Verified PostgreSQL target: database=%s user=%s server=%s:%s\n' \
  "$actual_db" "$actual_user" "$server_address" "$server_port"

psql \
  "${target[@]}" \
  --no-psqlrc \
  --set=ON_ERROR_STOP=1 \
  --file="$execution_sql"
```

Identity preflight i aplikace SQL používají přesně stejné explicitní cílové
argumenty. `psql` dostane vždy `--no-psqlrc`, `--set=ON_ERROR_STOP=1` a
`--file=<execution artifact>`. Jakýkoli nenulový exit code ukončí blok jako
chybu. SQL se neskládá přes konkatenovaný stdin stream, takže se BOM nemůže
přesunout doprostřed vstupu.

Automatické migrace při startu aplikace zůstávají vypnuté přes
`Database__ApplyMigrationsOnStart=false`.
