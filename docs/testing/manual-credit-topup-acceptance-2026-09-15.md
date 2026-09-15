# Staging acceptance ručního dobití kreditu – 2026-09-15

## Rozsah

Tento closeout dokumentuje živé staging ověření nové administrátorské operace
ručního dobití kreditu po nasazení aktuálního `main`
`9ecee2d9c57d88a2969d42094e49597b41f1642c`.

Ověření proběhlo proti `fuapay_demo` po úspěšném deploymentu a po aplikaci
migrace `20260913162532_AddManualCreditTopUps`.

## Uživatelský scénář

Administrator na stránce `Administrace -> Kredit` provedl jediné ruční dobití
staging testovacího Customer účtu:

- částka: 1 Kč;
- poznámka: `STAGING acceptance 2026-09-15 manual top-up`.

UI po odeslání zobrazilo úspěšné potvrzení, počet zobrazených kreditních pohybů
se zvýšil o jeden, nový pohyb byl `Ruční dobití kreditu` ve výši `+1 Kč` a
zůstatek se změnil z 200 Kč na 201 Kč. Následný Customer pohled nezávisle
zobrazil kredit 201 Kč a stejný poslední pohyb.

## Read-only databázové ověření

Po UI scénáři byly pouze read-only dotazy nad staging databází použity k ověření
persistované finanční evidence.

Výsledek:

- právě jeden odpovídající řádek v `credits.manual_topup_commands`;
- aktuální zůstatek účtu `20100` minor units;
- žádný `payments.payments` řádek nebyl svázán s command ID ani
  `creation_request_id` tohoto ručního dobití;
- právě jeden canonical kreditní pohyb svázaný přes
  `credits.movements.operation_id = manual_topup_commands.command_id`;
- `movement_type = 1` (`Credit`);
- `amount_minor_units = 100`;
- `balance_after_minor_units = 20100`;
- `description = Ruční dobití kreditu`;
- odpovídající audit event má `action = credit.manual-topup` a
  `entity_type = credit-account`;
- databázové porovnání potvrdilo shodu audit actoru s
  `administrator_user_id`, audit entity s `owner_id` a audit času s
  `accepted_at`.

## Výsledek

Staging acceptance ručního dobití kreditu je **PASS**.

Scénář prokázal, že jedna administrátorská operace vytvoří právě jeden
persistovaný command, právě jeden canonical kreditní pohyb a odpovídající audit,
bez vytvoření falešné `Payment`. Výsledný zůstatek v databázi i v Customer UI byl
201 Kč.

Tento acceptance scénář neověřuje PrintPayments/PrintCredentials ani produkční
ČSOB provoz; tyto části mají vlastní oddělené activation a acceptance hranice.
