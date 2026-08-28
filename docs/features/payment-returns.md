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

## Zatím nepodporované

- reverse/refund volání ČSOB ani jiného karetního poskytovatele;
- providerová vratka karetní úhrady zakázky;
- vratka karetního dobití kreditu;
- return hold a blokování disponibilního top-up zůstatku;
- administrační UI pro vratky;
- PDF nebo potvrzení o vratce;
- částečné vratky.
