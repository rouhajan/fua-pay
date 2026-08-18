# Ověření FUA Pay – 2026-08-18

## Širší ověřovací baseline

Ověřený výchozí stav před následnými UI změnami:

- větev: `main`
- commit: `46d5423`
- pracovní strom před ověřením: čistý

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

## Finální release gate po UI změnách

Po dokončení mobilních UI úprav a mechanického rozdělení `site.css` byl před integrací a nasazením ověřen commit `986be97d44adb417872babf65d212c16080eed65`:

- větev před integrací: `ui/mobile-ux`; následně fast-forward do `main`,
- `main` a `origin/main` po pushi ukazují na stejný commit,
- pracovní strom: čistý,
- Release build: PASS,
- webové a aplikační testy: PASS – 515/515,
- PostgreSQL integrační testy: PASS – 94/94,
- čerstvá izolovaná testovací databáze: 12/12 aplikovaných EF Core migrací,
- EF Core – nepromítnuté změny modelu: PASS,
- `dotnet format --verify-no-changes`: PASS,
- `git diff --check`: PASS,
- finální Git audit vůči `main`: PASS.

Rozdíl 510 → 515 webových testů vznikl tím, že původní kontrola jednoho statického `/css/site.css` byla po mechanickém rozdělení stylesheetu nahrazena kontrolami skutečných CSS assetů.

Širší bezpečnostní a prohlížečové kontroly z předchozí sekce nebyly po čistě UI/CSS změnách vydávány za znovu spuštěné; jejich výsledek se vztahuje k uvedenému baseline commitu `46d5423`.

## Deployment smoke

Commit `986be97d44adb417872babf65d212c16080eed65` byl 2026-08-18 nasazen na demo/staging `https://fuapay.tul.cz` jako `/opt/fuapay/releases/986be97d44ad`.

Po nasazení:

- Kestrel `/health/live`: HTTP 200,
- Kestrel `/health/ready`: HTTP 200,
- nové `base.css` a `responsive.css`: HTTP 200 přímo z Kestrelu,
- běžící executable i working directory odpovídaly novému release,
- veřejný Nginx vracel bez demo přihlášení očekávané HTTP 401 s Basic Auth realm `FUA Pay demo`,
- vizuální smoke na desktopu a mobilu: PASS,
- uploadované deployment archivy a dočasné cesty byly odstraněny.

## Poznámky

- Jediný nález DevSkim v celém repozitáři je záměrně nezabezpečené HTTP URI v negativním unit testu, který ověřuje jeho odmítnutí.
- Jeden rate-limit webový test při společném ověřovacím běhu jednou selhal. Při následném samostatném opakování prošel 5/5 běhů. Reprodukovatelná chyba aplikace nebyla prokázána.
- Runtime diagnostika ve vývojovém prostředí zachytila dva neblokující body určené k případnému pozdějšímu cílenému prověření:
  - při jednom diagnostickém běhu bylo zaznamenáno nestandardní chování `/FuaPay.Web.styles.css`; po čistém sestavení a spuštění odpovídal vzhled aplikace produkci, proto nejde o potvrzenou produktovou závadu,
  - CSP blokuje frameworkem generovaný inline atribut `style="display:none"` ve validačním souhrnu vývojové přihlašovací stránky.
- Tyto body se nesmí řešit plošnými výjimkami autorizace, povolením `unsafe-inline` ani vizuálními záplatami. Případná oprava musí nejprve určit skutečnou příčinu, použít nejmenší správný zásah, doplnit cílený regresní test a projít relevantními ověřovacími kontrolami.
- Surové výstupy ověřování jsou záměrně uchovány mimo Git repozitář.
