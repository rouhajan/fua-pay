# Vrácení finančního vypořádání

FUA Pay eviduje vrácení jako samostatnou trvalou událost
`SettlementReturn`. Původní platba, kreditní pohyb ani vypořádání zakázky se
neruší a nemaže. Zakázka zůstává historicky `Paid`, její původní typ, reference
a čas vypořádání se nemění. Výrobní lifecycle je nezávislý a vrácení jej nikdy
nepřetáčí zpět.

Podporované jsou pouze úplné vratky. Částku, zákazníka a zdroj určuje server z
autoritativních uložených dat; volající je nemůže zvolit. `RequestId` zajišťuje
trvalou idempotenci požadavku a unikátní vazba na zdroj dovolí pro jedno
vypořádání nejvýše jednu vratku.

## Aktuálně implementovaný tok

Zakázku uhrazenou z FUA Pay kreditu lze celou vrátit na kredit stejného
zákazníka. Původní `Debit` zůstává beze změny a připojí se kompenzační `Credit`
se `SettlementReturn.Id` jako přesně-jednou `OperationId`. Registrace vratky,
kreditní pohyb, dokončení a audit se zapisují v jedné databázové transakci.

Důvod, administrátorský aktér, zákazník a časové údaje jsou trvale uložené.
Stav `Completed` u tohoto lokálního toku znamená, že kompenzační kredit je
dokončený a při opakování také ověřený proti kreditnímu ledgeru.

## Blokování kreditu pro budoucí vratky

Modul Credits ukládá trvalý `CreditReturnHold` navázaný jedna ku jedné na
`SettlementReturn`. Aktivní hold snižuje disponibilní kredit, zatímco stavy
`Consumed` a `Released` jsou terminální a kredit neblokují. Zůstatek ledgeru se
při vytvoření holdu nemění.

Disponibilní kredit počítá jedna sdílená autoritativní služba jako zůstatek
ledgeru po odečtení aktivních FUA Print rezervací a aktivních return holdů.
Debit, vytvoření print rezervace i capture používají tento výpočet. Všechny
závody mezi blokováním a čerpáním se serializují zámkem kreditního účtu jako
prvním zámkem.

## Durabilní providerová vratka

Pro budoucí karetní vratky ukládá Payments oddělené provider-neutral pokusy
`SettlementReturnProviderAttempt`. Každý pokus má neměnný druh `Reverse` nebo
`Refund`, provider a provider reference odvozené z autoritativní původní
platby. Technický lifecycle rozlišuje připravený, zahájený, potvrzený,
zamítnutý a nejasný pokus. Omezená diagnostika neobsahuje raw HTTP zprávy,
podpisy ani kartová data.

Starší pokusy zůstávají zachované, takže po definitivně zamítnutém reverse
může vzniknout nový refund pokus bez přepsání historie. Pro jednu
`SettlementReturn` smí být současně aktivní nejvýše jeden připravený,
zahájený nebo nejasný pokus. Nejasný pokus dál blokuje nový externí pokus a po
restartu se sám nevrací do stavu připraveného k odeslání. Potvrzený pokus
sekvenci uzavírá; další pokus lze založit jen po předchozích definitivně
zamítnutých nebo neprovedených pokusech.

Pro plnou vratku zakázky uhrazené kartou přes ČSOB je nad tímto základem
implementované `payment/reverse`. Administrátorský POST používá stabilní
operation ID a ze serverového stavu odvozuje původní platbu, zákazníka, celou
částku, zakázku, provider i `payId`. Return i attempt musí být durabilně
`InProgress` před PUT a přes HTTP se nedrží databázová transakce.

Podepsaná a čerstvá odpověď `0/5` vratku potvrdí a dokončí. Přímá
odpověď reverse zamítne pokus jen při dokumentovaném `resultCode=150`
a aktuálním nereverzibilním stavu 8, 9 nebo 10. Jiné nenulové kombinace,
včetně `160/8`, přejdou do `Uncertain` / `RequiresAttention`.

Replay i restart použije pouze `payment/status` a stavový PUT se automaticky
neopakuje. Úspěšné podepsané stavové ověření `0/8`, `0/9` nebo `0/10`
zamítne jen Reverse attempt, takže `SettlementReturn` zůstává dostupná pro
budoucí samostatně autorizované rozhodnutí o refundu. Administrace při
existujícím aktivním pokusu zobrazuje stavové ověření se stejným uloženým
operation ID; dokončená, zamítnutá nebo nekonzistentní vratka nový reverse
nenabízí.

## Cílový rozsah před aktivací produkčních karetních plateb

Produktové rozhodnutí 2026-09-23 je podporovat od začátku běžné bezpečné vratky,
nikoli odkládat refund na neurčito. Aktuální implementovaný stav se tím nemění:
dokud níže uvedené body nejsou implementované a otestované, nesmějí se v UI
tvářit jako dostupné.

Požadovaný cílový tok:

- CardJob plná vratka: pokud je původní transakce ještě v reversibilním stavu,
  použít stávající `payment/reverse`; pokud už je zúčtovaná, použít
  `payment/refund`.
- CardJob částečná vratka: podporovat přes `payment/refund`, pokud to stav
  původní platby dovoluje. Součet všech potvrzených refundů nikdy nesmí překročit
  původní zúčtovanou částku.
- CardTopUp návrat na kartu: uživatelská/admin operace má podle
  autoritativního stavu původní ČSOB platby zvolit `payment/reverse`, pokud je
  transakce ještě reverzibilní, jinak `payment/refund`. Vrátit lze pouze
  částku, která je současně krytá původním karetním top-upem a stále bezpečně
  dostupná v kreditu zákazníka. Před externím providerovým krokem se vratná
  částka musí durabilně zablokovat přes `CreditReturnHold`, aby ji nebylo možné
  současně utratit. Potvrzený reverse/refund musí odpovídající kredit přesně
  jednou odebrat/spotřebovat; definitivně zamítnutá operace hold uvolní.
- U všech providerových vratek vznikne durabilní attempt před externím PUT.
  Jakmile mohl request odejít a výsledek není autoritativně známý, stav musí
  zůstat `Uncertain` / `RequiresAttention`; stejný refund/reverse se nesmí
  automaticky poslat podruhé jen kvůli timeoutu.
- Refund se vždy váže na původní provider reference/payId a na původní kartu
  prostřednictvím ČSOB; FUA Pay nevyplácí karetní platbu hotově ani běžným
  převodem jako standardní cestu.
- Vrácení musí být auditovatelné a zahrnuté do účetního/reconciliation exportu.
  PDF potvrzení o vratce je samostatný výstup a nesmí být zaměněno za
  providerovou idempotenci.

ČSOB eAPI 1.9 podporuje `payment/refund` bez částky pro plný refund a s
`amount` pro částečný refund
(https://github.com/csob/platebnibrana/blob/main/examples/eApi%20v1.9/php/Readme.md).
Standardní nízké refundy se proto nemají navrhovat jako proces vyžadující
telefonát bance při každé operaci. Ve veřejném merchant manuálu ČSOB je pro
návraty nad 50 000 Kč uveden zvláštní kontakt s Akceptací karet; tento limit se
však nesmí bez kontroly aktuální smlouvy/podmínek napevno zakódovat do domény.
Před implementací guardu se aktuální smluvní pravidlo znovu ověří. Očekávané
FUA Pay částky jsou výrazně nižší.

## Aktuálně ještě nepodporované

- skutečné ČSOB `payment/refund` volání a jeho recovery lifecycle;
- CardTopUp návrat nevyčerpaného kreditu na kartu;
- opakované/částečné refundy a jejich kumulativní limit;
- PDF nebo samostatné potvrzení o vratce.
