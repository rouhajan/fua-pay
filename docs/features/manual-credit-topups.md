# Ruční dobití kreditu

Administrátor může na stránce **Administrace → Kredit** použít operaci
**Dobít kredit**, pokud přijal platbu jiným způsobem nebo jinak schválil ruční
načtení kreditu. Vybere aktivního zákazníka, zadá kladnou částku v CZK a
povinnou provozní poznámku.

Ruční dobití je běžná provozní cesta financování a je odlišné od
**Administrativní korekce**. Dobití může kredit pouze zvýšit. Korekce zůstává
samostatnou operací se znaménkem `+/-` určenou k opravám chybně evidovaného
stavu; její význam ani chování se nemění.

## Finanční a auditní model

FUA Pay zůstává finanční autoritou. Přijatý příkaz ručního dobití, auditní
událost `credit.manual-topup` a kreditní pohyb se uloží atomicky v jedné
PostgreSQL transakci. ID příkazu je zároveň stabilní ID operace v existujícím
kanonickém credit ledgeru. Opakování stejného ID se stejnými daty vrátí původní
výsledek bez druhého pohybu; stejné ID s jinými daty je konflikt.

Durabilní příkaz uchovává ID příkazu, interní ID administrátora, interní ID
zákazníka, kladnou částku, normalizovanou poznámku a čas přijetí. Popis pohybu
jej výslovně označuje jako ruční dobití, takže jej nelze zaměnit za
administrativní korekci.

Dobití navyšuje tentýž kreditní zůstatek FUA Pay, ze kterého čerpají zakázky,
rezervace tisku a další kreditní konzumenti. Nevzniká nový ledger ani duplicitní
finanční stav.

## Hranice integrací

Ruční dobití je nezávislé na dostupnosti ČSOB a nevytváří falešnou platbu,
provider transakci ani karetní doklad. FUA Print nadále pouze spotřebovává
kredit FUA Pay přes existující PrintPayments API. Mezi FUA Print a databází
FUA Pay se nezavádí žádná přímá databázová integrace.
