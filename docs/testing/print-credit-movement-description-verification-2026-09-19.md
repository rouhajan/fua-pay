# Ověření zákaznického popisu tiskového debit pohybu – 2026-09-19

## Rozsah

Výchozí `main` před změnou byl
`826e80470af1937f1b0a2910e44f37bc323ecaf8`.

Feature větev je `fix/print-credit-movement-description`.

Změna řeší pouze zákaznickou prezentaci již existujícího kreditního debit
pohybu vzniklého při úspěšném FUA Print Capture. Finanční, databázová ani
rezervační semantika se nemění.

Před změnou platilo:

- `PrintReservationService` ukládal do ledger movement popis ve formátu
  `Capture print reservation <GUID>`;
- Customer stránka `/Customer/Credit` zobrazovala raw
  `movement.Description`;
- dashboard zobrazoval každý debit jako `Úhrada zakázky`.

Po změně Customer UI rozpozná pouze skutečný print-capture debit a zobrazí jej
jako `Úhrada tisku`.

## Implementační hranice

Rozpoznání je záměrně úzké. Movement se považuje za tiskový pouze pokud:

1. jde o `CreditMovementType.Debit`;
2. description začíná přesně `Capture print reservation `;
3. zbytek description je platný GUID ve standardním formátu `D`.

Pokud některá z podmínek neplatí, tiskový label se nepoužije.

Customer credit history používá společný prezentační helper místo raw
`movement.Description`. Dashboard používá stejnou detekci.

Záměrně se nemění:

- `PrintReservationService`;
- uložený ledger description;
- debit operation ID;
- Reserve / Capture / Release lifecycle;
- výpočet částky nebo zůstatku;
- databázové schéma nebo EF model;
- audit trail;
- FUA Print API;
- Admin credit history.

Technická reference tedy zůstává v uložených datech a interních diagnostických
cestách. Customer UI ji pouze nepředkládá zákazníkovi.

Tím se lidský label použije i pro již existující historický print-capture
movement bez přepisování finanční historie.

## Automatizované ověření

Nejprve byl přidán cílený test nad původní implementací.

Na commitu
`1e386f2378f58e802986790a3667f5720ebfc703`
selhal právě nový test
`MovementTitle_PrintCaptureUsesPrintLabel`:

- očekáváno: `Úhrada tisku`;
- skutečnost: `Úhrada zakázky`;
- targeted suite: 10 testů, 9 PASS, 1 FAIL.

Tím byl doložen RED stav před implementací.

Po implementaci na commitu
`31fa6e6564dcf5d6124ca32d2e79b3661f514242`
prošel stejný cílený suite:

- `DashboardDisplayTests`: 14/14 PASS.

Přidané testy pokrývají:

- validní print-capture debit -> `Úhrada tisku`;
- Customer credit description validního print-capture -> `Úhrada tisku`;
- netiskový credit zachová původní description;
- neplatný print-like text bez validního GUID se nepřeznačí;
- credit s print-like description se nepřeznačí jako tisk.

## Full application gate

Na commitu
`31fa6e6564dcf5d6124ca32d2e79b3661f514242`
byl lokálně spuštěn kanonický aplikační gate:

```powershell
./scripts/verify.ps1
```

Výsledek:

| Kontrola | Výsledek |
|---|---|
| Locked restore | PASS |
| Release build | PASS |
| Kontrola formátování | PASS |
| Webové a aplikační testy | PASS – 1030/1030 |
| EF pending model changes | PASS – žádné |
| `git status` po gate | PASS – working tree clean |
| `git diff --check origin/main..HEAD` | PASS |

Lokální gate záměrně neběžel s `-RunDatabaseTests` ani
`-RunCsobSandboxTests`, protože tato změna nemění databázi ani ČSOB integraci.
GitHub CI nad pull requestem musí ještě projít svým standardním plným gatem.

## Staging deployment a Customer smoke

Změna byla 2026-09-19 nasazena na staging v release
`cc142e23a200e72831284605d8553962b160a984`.

Po aktivaci prošly readiness, ČSOB reconciliation worker, kontrola skutečně
běžícího executable, migration count a veřejný HTTPS/canonical smoke.

Následný Customer smoke nad skutečným již existujícím placeným print-capture
pohybem za 18 Kč potvrdil:

- dashboard zobrazuje `Úhrada tisku`;
- `/Customer/Credit` zobrazuje `Úhrada tisku`;
- technický `Capture print reservation <GUID>` se v Customer UI nezobrazuje;
- částka zůstala -18 Kč a zůstatek se touto prezentační změnou nezměnil.

Tím je staging acceptance této změny PASS. Tento dokument stále neprokazuje
budoucí čistý production cutover.
