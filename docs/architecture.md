# Produkt a architektura

FUA Pay je jeden ASP.NET Core proces a jedna PostgreSQL databáze. Moduly mají
vlastní doménová pravidla a databázová schémata, ale finanční změny mohou být
provedeny v jedné EF Core/PostgreSQL transakci.

## Role a uživatelé

Existují přesně tři role:

- `Customer` vidí jen vlastní kredit, zakázky a platby;
- `Requester` vytváří a řídí zakázky pouze v přiřazených pracovištích;
- `Administrator` spravuje uživatele, role, pracoviště, přiřazení a auditované
  korekce kreditu.

Každý aktivní účet má základní roli Customer. Requester a Administrator jsou
doplňková oprávnění. Aktuální stav účtu a rolí se při chráněných požadavcích
znovu načítá; blokace nebo odebrání role proto začne platit bez čekání na
vypršení cookie.

Uživatel vznikne prvním legitimním Entra přihlášením nebo řízenou migrací.
FUA Pay neprohledává univerzitní adresář. Ostatní moduly odkazují stabilní
interní `UserId`, takže externí identitu lze připojit bez ztráty kreditu,
zakázek, plateb, rolí či auditu.

## Pracoviště a zakázky

`ServiceUnits` jsou jednoduchá pracoviště, například 3D tisk nebo dílna.
Přiřazení Requester–pracoviště je M:N a historicky použité pracoviště se
deaktivuje místo mazání.

Zakázka uchovává Customer, pracoviště a konkrétního uživatele, který ji
vytvořil. Výrobní životní cyklus je `Draft`, `Published`, `InProduction`,
`ReadyForPickup`, `Completed` a `Cancelled`. Úhrada je oddělená: změna
výrobního stavu nikdy nepředstírá úspěšnou externí platbu.

## Kredit a platby

Peníze jsou celé haléře (`long`) a měnou systému je pevně CZK. Kredit má
neměnnou posloupnost pohybů a nezáporný zůstatek. Stabilní ID operací,
unikátní databázové indexy, optimistic concurrency a transakce chrání před
opakovaným nebo souběžným účinkem.

Platba má jediný účel: `CreditTopUp` nebo přímou úhradu jedné zakázky.
Provider pouze inicializuje a ověřuje platbu; lokální kredit či zakázku mění
jediná settlement služba. Browserový návrat je pouze podnět k serverovému
ověření.

## Moduly

- `Access`: uživatelé, externí identity, role a relace;
- `ServiceUnits`: pracoviště a přiřazení Requesterů;
- `Jobs`: zakázky, čísla, výrobní a finanční stav;
- `Credits`: účty, pohyby a administrativní korekce;
- `Payments`: provider-neutral platby, inicializace, ČSOB a reconciliation;
- `Receipts`: read-only sestavení a PDF potvrzení již ověřené úhrady zakázky;
- `Audit`, `Notifications`, `Reporting`: auditní události, transakční outbox
  a CSV exporty.

`Receipts` nevlastní finanční stav ani databázové schéma. Před vykreslením znovu
ověří vazbu zakázky na konkrétní kreditní debit nebo úspěšnou přímou platbu a
při rozporu selže. Současný dokument je on-demand potvrzení o evidované úhradě,
ne perzistentní účetní/daňový doklad. Preview účetní údaje a DPH jsou popsány v
[PDF potvrzení o úhradě](features/payment-receipts.md).

## Webová bezpečnostní hranice

Razor Pages používají fallback autorizaci, antiforgery tokeny na změnových
formulářích a serverové načtení vlastníka objektu. Admin a Requester složky
mají role/policy konvence; Customer dotazy vždy filtrují podle interního
`UserId`. Více detailů je v [SECURITY.md](../SECURITY.md).
