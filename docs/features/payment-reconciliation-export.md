# Účetní export plateb a vratek

Administrační stránka `Admin → Exporty` nabízí samostatný CSV export
`Účetní ČSOB reconciliation`. Export je POST-only, používá antiforgery ochranu,
je omezený na 100 000 datových řádků a jeho vytvoření se zapisuje do auditu jako
`export.payment-reconciliation`.

Export obsahuje pouze identifikátory a finanční/provozní pole potřebná pro
párování s centrálním ČSOB výpisem. Neobsahuje jméno ani e-mail zákazníka.
Stejně jako ostatní exporty používá UTF-8 s BOM, středník, CSV escaping a ochranu
proti formula injection.

## Model řádků

Řazení je deterministické: platby podle času vytvoření a FUA Payment ID, vratky
podle času požadavku a SettlementReturn ID a provider attempt podle času vzniku
a ID.

- platba bez vratky má jeden payment-only řádek;
- vratka bez provider attemptu má jeden řádek vratky;
- každý provider attempt má vlastní řádek.

Model proto neztrácí opakované partial refundy ani historii Reverse → Refund.
Pole zahrnují `orderNo`, `payId`, FUA Payment ID, finanční časy, částku a měnu,
účel, číslo zakázky a pracoviště, finanční dokument a kompletní identitu/stav/časy
vratky a provider attemptu.
