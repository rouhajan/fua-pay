# PDF potvrzení o úhradě

FUA Pay generuje zákazníkovi PDF potvrzení k již uhrazené zakázce. Doklad je
read-only projekce existujících finančních dat; jeho stažení nemění stav
zakázky, platby ani kreditu a nevytváří nový databázový záznam.

## Zdroj pravdy

Doklad se vydá pouze pro Customer-scoped zakázku se stavem `Paid` a znovu
ověřeným settlementem:

- `Credit`: settlement reference musí odpovídat ID zakázky, ukazovat na
  kreditní `Debit` stejného Customer se stejnou částkou a čas kreditního
  pohybu musí odpovídat času úhrady zakázky;
- `DirectPayment`: reference zakázky musí ukazovat na dokončenou `Succeeded`
  platbu typu `Job`, stejného Customer, stejné zakázky a se stejnou částkou;
  úspěšná platba musí mít také provider reference a čas dokončení.

Při chybějící nebo rozporné vazbě se PDF nevygeneruje. Modul `Receipts` pouze
skládá read model nad `Jobs`, `Credits`, `Payments`, `Access` a `ServiceUnits`;
nevlastní finanční stav ani databázové schéma.

## Současný obsah

PDF obsahuje:

- logo FUA TUL;
- označení `Potvrzení o úhradě` a deterministickou referenci `PAY-{JobNumber}`;
- vystavitele a fakultu, adresu, IČO, DIČ a kontakt;
- zákazníka (zobrazované jméno a dostupný e-mail);
- číslo a název zakázky, pracoviště a datum úhrady v časové zóně Europe/Prague;
- způsob úhrady a u přímé platby poskytovatele a jeho referenci;
- celkovou uhrazenou částku, základ DPH, sazbu DPH a částku DPH;
- interní settlement reference v technické patičce.

Jediná měna je CZK. Současný preview výpočet považuje cenu zakázky za částku
včetně DPH a při sazbě 21 % dopočítává základ jako `gross / 1.21`, zaokrouhlený
na celé haléře, a DPH jako rozdíl. Toto je zatím prezentační pravidlo dokladu,
ne nové finanční pravidlo domény.

## Preview konfigurace

Vývojová konfigurace je záměrně označena jako preview. Obsahuje předběžně
zadané kontaktní údaje FUA a zástupné účetní identifikátory:

- vystavitel: `Technická univerzita v Liberci`;
- součást: `Fakulta umění a architektury`;
- adresa: `Studentská 1402/2`, `461 17 Liberec 1`, `Česká republika`;
- IČO: `00000000`;
- DIČ: `CZ00000000`;
- kontakt: `fua@tul.cz`;
- DPH: `21 %`.

Preview PDF proto nese viditelné upozornění, že IČO, DIČ a pravidlo DPH jsou
zástupné/neověřené a nejde o finální daňový doklad. Production odmítne start zapnutých
dokladů v preview režimu a mimo preview odmítne uvedené zástupné IČO/DIČ.

Před produkčním zapnutím je nutné dodat a ověřit skutečné účetní údaje,
schválený význam DPH a případně požadovanou samostatnou číselnou řadu dokladů.
Pokud má být dokument později právně neměnným účetním/daňovým dokladem, bude
nutné doplnit perzistentní snapshot vystaveného dokumentu; současná verze je
on-demand potvrzení o evidované úhradě.

Deterministická reference `PAY-{JobNumber}` současného potvrzení není číslem
budoucího účetního/daňového dokladu ani referencí poskytovatele platby. U ČSOB
je provider reference platby `payId`.

### Budoucí formální doklad

Pokud TUL schválí vystavování formálního účetního nebo daňového dokladu přímo
ve FUA Pay, dostane vystavený dokument vlastní neměnnou identitu a uložený
snapshot. Cílový model má oddělit minimálně:

- interní `DocumentId` a vlastní `DocumentNumber` z účetně schválené číselné
  řady; číslo musí být přidělené atomicky a jedinečně;
- `JobId`, typ settlementu a interní `SettlementReferenceId`;
- u `DirectPayment` vazbu na interní `PaymentId`, poskytovatele a jeho
  provider reference (u ČSOB tedy `payId`);
- u úhrady kreditem vazbu na odpovídající kreditní operaci;
- snapshot vystavitele, zákazníka, zakázky/pracoviště, částek a schválených
  účetních/DPH údajů platných v okamžiku vystavení;
- datum úhrady a samostatné datum vystavení.

Pozdější změna jména, adresy nebo konfigurace nesmí již vystavený formální
doklad přepsat. Pravidla pro opravy/storna, retenční dobu, export do účetnictví
a konkrétní formát číselné řady se doplní až podle schváleného zadání TUL.

## Ověření

Stav 2026-08-19:

- kontrola formátování a Release build prošly;
- receipts testy prošly 30/30 a celý webový test suite 545/545;
- `scripts/verify.ps1 -RunDatabaseTests` prošel nad izolovanou prázdnou
  PostgreSQL databází včetně kontroly EF modelu;
- locked restore pro `linux-x64` a self-contained Release publish s warnings as
  errors prošly; publish obsahuje PDFsharp a výchozí `Receipts:Enabled=false`;
- lokální Development runtime a vizuální kontrola PDF prošly.

Production zůstává vypnutá, dokud nejsou schválené účetní údaje, význam DPH
a provozní fonty.

## PDF a fonty

Renderer používá `PDFsharp` 6.2.4. Na Windows Development může použít systémové
Arial fonty. Na Linuxu musí být při zapnutí dokladů explicitně nastaveny
absolutní cesty k regular a bold TrueType/OpenType fontu přes
`Receipts__RegularFontPath` a `Receipts__BoldFontPath`; aplikace nespoléhá na
náhodně dostupný systémový font.

PDF se neposílá do `wwwroot` ani neukládá na disk. Endpoint je autorizovaný pro
Customer, načítá pouze jeho zakázku a odpovídá `application/pdf` s
`Cache-Control: private, no-store`.

Dobití kreditu v této etapě vlastní PDF potvrzení nemá. Jde o jiný ekonomický
případ než úhrada zakázky a nebude se do stejného modelu přimíchávat bez
schváleného účetního významu.
