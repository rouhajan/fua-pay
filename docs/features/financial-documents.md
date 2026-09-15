# Finanční doklady

Status: návrhový kontrakt pro implementaci `FinancialDocuments v2`.

Tento dokument stanovuje finanční a technické invarianty budoucího perzistentního
modelu dokladů. Dokud není implementace dokončená a samostatně ověřená, nemění
současné chování produkčních ani staging finančních toků.

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
- provider identifikátory, pokud existují;
- verzi schématu/renderingu potřebnou pro deterministickou interpretaci.

U přímé platby zakázky se uchová také stabilní vazba na zakázku a potřebný
snapshot popisu zakázky/pracoviště. U dobití kreditu se nevytváří falešná
zakázka ani falešná provider platba.

## Daňové údaje

DPH, daňový základ, DIČ, formální název dokladu ani jiné účetní/daňové tvrzení
se nesmí odvodit ze současného preview `Receipts` modelu nebo z jeho výchozí
21% sazby. Tyto údaje se do formálního dokladu aktivují až po schválení TUL.

Model může mít připravená explicitní pole pro schválený daňový snapshot, ale
neznámá pravidla se nesmí nahrazovat výchozími hodnotami. Produkční aktivace
formálního PDF zůstává fail-closed, dokud nejsou schválené údaje dostupné.

## Číselná řada

Cílový formát je:

`FUA-YYYY-NNNNNN`

`YYYY` je obchodní rok odvozený podle `Europe/Prague`, nikoli náhodně podle UTC.
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

Po dokončení v2 se PDF pro formální doklad musí renderovat z perzistentního
`FinancialDocument`. Starý preview tok bude buď odstraněn, nebo výslovně
ponechán jen jako neformální historické potvrzení; nesmí vytvářet paralelní
účetní význam.
