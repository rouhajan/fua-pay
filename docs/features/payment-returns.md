# Vrácení finančního vypořádání

FUA Pay eviduje vrácení jako samostatnou trvalou událost
`SettlementReturn`. Původní platba, kreditní pohyb ani vypořádání zakázky se
neruší a nemaže. Zakázka zůstává historicky `Paid`, její původní typ, reference
a čas vypořádání se nemění. Výrobní lifecycle je nezávislý a vrácení jej nikdy
nepřetáčí zpět.

Kreditní zakázky podporují jednu úplnou vratku. CardJob podporuje úplnou vratku
i opakované částečné refundy; každý refund má vlastní `SettlementReturn`,
`RequestId`, částku a provider attempt. Zákazníka, zdroj, měnu a horní limit
vždy určuje server z autoritativních dat. `RequestId` zajišťuje trvalou
idempotenci a jeho opakování s jinou částkou nebo jiným payloadem je konflikt.
Filtrovaná unikátnost dál dovolí nejvýše jednu `CreditJob` vratku na zakázku a
nejvýše jednu `CardTopUp` vratku na původní platbu. CardTopUp vratka je pouze
administrační a vždy vrací celou serverem odvozenou částku původního úspěšného
ČSOB dobití; částečný CardTopUp refund ani hotovostní/bankovní alternativa
neexistují.

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
implementovaný tok Reverse → full Refund. Administrátorský POST používá stabilní
operation ID a ze serverového stavu odvozuje původní platbu, zákazníka, celou
částku, zakázku, provider i `payId`. Return i provider attempt musí být durabilně
`InProgress` před příslušným PUT a přes HTTP se nedrží databázová transakce.

Nová vratka vždy začíná `payment/reverse`. Podepsaná a čerstvá odpověď `0/5`
potvrdí Reverse a dokončí vratku stejně jako dosud. Pokud přímá dokumentovaná
odpověď `150/8` nebo status-only recovery `0/8` prokáže, že je platba již
zúčtovaná, Reverse attempt se definitivně zamítne a ve stejné databázové
transakci vznikne a začne nový Refund attempt. Teprve po commitu se právě jednou
volá `payment/refund` bez pole `amount`, tedy jako plný refund.

Přímá podepsaná odpověď refundu `0/10` potvrzuje právě odeslaný plný refund a
dokončí vratku. Odpověď `0/9` znamená zpracovávaný refund: vratka zůstává
nedokončená, aktivní attempt se při dalším spuštění ověřuje jen přes
`payment/status` a další refund PUT se neposílá. Neznámé nebo nenulové kombinace
se nepovažují za úspěch a zůstávají fail-closed.

Stav 9 nebo 10 zjištěný ještě na Reverse cestě může znamenat externí či dřívější
refund. Taková vratka přejde do attention/reconciliation stavu a FUA Pay neodešle
žádný refund PUT. Po nejasném refund PUT se attempt uloží jako `Uncertain`; každý
replay je status-only. Status 9 zůstává zpracovávaný. Veřejný ČSOB model sice pro
status 10 popisuje detail rozlišující plný a částečný návrat, ale veřejná primární
dokumentace neposkytuje jednoznačnou strojově čitelnou hodnotu, kterou by současný
response model mohl bezpečně použít jako důkaz plného refundu. Samotný status 10
proto při recovery vratku automaticky nedokončí a vyžaduje reconciliation.

Administrace rozlišuje dokončený Reverse, zpracovávaný Refund, dokončený Refund a
stav vyžadující pozornost včetně předem existující providerové vratky. Aktivní
`InProgress` / `Uncertain` attempt nabízí jen statusové ověření se stejným
uloženým operation ID; nový Reverse ani druhý Refund PUT nenabízí.

### Částečné a opakované CardJob refundy

Částečný refund vždy začíná přímo novým `Refund` attemptem a volá
`RefundAsync(payId, amountMinorUnits)`. V transakci se nejprve zamkne řádek
autoritativní zakázky, zkontroluje platba i settlement a sečtou všechny její
`CardJob` vratky kromě definitivně `Rejected`. `Requested`, `InProgress`,
`RequiresAttention` a `Completed` rezervují svou celou částku. Nový
`SettlementReturn` i attempt se uloží jako `InProgress` před PUT; transakce se
před HTTP ukončí. Stejný zámek serializuje souběžné požadavky, takže nemohou
společně překročit původní částku.

ČSOB veřejný kontrakt požaduje, aby hodnota `amount` byla kladná a přísně menší
než zbývající rozdíl. Částečný požadavek rovný celému zbytku proto FUA Pay
neodešle. Plná R2 cesta bez předchozí rezervované nebo provedené CardJob vratky
dál používá Reverse → full Refund. Pokud už partial refund existuje, plný refund
bez `amount` selže uzavřeně; veřejný kontrakt nedává bezpečný způsob, jak tímto
voláním vrátit přesně zbytek bez rizika překročení.

Přímá podepsaná a čerstvá odpověď `0/10` potvrzuje právě odeslaný PUT a jeho
konkrétní částku. Přímé `0/9` a pozdější status 9 znamenají processing. Timeout,
cancellation, transportní, podpisová, freshness, `payId` nebo persistencí
způsobená nejasnost přejde do `Uncertain` / `RequiresAttention`; každý replay je
pak pouze `payment/status`. Samotný status 10 při recovery konkrétní partial
částku nedokazuje a vratku automaticky nedokončí. Definitivně `Rejected` částka
se do rezervace nepočítá, ostatní stavy ano.

### CardTopUp plná vratka

Před prvním provider PUT se jako první zdrojový zámek zamkne kreditní účet.
Ve stejné transakci se vytvoří nebo bezpečně replayuje jediný `SettlementReturn`,
aktivní `CreditReturnHold` celé původní částky a zahájený provider attempt. Teprve
po commitu může následovat Reverse a případně full Refund bez `amount`.

Aktivní hold vstupuje do sdíleného výpočtu disponibilního kreditu. Replay při
vlastním existujícím holdu používá explicitní výpočet, který vyloučí právě tento
hold, nikoli ruční aritmetickou zkratku. Nedostatečný kredit skončí před provider
mutací.

Přímo potvrzený Reverse `0/5` nebo přímo potvrzený full Refund `0/10` v jediné
lokální transakci odečte přesně původní částku s operation ID
`SettlementReturn.Id`, spotřebuje hold, potvrdí attempt, dokončí vratku a zapíše
audit. Nejasný/maybe-sent výsledek nechává hold aktivní a další průchod je pouze
statusový. Samotný pozdější status 10 není důkazem konkrétní částky a lokální
debit automaticky nedokončí.
Pokud je provider attempt i `SettlementReturn` definitivně `Rejected`, replay
uvolní dosud aktivní hold idempotentně; nejasný nebo možná odeslaný pokus tuto
větev nikdy nepoužije.

## Rozsah před aktivací produkčních karetních plateb

Produktové rozhodnutí 2026-09-23 je podporovat od začátku běžné bezpečné vratky,
nikoli odkládat refund na neurčito. CardJob úplné i částečné vratky a plná
CardTopUp vratka jsou implementované; živá staging acceptance zůstává otevřená.

Požadovaný cílový tok:

- CardJob plná vratka: pokud je původní transakce ještě v reversibilním stavu,
  použít stávající `payment/reverse`; pokud už je zúčtovaná, použít
  `payment/refund`.
- CardJob částečná vratka: implementována přes `payment/refund`; konzervativní
  součet dokončených, rozpracovaných a nejasných vratek nikdy nesmí překročit
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
(https://github.com/csob/paymentgateway/wiki/Basic-Methods#paymentrefund-method).
Standardní nízké refundy se proto nemají navrhovat jako proces vyžadující
telefonát bance při každé operaci. Ve veřejném merchant manuálu ČSOB je pro
návraty nad 50 000 Kč uveden zvláštní kontakt s Akceptací karet; tento limit se
však nesmí bez kontroly aktuální smlouvy/podmínek napevno zakódovat do domény.
Před implementací guardu se aktuální smluvní pravidlo znovu ověří. Očekávané
FUA Pay částky jsou výrazně nižší.

## Aktuálně ještě nepodporované

- automatické potvrzení konkrétní částečné vratky pouze ze status-only odpovědi
  10 bez publikovaného strojově čitelného důkazu;
- PDF nebo samostatné potvrzení o vratce.


## Live staging acceptance 2026-09-25

Release `e84d851a31083a67f947e4d83b1ff37d2e5871e6` živě ověřil celý
implementovaný providerový return scope proti ČSOB integration gateway.

- Fresh CardJob Reverse: 20 CZK, přímé podepsané `0/5`, jeden provider attempt,
  return `Completed`.
- Full CardJob return po settlementu: Reverse větev prokázala stav 8 a byla
  definitivně uzavřena; vznikl právě jeden full Refund attempt bez `amount`,
  přímá podepsaná odpověď `0/10`, return `Completed`.
- Repeated partial CardJob refund: 100 CZK a následně 50 CZK proti původní
  platbě 520 CZK; každý refund měl vlastní `SettlementReturn` a vlastní
  potvrzený Refund attempt `0/10`. Konzervativní zbývající vratná částka po
  obou operacích byla 370 CZK.
- CardTopUp return: plných 10 CZK bylo vráceno na původní kartu přes potvrzený
  Reverse `0/5`; `CreditReturnHold` byl spotřebován a kredit byl odečten
  právě jednou. Původní payment zůstal historicky `Succeeded`.
- Účetní reconciliation export zachoval jeden řádek na provider attempt:
  full Reverse -> Refund má jeden SettlementReturn a dva provider-attempt řádky;
  repeated partial refundy mají dva samostatné SettlementReturns.

Původní payment/job settlement se při vratkách nemaže ani nepřepisuje; live data
potvrdila zamýšlenou historickou semantiku.

Detailní identifikátory a DB/export evidence jsou v
[`csob-final-staging-acceptance-2026-09-25.md`](../testing/csob-final-staging-acceptance-2026-09-25.md).

Tento PASS nemění fail-closed recovery hranice. Nejasný možná odeslaný
Reverse/Refund se nesmí automaticky opakovat druhým PUT a samotný pozdější
`payment/status=10` bez jednoznačného důkazu konkrétní vratky nadále
automaticky nepotvrzuje konkrétní full/partial refund.
