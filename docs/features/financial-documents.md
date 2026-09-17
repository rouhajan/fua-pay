# Finanční doklady

Status: normativní kontrakt pro implementaci `FinancialDocuments v2`.

Tento dokument stanovuje finanční a technické invarianty perzistentního modelu
dokladů. Lokální implementace ani její testy samy o sobě nemění současné chování
produkčních nebo staging finančních toků.

## Zdroj pravdy

Perzistentní `FinancialDocument` je po vystavení neměnný finanční záznam.
PDF je pouze jeho opakovatelný render a není zdrojem pravdy. Stažení nebo nové
vygenerování PDF nesmí vytvořit nový doklad, změnit finanční stav ani přidělit
nové číslo.

Doklad má vlastní interní `DocumentId` a vlastní `DocumentNumber`. Číslo dokladu
není provider order number, variabilní symbol, `PaymentId`, `payId`, číslo
zakázky ani ID kreditního pohybu.

## Události, které vytvářejí doklad

Doklad vzniká jen při potvrzeném externím příjmu peněz do FUA Pay:

- administrátorské ruční dobití kreditu;
- úspěšné karetní dobití kreditu po autoritativním dokončení platby;
- úspěšná přímá karetní platba zakázky po autoritativním dokončení platby.

Utracení dříve nabitého FUA Pay kreditu za zakázku nový externí příjem peněz
nevytváří a proto samo o sobě nový finanční doklad nevystavuje. Administrativní
korekce kreditu rovněž není příjmový doklad.

`Created`, `Pending`, `Failed`, `Cancelled`, `Expired`, `RequiresAttention` ani
jiný neautoritativní/nejasný stav platby doklad nevytváří. Browser return není
finanční autorita.

## Stabilní zdrojová identita a idempotence

Každý doklad nese stabilní zdrojovou identitu. Databáze musí vynutit, že jeden
zdroj může mít nejvýše jeden doklad.

Minimální zdroje:

- `ManualCreditTopUp` + `CommandId`;
- `Payment` + interní `PaymentId`.

Replay stejné finanční operace tedy vrací existující doklad. Souběžné zpracování
nesmí vytvořit dva doklady ani dva finanční efekty.

U payment zdroje se v dokumentu navíc snapshotují dostupné provider vazby,
zejména provider, interní payment ID, provider reference (`payId` u ČSOB) a
provider order number/variabilní symbol, pokud jej daný tok má.

## Rollout and source-flow cutover

The FinancialDocument requirement applies to new financial sources created after
the corresponding source flow is activated. Manual credit top-ups persisted
before the manual-flow cutover remain explicit legacy commands. They are not
automatically backfilled because the complete immutable customer snapshot from
the original financial event was not persisted.

An exact replay of a legacy manual top-up is a financial no-op: it returns the
original business result and creates neither a document nor a document number.
A persisted command marker distinguishes these legacy rows from new commands.
For a post-cutover command the marker requires the canonical FinancialDocument;
a missing document is a corruption condition that fails closed and must not be
reclassified as legacy or reconstructed from current mutable customer data.

This rollout rule does not weaken the document invariant for any new source
created after its source-flow cutover.

## Neměnný snapshot

Po vystavení se nesmí obsah dokumentu dopočítávat z aktuálně změnitelných profilů
nebo konfigurace. Perzistentní snapshot má uchovat minimálně:

- číslo a typ dokladu;
- typ zdroje a stabilní ID zdroje;
- interní ID zákazníka;
- zobrazované jméno a dostupný e-mail zákazníka v okamžiku vystavení;
- částku v minor units a měnu;
- čas finanční události a čas vystavení;
- způsob vypořádání v technicky jednoznačné podobě;
- snapshot schválených údajů vystavitele;
- neměnný daňový snapshot (daňové zacházení, sazba v basis points, základ a
  částka DPH);
- provider identifikátory, pokud existují;
- verzi schématu/renderingu potřebnou pro deterministickou interpretaci.

U přímé platby zakázky se uchová také stabilní vazba na zakázku a potřebný
snapshot popisu zakázky/pracoviště. U dobití kreditu se nevytváří falešná
zakázka ani falešná provider platba.

## Schválená podoba, vystavitel a daňové údaje

Název dokumentu je přesně `Doklad o úhradě`; dokument se neoznačuje jako
faktura ani daňový doklad. Vystavitelem je tento kanonický profil:

- Technická univerzita v Liberci;
- Fakulta umění a architektury;
- Studentská 1402/2;
- 461 17 Liberec 1;
- Česká republika;
- IČO 46747885;
- DIČ CZ46747885;
- fua@tul.cz.

Schválená sazba DPH je 21 % a `AmountMinorUnits` je hrubá částka včetně DPH.
Při vystavení se jednou deterministicky vypočte základ v celých minor units
jako `round(gross / 1.21, MidpointRounding.AwayFromZero)` a částka DPH jako
`gross - base`. Uložený v2 snapshot musí obsahovat právě tento vypočtený základ
a residual DPH; samotná shoda sazby a součtu nestačí. Vždy současně platí
`base + VAT = gross`. Stejný versioned invariant prosazuje doména i databázový
CHECK constraint přes přesnou `numeric` aritmetiku.
Daňové zacházení je explicitně typované a sazba se ukládá přesně jako 2100
basis points.

Tyto hodnoty nejsou převzaté z runtime konfigurace ani z legacy modulu
`Receipts`. Kanonický schválený issuance profil vlastní modul
`FinancialDocuments`; při vzniku dokumentu se vystavitel i daňový rozpad
snapshotují do dokumentu. Renderer používá výhradně uložený snapshot, nic
nepřepočítává a pozdější změna konfigurace nebo profilu starý dokument nemění.

Dokumenty schématu/renderu `1/1` zůstávají platnými neměnnými historickými
záznamy, ale pro schválené PDF jsou klasifikovány jako `legacy-incomplete` a
renderer je odmítne typovanou chybou. Neprovádí se backfill, změna čísla ani
dopočet z dnešní konfigurace. Nové dokumenty po tomto cutoveru používají
výhradně schéma/render `2/2` a vyžadují úplný issuer i tax snapshot.

## Číselná řada

Cílový formát je:

`FUA-YYYY-NNNNNN`

`YYYY` je obchodní rok odvozený podle `Europe/Prague`, nikoli podle UTC.
V rámci roku je jedna společná řada pro všechny typy finančních dokladů.

Přidělení musí být atomické a bezpečné při souběhu. Přidělené číslo se nikdy
nesmí použít pro jiný doklad. Mezery jsou povolené; recyklace čísla po chybě nebo
rollbacku není povolená. Přesný PostgreSQL mechanismus je implementační detail a
musí být prokázán integračním testem, nikoli odhadnut.

## Transakční hranice

Finanční efekt a perzistentní dokument musí být z pohledu business operace
atomické:

- úspěšné ruční dobití nesmí existovat bez odpovídajícího dokumentu;
- dokument ručního dobití nesmí existovat bez odpovídajícího kreditního pohybu;
- úspěšné settlement dokončení externí platby nesmí vytvořit více než jeden
  dokument;
- neúspěšná nebo nejasná platba nesmí vytvořit dokument.

Číselná řada může při rollbacku zanechat mezeru, ale nesmí vrátit číslo k
opětovnému použití.

## Povinné adversarial testy

Před mergem implementace musí automatické testy prokázat minimálně:

- replay stejného zdroje nevytvoří druhý dokument;
- souběžné vystavení vytváří unikátní čísla a nejvýše jeden dokument pro zdroj;
- rollback po přidělení čísla číslo nerecykluje;
- chyba uvnitř ručního dobití nezanechá ani kredit bez dokumentu, ani dokument
  bez kreditu;
- `Failed`/`Cancelled`/`Expired`/nejasná platba nevytvoří dokument;
- opakované stažení PDF nevytváří další dokument ani číslo;
- změna Access profilu nebo konfigurace po vystavení nemění již uložený snapshot.

## Vztah k současnému `Receipts`

Současný modul `Receipts` je on-demand read-only preview nad aktuálními daty a
není perzistentním finančním dokumentem. Jeho `PAY-{JobNumber}` není číslo
formálního dokladu. `FinancialDocuments v2` jej nebude používat jako finanční
source of truth.

PDF pro schválený doklad se musí renderovat z perzistentního
`FinancialDocument`. Starý preview tok bude buď odstraněn, nebo výslovně
ponechán jen jako neformální historické potvrzení; nesmí vytvářet paralelní
účetní význam.

## Viditelný obsah PDF

PDF zobrazuje logo, název `Doklad o úhradě`, persistentní `DocumentNumber`,
uložený snapshot vystavitele, datum úhrady, způsob úhrady a uložený daňový
rozpad. Zákaznická sekce se nezobrazuje. Provider `payId`/reference, provider
order number/variabilní symbol, `SourceId`, `PaymentId` ani jiná interní
technická reference se nezobrazují.

Ruční dobití zobrazuje účel `Dobití kreditu`. Přímá platba zakázky zobrazuje
název zakázky, pracoviště a číslo zakázky. Řádky částek jsou `Částka včetně
DPH`, `Základ bez DPH`, `DPH 21 %` a `Celkem uhrazeno` a používají pouze
uložené hodnoty.
