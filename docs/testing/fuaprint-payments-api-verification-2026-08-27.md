# Ověření FUA Print Payments API – 2026-08-27

## Rozsah

Výchozí stav byl čistý `main` na merge commitu
`1d612692e9e2453a0f2706b14c3001105b347ce3`. Ověřený finální kódový commit na
větvi `integration/fuaprint-api` je
`22542280199486a94646dcafde6e644ed4164eb8`.

Milestone přidal pouze read-only linked identity resolver, dedikovanou service
autentizaci a interní HTTP boundary nad existujícím `PrintReservationService`.
Finanční model, globální lock order, audit a ledger zůstaly autoritou existující
aplikační služby.

Kódové commity:

1. `2c031ba22bc23a45ef815da4f493436503ff08b1` – `access: add read-only linked identity resolution`;
2. `b7c677a9c31a2710782d0b8f2362b71882a3a63d` – `security: authenticate fua print service requests`;
3. `9d0b83dad2e6246c81b7dff03371cfea97f3db61` – `credits: expose authenticated print payments api`;
4. `b0cf842387884618634cb384612fb68acc0254b9` – `tests: harden print payments integration boundaries`;
5. `22542280199486a94646dcafde6e644ed4164eb8` – `security: harden print payments request perimeter`.

## Automatizované pokrytí

Webová a aplikační suite má proti baseline 40 nových test cases. Ověřuje zejména:

- exact `microsoft-entra/tid/oid` lookup existujícího aktivního Customer bez
  write/JIT/profile nebo role mutation;
- odmítnutí neznámé identity, stejného e-mailu s jiným stable key, blokovaného
  uživatele a uživatele bez Customer role;
- defaultně disabled feature a startup fail-closed pro chybějící, malformed nebo
  duplicitní source/digest konfiguraci;
- chybějící, nesprávný, malformed a příliš dlouhý credential, fixed service
  policy a odvození source ID z credentialu;
- odmítnutí klientského `printSourceId` v JSON i query, neznámých JSON polí,
  invalidních business vstupů a malformed nebo chunked nadlimitního body;
- stabilní ProblemDetails kódy a nepřítomnost credentialu v response;
- source-scoped aplikační recovery lookup.

Šest nových PostgreSQL API test cases používá skutečný
`WebApplicationFactory`, Access repository, `PrintReservationService`, EF
repository, transakce a audit. Prokazují:

- kompletní reserve → recovery → ResolutionRequired → capture i samostatný
  release lifecycle s durable stavem;
- reserve replay bez druhé rezervace nebo auditu, capture replay bez druhého
  debitu/auditu a release replay bez movementu;
- deterministický reserve command a print-job konflikt;
- neznámou identitu i shodu e-mailu bez JIT uživatele, účtu nebo rezervace;
- blocked a not-eligible uživatele bez finanční mutation;
- insufficient available credit bez rezervace a auditu;
- nemožnost source B číst nebo mutovat rezervaci source A.

Existující PostgreSQL rollback, race a lifecycle testy zůstávají součástí stejné
canonical suite. Celkem prošlo 817 lokálních automatizovaných testů: 647 webových
a aplikačních plus 170 PostgreSQL, bez skipped testů.

## Finální gate

Na nové prázdné loopback databázi `fuapay_test_api_20260827_1`, vlastněné
nesuperuser rolí `fuapay_app`, byl úspěšně aplikován celý řetězec 14 existujících
EF migrací od `20260725170405_InitialCreditsSchema` po
`20260826161935_EnforcePrintReservationLifecycle`.

| Kontrola | Výsledek |
|---|---|
| `dotnet tool restore` | PASS – `dotnet-ef` 10.0.4 |
| solution locked restore | PASS |
| Release build | PASS – 0 warningů, 0 chyb |
| `dotnet format --verify-no-changes` | PASS |
| Webové a aplikační testy | PASS – 647/647, 0 skipped |
| EF pending model changes | PASS – žádné |
| PostgreSQL integrační testy | PASS – 170/170, 0 skipped |
| NuGet vulnerability audit | PASS – 0 známých zranitelností |
| locked restore `linux-x64` | PASS |
| self-contained publish `linux-x64` | PASS – 4 povinné artefakty ověřeny |
| `git diff --check` | PASS |

Živé ČSOB sandbox volání nebylo součástí milestone ani gate.

## Finální self-review

Samostatné read-only review porovnalo celý diff proti baseline a hledalo auth
bypass, source spoofing/isolation, token leakage, timing-unsafe compare, JIT nebo
e-mailové párování, profilové mutation, broad exception catch, chybnou 2xx/4xx
semantiku, duplicitní finanční efekt, paralelní ledger, lock-order regresi,
nežádoucí migraci či dependency a CUPS/FUA Print leakage.

Review našlo dvě perimeter hardening položky: body limit závislý na
`Content-Length` nepokrýval chunked request a globální rate-limit partition
zbytečně sdílela kvótu všem klientům. Commit `2254228` zavedl streamově vynucený
8KiB limit, test chunked body, přesnou validaci lookup query a rate-limit
partition podle vzdálené IP pouze pro throttling. IP se nestala autentizačním
faktorem. Následný cílený i plný gate prošel; další nález ve schváleném scope
nezůstal.

## Migrace, závislosti a záměrně odložený scope

Milestone nepřidal migraci, tabulku, package dependency, druhou databázi,
paralelní ledger ani nový obecný framework. Committed konfigurace neobsahuje raw
credential a feature je defaultně disabled.

Záměrně se neměnil existující webový Entra login ani ČSOB. Nevznikl CUPS klient,
broker, tiskový daemon, řízení tiskárny, FUA Print journal, PC/AD integrace,
refund/reverse, UI, deployment ani další milestone.
