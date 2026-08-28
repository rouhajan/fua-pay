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

## Zatím nepodporované

- reverse/refund volání ČSOB ani jiného karetního poskytovatele;
- providerová vratka karetní úhrady zakázky;
- vratka karetního dobití kreditu;
- providerová orchestrace nejistého výsledku a následné recovery;
- administrační UI pro vratky;
- PDF nebo potvrzení o vratce;
- částečné vratky.
