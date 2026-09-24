# CodeQL pagination XSS verification - 2026-09-24

## Scope

Ověření dvou otevřených GitHub CodeQL nálezů:

- code scanning alert #2;
- code scanning alert #3;
- rule: `cs/web/xss`;
- severity: High;
- page: `src/FuaPay.Web/Pages/Admin/Payments/Index.cshtml`.

Oba nálezy představují stejný datový tok pro dvě větve stránkování
`Předchozí` a `Další`.

Production ani staging nebyly při tomto ověření použity nebo změněny.

## CodeQL data flow

CodeQL sleduje uživatelský GET parametr `search` z:

`IndexModel.OnGetAsync(..., string? search, ...)`

přes:

`Search = string.IsNullOrWhiteSpace(search) ? null : search.Trim();`

do Razor route hodnoty:

`asp-route-search="@Model.Search"`

na odkazech stránkování.

`Trim()` není bezpečnostní sanitizace, proto bylo nutné ověřit skutečný
výstup Razor/Anchor Tag Helper runtime.

Stránka vyžaduje roli `Admin`. Pokud by zde skutečné reflected XSS existovalo,
ke spuštění by došlo v browseru autentizovaného administrátora, například po
otevření útočníkem připravené URL. Útočník by sám nemusel mít roli Admin.

## Runtime verification

Do
`tests/FuaPay.Web.Tests/Pages/AdminPaymentReturnRenderingTests.cs`
byl přidán test:

`PaginationSearch_DoesNotRenderExecutableHtml`

Test vykreslí skutečnou Razor stránku s autentizovanou Admin session a
uživatelským payloadem:

`"><script>alert(1)</script>`

Současně vynutí zobrazení obou pagination odkazů.

Ověřuje, že:

- výsledné HTML neobsahuje spustitelný `<script>alert(1)</script>`;
- payload nemůže ukončit atribut sekvencí `"><script>`;
- route hodnota je URL-encoded;
- encoded payload je přítomen právě ve dvou pagination odkazech.

Targeted runtime test:

- total: 1
- passed: 1
- failed: 0
- skipped: 0

Result: **PASS**

## Interpretation

CodeQL správně identifikoval tok uživatelské hodnoty z HTTP parametru do
pagination route hodnoty.

Skutečný ASP.NET Core Razor Anchor Tag Helper však tuto route hodnotu při
generování odkazu bezpečně zakóduje. Ověřený payload proto nevytváří
spustitelný HTML nebo JavaScript obsah.

Pro současnou implementaci jsou alerty #2 a #3 vyhodnoceny jako false positive
statické analýzy nad bezpečně zakódovaným runtime výstupem.

Produkční aplikační kód nebyl kvůli těmto nálezům změněn. Regression test
zůstává jako automatická evidence bezpečnostní vlastnosti.

## Full local verification

Po targeted testu prošel standardní repository verification gate:

- Release build: PASS
- formatting verification: PASS
- web/application tests: `1093/1093` PASS
- EF model pending changes: none
- `git diff --check`: PASS

PostgreSQL integrační testy nebyly znovu spouštěny, protože změna nezasahuje
aplikační ani persistence logiku.

Production a staging zůstaly beze změny.

## Merge a closeout 2026-09-24

[PR #77](https://github.com/rouhajan/fua-pay/pull/77) byl mergnutý z commitu
`f007375120a4876687322f83f5aaa3f5f53f4a78` do `main` merge commitem
`4497133577b004b0f64f84116f9cc2706cd578cd`. Diff proti předchozímu `main`
`8e45f248f295d1d9af665e3214dfce2ab2eef399` obsahoval pouze tento evidenční
soubor a `AdminPaymentReturnRenderingTests.cs`.

Post-merge gate na přesném `4497133577b004b0f64f84116f9cc2706cd578cd`:

- [CI #402](https://github.com/rouhajan/fua-pay/actions/runs/35996673068):
  `SUCCESS`, včetně PostgreSQL integračních testů, migračního gate,
  NuGet auditu, linux-x64 publish, ověření release archivu a čistoty runneru.
- [CodeQL #407](https://github.com/rouhajan/fua-pay/actions/runs/35996673119):
  `SUCCESS` pro C#, JavaScript/TypeScript a Actions.

Úspěšný CodeQL workflow není sám o sobě důkazem uzavření jednotlivých alertů.
Stav `Dismissed` u #2 i #3 potvrdil provozovatel v GitHub UI; konkrétní
metadata dismissalů nebyla nezávisle načtena přes bezpečnostní API.
U #3 provozovatel uvedl komentář `fixed`. Tento komentář nemění technický
závěr výše: jde o false positive doložený testem, nikoli o opravu produkčního
aplikačního kódu. Původní analýza a její výsledky zůstávají zachované.

Provozovatel dodal úspěšný výstup kontrolovaného fast-forwardu místního `main`
na merge commit a následného odstranění pouze větve
`test/codeql-xss-runtime-proof` místně i vzdáleně. Konečný místní výpis
potvrdil čistý pracovní strom. GitHub read-only kontrola nezávisle potvrdila
nezměněný `main` a nepřítomnost této vzdálené větve.

Closeout neprováděl deployment, změnu konfigurace, migraci ani živý bankovní
test. Nejde o nové ověření aktuálního běžícího stavu Production či stagingu.
