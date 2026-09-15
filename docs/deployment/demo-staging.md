# Demo / staging deployment

Status: 2026-09-15

Tento soubor je zdroj pravdy pro aktuální staging runtime a pro staging-specifické
provozní údaje. Kanonické vytváření release a migration artefaktů je v
[`release-artifacts.md`](release-artifacts.md). Historický ČSOB incident je v
[`../integrations/csob-incident-2026-09-13.md`](../integrations/csob-incident-2026-09-13.md)
a jeho následná expiry acceptance v
[`../testing/csob-expiry-acceptance-2026-09-14.md`](../testing/csob-expiry-acceptance-2026-09-14.md).

## Aktuální runtime

Ověřeno na staging VM 2026-09-15:

- URL: `https://fuapay.tul.cz`;
- alternate URL: `https://fuapay.fa.tul.cz` -> canonical URL;
- Git `main`: `9ecee2d9c57d88a2969d42094e49597b41f1642c`;
- aktivní release:
  `/opt/fuapay/releases/bc868276aead1b350033303ce8e80bc6ce9a5894`;
- běžící executable:
  `/opt/fuapay/releases/bc868276aead1b350033303ce8e80bc6ce9a5894/FuaPay.Web`;
- `fuapay.service`: active/running;
- service account: `fuapay:fuapay`;
- Kestrel: `127.0.0.1:5080` behind Nginx;
- configuration: `/etc/fuapay/staging.env`;
- database: `fuapay_demo`;
- `Database__ApplyMigrationsOnStart=false`;
- databáze má 19 aplikovaných EF migrací;
- nejnovější migrace je
  `20260913111715_AddCsobExpiryFailureProvenance`;
- `/health/ready`: `Healthy`;
- `/health/workers/csob-reconciliation`: `Healthy`;
- Microsoft Entra login: live and in use;
- payment provider: ČSOB integration;
- simulated payments: disabled;
- `PrintPayments` a `PrintCredentials` nejsou ve staging environment konfiguraci
  aktivovány; committed defaults obou feature jsou `Enabled=false`;
- production ČSOB traffic ani production database workload nejsou na tomto
  stagingu aktivní.

ČSOB 30min expiry acceptance na release `bc868276...` dne 2026-09-14 je PASS:
browser return `130/6`, následný podepsaný server status `0/6`, interní stav
`Expired` a žádný finanční efekt. Konkrétní root cause incidentu z 2026-09-13
zůstává neprokázaný.

## Staging PostgreSQL deployment/auth model

Ověřeno přímo z PostgreSQL katalogu a `pg_hba_file_rules` 2026-09-15:

- databázi `fuapay_demo` vlastní `fuapay_migrator`;
- schémata `access`, `app`, `audit`, `credits`, `jobs`, `notifications`,
  `payments` a `service_units` vlastní `fuapay_migrator`;
- `fuapay_migrator` má na těchto schématech `USAGE` a `CREATE`;
- `app.__ef_migrations_history` vlastní `fuapay_migrator`;
- existují PostgreSQL role `fuapay_app` a `fuapay_migrator`;
- obě jsou LOGIN role, nejsou superuser, nemají `CREATEDB` ani `CREATEROLE`;
- žádná z nich nemá PostgreSQL password uložený v roli;
- mezi FUA Pay rolemi nejsou žádná role memberships;
- staging nemá roli `fuapay_deployer`;
- lokální Unix-socket autentizace používá `peer`; TCP localhost pravidla používají
  `scram-sha-256`.

`fuapay_deployer`, který se objevuje v CI a v generickém příkladu v
`release-artifacts.md`, není staging účet a nesmí se pro staging odvozovat ani
vytvářet. CI používá oddělený testovací model rolí; staging používá výše uvedený
skutečně ověřený model.

Před jakoukoli staging migrací musí preflight ověřit přesnou cílovou databázi a
aktuálního PostgreSQL uživatele. Migration execution artefakt musí zůstat přesně
ten, který byl vytvořen a byte-for-byte ověřen repozitářovým nástrojem s
`SET ROLE "fuapay_migrator";`. Automatické migrace při startu zůstávají vypnuté.

## Připravený deployment `9ecee2d...` — dosud neaktivovaný

Dne 2026-09-15 byl pro přesný commit
`9ecee2d9c57d88a2969d42094e49597b41f1642c` připraven a lokálně ověřen nový
self-contained `linux-x64` release a migration artefakty. `main` CI i CodeQL nad
tímto SHA jsou PASS.

Release artefakt:

- soubor:
  `fuapay-staging-9ecee2d9c57d88a2969d42094e49597b41f1642c-linux-x64.tar.gz`;
- velikost: `123264396` bytes;
- SHA-256:
  `27442F74317623C75C9664851BF04BE4D69BA10A40579AFFEF9B7FA504FCF41D`.

Migration artefakty:

- `fuapay-migrations.sql`;
  SHA-256
  `FD74956E3CB7853C7584CA1DF28F4014FFBDABE00998CC19E1243796DF5A84B8`;
- `fuapay-migrations.execution.sql`;
  SHA-256
  `9AFEC76CB0C55766B4CEA167D4C0408791F79A197C1B2CD4226FCCD51857C778`.

Lokální release/migration verification prošla a server po přenosu znovu ověřil
všechny tři SHA-256. `gzip -t` a úplný tar listing release archivu prošly.
Artefakty jsou dočasně uloženy v soukromém deployment adresáři pod
`/home/rouha`.

Před změnou schema vznikl PostgreSQL custom dump:

`/home/rouha/fuapay_demo-pre-print-20260915T113533Z.dump`

- velikost: `213739` bytes;
- mode: `0600`;
- owner: `rouha:rouha`;
- SHA-256:
  `7679a801a366d52e704b818b05eaa578ee08db5f66266e6d4ea4c93489cb4afb`;
- `pg_restore --list`: PASS.

Staging DB zatím stále obsahuje 19 migrací. Pro commit `9ecee2d...` jsou proti
aktuálnímu staging schema nové přesně dvě forward-only migrace:

1. `20260913162532_AddManualCreditTopUps` — vytváří pouze
   `credits.manual_topup_commands`;
2. `20260914080618_AddPersistentPrintCredentials` — vytváří pouze
   `credits.print_credentials` včetně constraints, FK a filtered unique indexu.

Obě jsou additive; jejich `Up()` nemaže ani nepřepisuje existující finanční
schema. Print feature zůstávají po samotném deploymentu vypnuté, dokud nejsou
později explicitně nakonfigurovány.

## Canonical staging post-activation health rule

Direct Kestrel requests musí obsahovat:

```text
Host: fuapay.tul.cz
X-Forwarded-Proto: https
```

Startup health je bounded, ne okamžitý:

1. retry `/health/ready` do `Healthy` nebo timeoutu;
2. poté poll `/health/workers/csob-reconciliation`;
3. worker `NotStarted` bezprostředně po restartu je warm-up stav, ne důvod k
   rollbacku;
4. worker `Healthy` je PASS;
5. worker `Failed`, `Stale` nebo bounded timeout je FAIL;
6. ověřit, že běžící executable odpovídá aktivnímu release;
7. dokončit canonical/alternate HTTPS smoke.

Aktuální rollback target se musí při každém deploymentu ověřit přímo na serveru.
Nesmí se odvozovat ze starší historické sekce nebo z neprivilegovaného
`readlink -f`, pokud deployment user nemůže projít release adresářem.

## Historie 2026-09-13 až 2026-09-14

PR #44 byl mergnut jako
`bc868276aead1b350033303ce8e80bc6ce9a5894`. Při prvním nasazení prošel
artifact/schema/deployment gate, ale následný `payment/init` opakovaně timeoutoval.
Staging byl proto dočasně vrácen pouze kódem na předchozí release; databázové
schema se nevracelo. Stejný symptom se reprodukoval i na předchozím release a
následná diagnostika prokázala funkční DNS/TCP/TLS cestu, ale neprokázala
konkrétní root cause. Detailní incident evidence zůstává v
[`../integrations/csob-incident-2026-09-13.md`](../integrations/csob-incident-2026-09-13.md).

Po obnovení merchant eAPI byl 2026-09-14 znovu nasazen/ověřen release
`bc868276...` a čerstvý 30min expiry scénář prošel. Detailní acceptance evidence
je v
[`../testing/csob-expiry-acceptance-2026-09-14.md`](../testing/csob-expiry-acceptance-2026-09-14.md).

## Starší deployment evidence

Historické release SHA, artifact hashe a dřívější rollback baseline zůstávají v
Git historii tohoto dokumentu a v příslušných integračních/testovacích
closeoutech. Pro nový deployment se nesmějí používat jako implicitní aktuální
hodnoty.
