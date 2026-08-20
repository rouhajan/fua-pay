# Ověření GitHub repository hardening – 2026-08-20

## Rozsah

GitHub repository hardening byl připraven na větvi
`security/github-repository-hardening` nad ověřeným `main` commitem
`36a7390e1c493005eba29025d257ae3b6b201acc`.

První implementační commit:

`f211b307abe4569c6ef0494ec67be9ebc737ba1e`
(`ci: add GitHub repository security checks`)

zavedl:

- GitHub Actions CI workflow;
- CodeQL workflow;
- Dependabot konfiguraci pro NuGet, .NET SDK a GitHub Actions;
- plné SHA piny použitých GitHub Actions;
- `persist-credentials: false` pro checkout;
- minimální explicitní GitHub token permissions.

Commit neobsahoval změny aplikačního kódu.

## První skutečný GitHub běh

První push commitu `f211b30` spustil:

- CI run `32380496574`;
- CodeQL run `32380496561`.

Běhy byly záměrně ponechány v historii jako součást ověřovací stopy.

CI úspěšně připravilo PostgreSQL 18.4, provedlo locked restore, Release build
bez warningů a chyb a kontrolu formátování. Webová testovací sada následně
skončila 544/545, protože globální nastavení prostředí `Development` v CI
změnilo validaci DI uvnitř jednoho izolovaného testovacího
`WebApplicationBuilder`.

CodeQL C# část úspěšně provedla inicializaci a build. JavaScript/TypeScript
část skončila konfigurační chybou, protože po vyloučení vendored knihoven
repozitář neobsahuje vlastní JavaScript/TypeScript zdrojový kód.

Tyto výsledky nebyly interpretovány jako chyba aplikačního kódu. Vedly k
samostatné opravě workflow.

## Opravný commit

Commit

`59f476a564161728fac4ff6072c8a43297d4e265`
(`fix: align GitHub checks with repository`)

provedl pouze tyto opravy:

- CodeQL nyní analyzuje C# a GitHub Actions místo neexistujícího vlastního
  JavaScript/TypeScript kódu;
- odstranil již nepotřebnou JavaScript CodeQL path konfiguraci;
- odstranil globální změnu aplikačního prostředí z CI;
- databázový connection string a povolení destruktivních DB testů jsou
  omezeny pouze na databázové kroky;
- PostgreSQL healthcheck explicitně používá uživatele a databázi `postgres`.

## Finální GitHub ověření

Commit `59f476a564161728fac4ff6072c8a43297d4e265` byl ověřen skutečnými
GitHub Actions běhy:

- CI run `32381585690`;
- CodeQL run `32381585583`.

### CI

| Kontrola | Výsledek |
|---|---|
| PostgreSQL 18.4 service | PASS |
| Omezený aplikační DB účet | PASS |
| Locked restore | PASS |
| Release build | PASS – 0 warningů, 0 chyb |
| `dotnet format --verify-no-changes` | PASS |
| Webové a aplikační testy | PASS – 545/545 |
| EF Core pending model changes | PASS – žádné |
| EF Core migrace na čerstvou izolovanou DB | PASS – 12/12 |
| PostgreSQL integrační testy | PASS – 94/94 |
| Známé NuGet zranitelnosti včetně tranzitivních | PASS – 0 |
| Locked restore pro `linux-x64` | PASS |
| Self-contained Release publish pro `linux-x64` | PASS |
| Kontrola čistoty pracovního stromu | PASS |

Databázové testy běžely pouze proti izolované CI databázi
`fuapay_test_ci`. Connection string a opt-in proměnná pro destruktivní DB
testy byly nastaveny pouze v databázových krocích workflow.

### CodeQL

| Analýza | Výsledek |
|---|---|
| C# | PASS |
| GitHub Actions | PASS |

C# analýza používá manual build mode a skutečný Release build řešení.
GitHub Actions jsou analyzovány jako samostatný podporovaný CodeQL jazyk.

## Výsledek

Automatické repository checks zavedené touto větví jsou pro commit
`59f476a564161728fac4ff6072c8a43297d4e265` ověřeny jako PASS.

Tento záznam neuzavírá celý GitHub governance scope. Zejména nepředstavuje
důkaz konfigurace branch protection/ruleset, Private Vulnerability Reporting
ani samostatného nastavení Dependabot security updates. Dependabot
konfigurace je součástí této větve, ale její provoz po začlenění do default
branch zatím tímto záznamem není prohlášen za ověřený.

Tento záznam rovněž nepředstavuje kompletní bezpečnostní audit aplikace podle
OWASP ASVS.
