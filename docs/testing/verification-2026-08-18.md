# Ověření FUA Pay – 2026-08-18

Ověřený výchozí stav:

- větev: `main`
- commit: `46d5423`
- pracovní strom před ověřením: čistý

## Výsledky

| Kontrola | Výsledek |
|---|---|
| Release build | PASS – 0 upozornění, 0 chyb |
| Webové a aplikační testy | PASS – 510/510 |
| PostgreSQL integrační testy | PASS – 94/94 |
| EF Core – nepromítnuté změny modelu | PASS – žádné |
| `dotnet format --verify-no-changes` | PASS |
| Známé zranitelnosti NuGet včetně tranzitivních závislostí | PASS – 0 |
| Zastaralé produkční balíčky | PASS – 0 |
| DevSkim – aplikační zdrojový kód | PASS – 0 Critical / Important / Moderate |
| DevSkim – celý repozitář | PASS s jedním akceptovaným nálezem pouze v testu |
| Playwright – autentizovaný navigační smoke test | PASS – 56/56 navigací |
| Runtime kontrola autorizace rolí | PASS – 25/25 platných kontrol |
| Bezpečnostní vlastnosti cookies | PASS – Secure / HttpOnly, nastavené SameSite |
| Základní HTTP bezpečnostní hlavičky | PASS |

## Poznámky

- Jediný nález DevSkim v celém repozitáři je záměrně nezabezpečené HTTP URI v negativním unit testu, který ověřuje jeho odmítnutí.
- Jeden rate-limit webový test při společném ověřovacím běhu jednou selhal. Při následném samostatném opakování prošel 5/5 běhů. Reprodukovatelná chyba aplikace nebyla prokázána.
- Runtime diagnostika ve vývojovém prostředí zachytila dva neblokující body určené k případnému pozdějšímu cílenému prověření:
  - při jednom diagnostickém běhu bylo zaznamenáno nestandardní chování `/FuaPay.Web.styles.css`; po čistém sestavení a spuštění odpovídal vzhled aplikace produkci, proto nejde o potvrzenou produktovou závadu,
  - CSP blokuje frameworkem generovaný inline atribut `style="display:none"` ve validačním souhrnu vývojové přihlašovací stránky.
- Tyto body se nesmí řešit plošnými výjimkami autorizace, povolením `unsafe-inline` ani vizuálními záplatami. Případná oprava musí nejprve určit skutečnou příčinu, použít nejmenší správný zásah, doplnit cílený regresní test a projít relevantními ověřovacími kontrolami.
- Surové výstupy ověřování jsou záměrně uchovány mimo Git repozitář.
