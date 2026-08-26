# Ověření lifecycle rezervací tisku – 2026-08-26

## Rozsah a audit

Ověřený finální kódový commit je
`d5bb7b3e72dc7582a8938d7acd424bd62fa70cd3` na větvi
`integration/fuaprint-payments`.

Audit potvrdil, že `IApplicationTransaction` drží jednu PostgreSQL transakci
i přes vnořená aplikační volání. Repository mohou uvnitř provést dílčí
`SaveChanges`, ale durable stav vznikne až vnějším commitem. Kreditní mutace už
před změnou účtu používaly account row lock a existující auditní vzor ukládal
`IAuditTrail.Stage()` společně s následujícím `SaveChanges`.

Existující reserve/available základ odpovídal finančním invariantům. Nalezená
chyba byla v DB constraintu: stav `ResolutionRequired` nevyžadoval
`resolution_command_id` a nezakazoval `terminal_command_id`. Oprava je součástí
nové forward-only migrace
`20260826161935_EnforcePrintReservationLifecycle`. Jiný rozpor v dosavadním
reservation základu audit neprokázal.

## Implementovaný stav

Všechny lifecycle operace používají pořadí:

`account FOR UPDATE → reservation FOR UPDATE → movement/reservation změny → audit → commit`.

- `Reserve` nemění booked balance a blokuje pouze dostupný kredit.
- `ResolutionRequired` je durable, dál blokuje, nevytváří movement a ukládá
  `resolutionCommandId`.
- `Capture` vytvoří interní globálně unikátní `debitOperationId`, odečte přesně
  částku vlastní zamknuté rezervace, vytvoří právě jeden debit a přepne rezervaci
  na `Captured` ve stejné transakci. Běžný `CreditService.DebitAsync` se pro
  capture nepoužívá.
- `Release` nevytváří movement ani nemění booked balance; pouze přepne rezervaci
  na `Released` a odstraní její blocking efekt.
- `Captured` a `Released` jsou neměnné terminální stavy.

Replay stejného command ID se stejnou rezervací a operací vrací uložený výsledek
bez druhého finančního nebo auditního efektu. Použití command ID pro jinou
rezervaci či jinou terminální operaci končí deterministickým konfliktem.
Unikátní indexy a překlad souběžného unique race tvoří druhou ochrannou vrstvu.

Procesní actor `fua-print-payments` zapisuje ve stejné DB transakci akce
`print-reservation.resolution-required`, `print-reservation.captured` a
`print-reservation.released`. Audit obsahuje reservation ID, print job UUID,
částku, výsledný stav a u capture také debit operation ID.

## Ověření

| Kontrola | Výsledek |
|---|---|
| `scripts/verify.ps1` | PASS |
| Release build | PASS – 0 warningů, 0 chyb |
| Formátování | PASS |
| Webové a aplikační testy | PASS – 607/607, 0 skipped |
| EF pending model changes | PASS – žádné |
| PostgreSQL integrační testy | PASS – 160/160, 0 skipped |
| NuGet vulnerability audit | PASS – 0 známých zranitelností |
| Locked restore `linux-x64` | PASS |
| Self-contained publish `linux-x64` | PASS |
| `git diff --check` | PASS |

PostgreSQL testy proběhly se safety opt-in proti izolované loopback databázi
`fuapay_test_e178cdf`. Řízené race testy bez `Sleep` pokryly capture/capture,
capture/release, resolution proti capture/release, běžný debit proti capture i
release a cross-account konflikt stejného lifecycle command ID.

Celkem prošlo 767 lokálních automatizovaných testů v canonical webové a
PostgreSQL suite. Živé ČSOB sandbox testy nebyly součástí tohoto milestone.

## Migrace, závislosti a hranice

Vznikla jedna nutná constraint migrace uvedená výše. Nevznikla nová balíčková
závislost, databáze, ledger, worker ani framework.

Záměrně nebyly implementovány HTTP PrintPayments API, FUA Print/CUPS klient,
Entra S2S, broker, expiry, background reconciliation, refund/reklamace, ČSOB
změny ani UI. Staging deployment a merge do `main` nebyly provedeny.

V implementovaném scope po provedeném gate není známá otevřená mezera. Uvedené
externí integrační a provozní části zůstávají samostatnými budoucími milníky.
