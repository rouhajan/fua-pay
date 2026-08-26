# Ověření lifecycle rezervací tisku – 2026-08-26

## Rozsah a audit

Ověřený finální kódový commit je
`b9c8171f4c0d6116655402640e8c3164314c7343` na větvi
`integration/fuaprint-payments`.

Audit potvrdil, že `IApplicationTransaction` drží jednu PostgreSQL transakci
i přes vnořená aplikační volání. Repository mohou uvnitř provést dílčí
`SaveChanges`, ale durable stav vznikne až vnějším commitem. Kreditní mutace už
před změnou účtu používaly account row lock a existující auditní vzor ukládal
`IAuditTrail.Stage()` společně s následujícím `SaveChanges`.

Navazující nezávislé review našlo dvě mezery: nový `Reserve` nestageoval audit a
nebyly explicitně ověřeny rollbacky finančních transakčních hranic. `Reserve`
nyní stageuje `AuditEntry.ForProcess` před repository `AddAsync`; uložení
rezervace a auditu proto dokončí až stejný vnější commit. Replay existujícího
reserve commandu se vrací před stage a druhý audit nevytvoří.

Dřívější lifecycle review opravilo DB constraint stavu `ResolutionRequired`
forward-only migrací `20260826161935_EnforcePrintReservationLifecycle`. Tento
navazující review-fix model ani migrace nemění.

## Implementovaný stav

`Reserve` používá pořadí:

`account FOR UPDATE → kontrola dostupnosti → reservation + audit → commit`.

Změny existující rezervace používají pořadí:

`account FOR UPDATE → reservation FOR UPDATE → movement/reservation změny → audit → commit`.

- `Reserve` nemění booked balance, blokuje pouze dostupný kredit a atomicky
  ukládá audit `print-reservation.reserved`.
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
`print-reservation.reserved`, `print-reservation.resolution-required`,
`print-reservation.captured` a `print-reservation.released`. Audit obsahuje
reservation ID, print job UUID, částku, výsledný stav a u capture také debit
operation ID.

## Ověření

| Kontrola | Výsledek |
|---|---|
| `scripts/verify.ps1` | PASS |
| Release build | PASS – 0 warningů, 0 chyb |
| Formátování | PASS |
| Webové a aplikační testy | PASS – 607/607, 0 skipped |
| EF pending model changes | PASS – žádné |
| PostgreSQL integrační testy | PASS – 164/164, 0 skipped |
| NuGet vulnerability audit | PASS – 0 známých zranitelností |
| Locked restore `linux-x64` | PASS |
| Self-contained publish `linux-x64` | PASS |
| `git diff --check` | PASS |

PostgreSQL testy proběhly se safety opt-in proti izolované loopback databázi
`fuapay_test_e178cdf`. Řízené race testy bez `Sleep` pokryly capture/capture,
capture/release, resolution proti capture/release, běžný debit proti capture i
release a cross-account konflikt stejného lifecycle command ID.

Nové persistence testy explicitně prokázaly:

- selhání reserve auditu rollbackne vznik rezervace, blocking částku i audit;
- selhání credit/movement save při capture nezanechá debit, změnu balance ani
  capture audit a rezervace dál blokuje;
- selhání reservation save po předchozím account/movement `SaveChanges`
  rollbackne celou vnější transakci, včetně debitu, balance a capture auditu;
- selhání capture auditu nezanechá žádný částečný finanční stav.

Celkem prošlo 771 lokálních automatizovaných testů v canonical webové a
PostgreSQL suite. Živé ČSOB sandbox testy nebyly součástí tohoto milestone.

## Migrace, závislosti a hranice

Dřívější lifecycle commit obsahuje jednu nutnou constraint migraci uvedenou
výše. Tento review-fix nepřidal migraci ani balíčkovou závislost, databázi,
ledger, worker či framework.

`debit_operation_id` záměrně nemá FK na
`credits.movements.operation_id`: ledger má `OperationId` jako unique index,
nikoli jako principal/alternate key. Současnou vazbu zajišťuje atomický capture
ve stejné transakci a interně generované globálně unikátní `debitOperationId`;
to zůstává záměrným řešením tohoto milestone.

Záměrně nebyly implementovány HTTP PrintPayments API, FUA Print/CUPS klient,
Entra S2S, broker, expiry, background reconciliation, refund/reklamace, ČSOB
změny ani UI. Staging deployment a merge do `main` nebyly provedeny.

V implementovaném scope po provedeném gate není známá otevřená mezera. Uvedené
externí integrační a provozní části zůstávají samostatnými budoucími milníky.
