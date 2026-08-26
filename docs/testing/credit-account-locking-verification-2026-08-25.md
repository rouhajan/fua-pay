# Ověření serializace kreditního účtu – 2026-08-25

## Rozsah

Ověření se vztahuje ke commitu
`caaecb68626a179d9f8f48b0b57ead408ad567f9` na větvi
`integration/fuaprint-payments`. Jeho rodičem je
`559468cbb8f551c54df7613fe9e146eb22fcf7e0`.

Commit změnil pouze serializaci mutací kreditního účtu:

- `CreditAsync` a `DebitAsync` běží přes existující
  `IApplicationTransaction`;
- mutace existujícího kreditního účtu se serializují pomocí PostgreSQL
  `SELECT ... FOR UPDATE`;
- lock je získán před načtením agregátu používaného pro finanční mutaci;
- lazy vytvoření chybějícího účtu koordinuje transaction-scoped PostgreSQL
  advisory lock odvozený z `ownerId`;
- po získání advisory locku se existence účtu znovu ověří;
- optimistic `Version` kontrola zůstala zachována;
- nevznikla změna schématu ani migrace.

Změna je finanční concurrency základ pro navazující práci. Sama
neimplementuje reservations ani další části integrace FUA Print.

## Ověřovací výsledky

| Kontrola | Výsledek |
|---|---|
| Release build | PASS – 0 upozornění, 0 chyb |
| Formátování | PASS |
| Webové a aplikační testy | PASS – 553/553 |
| EF model check | PASS – žádné pending model changes |
| PostgreSQL integrační testy | PASS – 104/104 |
| `git diff --check` | PASS |

Prošlo všech pět nových PostgreSQL concurrency scénářů:

1. Credit/Credit nad existujícím účtem;
2. Credit/Debit nad existujícím účtem;
3. Debit/Debit s dostatečným zůstatkem;
4. dva `CreditAsync` nad dosud neexistujícím účtem vytvoří právě jeden
   účet;
5. dva souběžné debity, které nemohou oba projít, nezpůsobí overdraw ani
   lost update.

Regresní persistence testy rovněž prošly:

- payment settlement: 6/6 PASS;
- credit job payment: 2/2 PASS;
- financial command idempotency: 6/6 PASS.

## Timeout-after-commit test

Po zavedení vnořeného `IApplicationTransaction` do `CreditService` odhalil
existující failure-injection test nepřesnost svého test doublova. Produkční
`EfApplicationTransaction` kvůli tomu nebyl změněn.

Testovací `ThrowAfterCommitApplicationTransaction` byl upraven tak, aby
simuloval timeout pouze po skutečném outer commit boundary, nikoli po
vnořeném `ExecuteAsync`, které již běží uvnitř aktivní databázové transakce.

Finální timeout-after-commit testy: 2/2 PASS.

## PostgreSQL prostředí

Integrační gate proběhl proti PostgreSQL 18 na loopback rozhraní. Použita
byla existující testovací databáze `fuapay_test_e178cdf` vlastněná rolí
`fuapay_app`.

Před gate měla databáze přesně všech 12 očekávaných migrací:

- chybějící migrace: 0;
- neznámé migrace: 0.

Databáze `fuapay_dev` nebyla pro integrační testy použita. Databáze
`fuapay_test_m0_20260805_143633` nebyla použita ani změněna. Nešlo o čerstvou
prázdnou disposable databázi.

## Známá vlastnost DB test suite

Celá DB test suite po sobě uklidila business data používaná testy, ale není
plně stavově neutrální:

- `audit.events` při finálním běhu vzrostlo z 54 na 87;
- `payments.order_number_sequence` zůstala jako jeden řádek, ale její stav
  se posunul;
- ostatních 14 aplikačních tabulek bylo po gate prázdných.

Nebyl proveden ruční cleanup, `TRUNCATE`, `DROP` ani reset. Jde o samostatný
známý test-infrastructure bod, který není součástí produkční account-locking
změny.

## Výsledek

Commit `caaecb68626a179d9f8f48b0b57ead408ad567f9` je pro rozsah serializace
mutací kreditního účtu ověřen jako PASS.

Tento výsledek neuzavírá integraci FUA Print, reservations, Microsoft Entra
ani ČSOB.
