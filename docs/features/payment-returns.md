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

## Zatím nepodporované

- `payment/refund` volání ČSOB ani vratka jiného karetního poskytovatele;
- vratka karetního dobití kreditu;
- PDF nebo potvrzení o vratce;
- částečné vratky.
