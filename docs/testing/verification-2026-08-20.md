# Ověření FUA Pay – security servicing 2026-08-20

## Rozsah

Ověření se vztahuje ke commitu
`f474be9ea1da39666e44dfd4e072ac1de3ecde4b` na větvi
`security/august-2026-servicing`. Jeho rodičem je
`78052af1d2ea083200eb53065d9ae9d42c64113e`.

Servicing změnil pouze:

- .NET SDK `10.0.302` → `10.0.303`;
- `Microsoft.AspNetCore.Authentication.OpenIdConnect` `10.0.10` → `10.0.11`;
- `Microsoft.AspNetCore.Mvc.Testing` `10.0.10` → `10.0.11`;
- odpovídající NuGet lock files.

Ostatní přímé balíčky zůstaly beze změny. Audit lock files neprokázal
přidání ani odebrání balíčku ani jiný version jump než odpovídající
servicing `10.0.10` → `10.0.11`.

## Ověřovací výsledky

| Kontrola | Výsledek |
|---|---|
| Release build | PASS – 0 upozornění, 0 chyb |
| `dotnet format --verify-no-changes` | PASS |
| Webové a aplikační testy | PASS – 545/545 |
| Čerstvá izolovaná PostgreSQL databáze | PASS – PostgreSQL 18.4 |
| EF Core migrace na prázdné databázi | PASS – 12/12 |
| PostgreSQL integrační testy | PASS – 94/94 |
| EF Core – nepromítnuté změny modelu | PASS – žádné |
| Známé zranitelnosti NuGet včetně tranzitivních závislostí | PASS – 0 ve všech čtyřech projektech |
| Locked restore pro `linux-x64` | PASS |
| Self-contained Release publish pro `linux-x64` | PASS |
| Publikovaný .NET runtime | PASS – 10.0.11 |
| Publikovaný OpenID Connect balíček | PASS – 10.0.11 |
| PDFsharp v publikovaném artefaktu | PASS – 6.2.4 |
| `git diff --check` | PASS |
| Finální audit rozsahu commitu | PASS – přesně 6 očekávaných souborů |

NuGet audit byl proveden proti `https://api.nuget.org/v3/index.json` pomocí
kontroly známých zranitelností včetně tranzitivních závislostí.

## Izolovaná databázová kontrola

PostgreSQL integrační gate neběžel proti lokální vývojové databázi
`fuapay_dev`.

Pro ověření vznikl samostatný dočasný PostgreSQL 18.4 cluster navázaný pouze
na loopback rozhraní a volný lokální port. V něm byla vytvořena nová prázdná
databáze `fuapay_audit_*`, aplikováno všech 12 EF Core migrací a spuštěno
94 integračních testů.

Po úspěšném běhu byl dočasný PostgreSQL server zastaven a jeho cluster
odstraněn. Stávající PostgreSQL služba na portu 5432 nebyla tímto gate
upravena.

Surové ověřovací výstupy jsou záměrně uchovány mimo Git repozitář.

## Výsledek

Security servicing pro .NET 10.0.11 je pro uvedený commit ověřen jako PASS.

Tento záznam nepředstavuje kompletní bezpečnostní audit aplikace podle OWASP
ASVS ani uzavření GitHub repository governance. Tyto kontroly jsou samostatné
navazující kroky a jejich výsledky budou dokumentovány až po skutečném
provedení.
