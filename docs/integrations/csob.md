# ČSOB Payment Gateway eAPI 1.9

Status: 2026-09-09

FUA Pay používá ČSOB jako provider adaptér nad interním provider-neutral modelem
platby. Browserový návrat nikdy není finanční autorita; autoritativní stav se
vždy ověřuje serverovým `payment/status` a teprve ověřený stav může změnit
lokální platbu, kredit nebo zakázku.

Podrobný aktuální checklist od integračního prostředí až po produkci je v
[`csob-production-readiness.md`](csob-production-readiness.md). Tento dokument
popisuje stabilní integrační kontrakt a aktuální implementační stav, aby se
stejné provozní TODO neduplikovalo na více místech.

## Aktuální integrační prostředí

- Merchant ID: `M1EPAY2213`.
- ČSOB integration API: `https://iapi.iplatebnibrana.csob.cz/`.
- Return URL: `https://fuapay.tul.cz/payments/csob/return`.
- Aktivní staging provider: `Csob`; simulované platby jsou vypnuté.
- Merchant private key i integration gateway public key jsou mimo Git/release a
  čitelné účtem služby.
- GET `echo`: ověřeno proti živému integračnímu prostředí.
- POST `echo`: implementováno se stejným podpisem, ověřením odpovědi a časovým
  oknem jako GET; živé integration ověření zatím nebylo provedeno.
- Úspěšné integrační dobití kreditu 100 Kč: ověřeno 2026-09-07 a znovu
  2026-09-08.
- Zrušení zákazníkem na bráně: ověřeno 2026-09-08; lokálně skončilo jako
  `Cancelled` / „Zrušená“.
- Return redirect fix z PR #35 je nasazen a reálně ověřen: návrat vede na
  `/Customer/Payments/Details/{id}?view=customer`, bez 404.
- Produkční ČSOB provoz není aktivní.

## Tok platby a bezpečnostní hranice

Po vytvoření platby se persistuje interní `Payment`, `PaymentInitiation`,
`orderNo` a korelační data. Adaptér provede podepsané `payment/init`, ověří
podepsanou odpověď, durabilně zachytí `payId`/process URI a bezprostředně provede
podepsané `payment/status`. Teprve poté se lokální platba přepne do `Pending`.

Po nové nebo v aktuálním requestu bezpečně dokončené inicializaci přesměruje
FUA Pay zákazníka přímo na podepsanou HTTPS `payment/process` URI. Před
redirectem server fail-closed ověří přesný nakonfigurovaný ČSOB origin, tvar
cesty, merchant ID a shodu `payId` s persistovanou provider reference. Stabilní
replay již inicializované `Pending` platby a provider bez externí URI vedou na
lokální detail. FUA Pay nezobrazuje formulář karty a neukládá PAN, CVV/CVC, PIN,
expiraci ani 3-D Secure údaje.

Return endpoint přijímá GET nebo malý `application/x-www-form-urlencoded` POST.
Z browseru používá pouze `payId` jako podnět pro persistovanou reconciliation
frontu. Částka, identita, účel ani browserový stav nejsou důkazem platby.
Background worker vždy znovu volá serverové `payment/status`.

Ověřené stavy ČSOB se mapují do interního lifecycle. Úspěšné stavy mohou vstoupit
pouze do jediné settlement služby; settlement, kredit/job, audit a outbox jsou
atomické a opakovaný return/status má nejvýše jeden finanční účinek. Nejasný
`payment/init` se slepě neopakuje; známý `payId` jde do recovery a neznámý výsledek
vyžaduje operátora.

Podepsaná a čerstvá odpověď `payment/status` s přesnou kombinací
`resultCode=130`, `paymentStatus=6` uzavírá odpovídající `Pending` platbu jako
`Expired` bez settlement/kredit/job efektu. Stejná odpověď umí bezpečně uzavřít
i `Created` platbu pouze tehdy, když její nejasná inicializace obsahuje přesně
stejnou persistovanou observed provider reference. Stav `6` s `resultCode=0`
zůstává běžné `Failed`; všechny ostatní nenulové kombinace zůstávají
`RequiresAttention`.

## Potvrzené UX poznatky z reálného integračního testu

### Návrat z brány

Return endpoint pouze naplánuje reconciliation a okamžitě přesměruje browser na
detail platby. Reconciliation může doběhnout až o několik sekund později. Detail
proto při lokálním stavu `Pending` používá owner-scoped GET status handler, který
čte pouze lokální payment read model a u dobití aktuální lokální kredit. Odpověď
má `Cache-Control: no-store`; handler nevolá ČSOB, reconciliation ani settlement
a nic nemění.

Self-hosted skript pod stávající CSP načítá stav po dvou sekundách, nejvýše
30krát. V místě aktualizuje status badge, čas, pending-only akce a případný
kredit v shellu. Končí při terminálním stavu, chybě, ztrátě přístupu/not-found
nebo vyčerpání limitu; ruční refresh zůstává dostupný. Jde o lokálně
implementovaný stav, nikoli důkaz živého ČSOB scénáře.

### Přechod na platební bránu

Po úspěšné nové nebo bezpečně obnovené inicializaci a okamžitém podepsaném
`payment/status` ověření vede top-up i přímá CardJob cesta rovnou na důvěryhodnou
`payment/process` URI. Detail platby zůstává recovery cesta pro již existující
`Pending` platbu a znovu ověřuje persistovanou URI stejnou provider hranicí před
zobrazením odkazu. Browser/form/query vstup cíl redirectu neurčuje.

Je správně, že lokální `Pending` záznam může existovat ještě před zadáním karty:
v té chvíli už byla platba skutečně založena u ČSOB a má `payId`. Opuštěné
`Pending` pokusy proto nejsou samy o sobě chyba a musí je řešit reconciliation /
expiry lifecycle, ne mazání historie.

## CardJob payment/reverse

Administrátor může z přehledu plateb spustit pouze plnou vratku úspěšné
ČSOB platby zakázky. POST s antiforgery předá jen idempotentní operation ID,
identifikátor vybírané platby a důvod; zákazníka, zakázku, částku, provider a
`payId` služba vždy znovu odvodí z autoritativní uložené platby a vypořádání
zakázky. CardTopUp, kreditní zakázka, částečná vratka a refund nejsou touto
cestou podporovány.

`SettlementReturn` a jeho Reverse provider attempt se nejdřív v jedné databázové
transakci uloží jako `InProgress`. Teprve po commitu smí první oprávněný request
odeslat podepsaný PUT `payment/reverse`; databázová transakce se přes HTTP nedrží.
Jediný potvrzený výsledek je čerstvá, podepsaná odpověď pro stejné `payId` s
`resultCode=0`, `paymentStatus=5`.

Jakmile PUT mohl být odeslán, timeout, zrušení, transportní chyba, neplatná
odpověď nebo chyba lokálního zápisu vedou do `Uncertain` /
`RequiresAttention`. Replay `InProgress` nebo `Uncertain` volá pouze podepsané
`payment/status` a PUT nikdy automaticky neopakuje.

Přímá odpověď reverse potvrzuje pouze `resultCode=0`, `paymentStatus=5`.
Dokumentované `resultCode=150` spolu se stavem 8, 9 nebo 10 zamítne jen Reverse
attempt a ponechá vratku v `RequiresAttention` pro samostatné budoucí
rozhodnutí o refundu. Jiné nenulové kombinace, například `160/8`, zůstávají
nejasné. Stavové recovery má oddělenou hranici: úspěšná podepsaná
odpověď `payment/status` `0/5` vratku dokončí a `0/8`, `0/9` nebo `0/10`
zamítne jen Reverse attempt. Z lokálního času se výsledek neodvozuje.

Administrátorský přehled načítá provider-neutral stav existující vratky.
Aktivní `InProgress` / `Uncertain` pokus nabídne jen stavové ověření s
původním uloženým request ID. Dokončená vratka se zobrazí jako vrácená;
zamítnutý Reverse jako rozhodnutí o refundu a nekonzistentní stav jako
vyžadující pozornost. Žádný z těchto stavů nenabídne nový reverse.

## Známé implementační mezery před production readiness

Skutečné ČSOB `payment/reverse` pro plnou CardJob vratku je implementované nad
provider-neutral persistence, ale živý bankovní aktivační scénář zatím nebyl
proveden a zůstává nezaškrtnutý v readiness checklistu.

Refund není součástí povinného ČSOB production-activation checklistu. Zda má být
in-app card refund součást první produkční verze FUA Pay, zůstává samostatné
produktové/účetní rozhodnutí. Pokud nebude, musí být výslovně definován
operátorský postup mimo aplikaci.

## Konfigurace

Integration:

```text
Csob__ApiBaseUrl=https://iapi.iplatebnibrana.csob.cz/
Payments__Provider=Csob
Csob__Enabled=true
Csob__MerchantId=M1EPAY2213
Csob__PrivateKeyPath=<absolutní cesta k privátnímu PEM klíči obchodníka>
Csob__GatewayPublicKeyPath=<absolutní cesta k integration public key brány>
Csob__ReturnUrl=https://fuapay.tul.cz/payments/csob/return
```

Production smí použít pouze:

```text
Csob__ApiBaseUrl=https://api.platebnibrana.csob.cz/
```

Při production cutoveru se musí použít produkční konfigurace/klíče schválené
ČSOB a produkční veřejný klíč brány. Žádný privátní klíč ani secret nesmí být v
Git/release. Neúplná nebo konfliktní ČSOB konfigurace musí zastavit startup;
`Development` provider není fallback.

Reconciliation konfigurace musí mít dost pokusů, aby se její retry horizont
nevyčerpal před `Csob:PaymentTtlSeconds`, třicetisekundovou provider rezervou a
jedním intervalem workeru. Výchozích 14 pokusů pokrývá celý povolený rozsah TTL
300–1800 sekund při výchozím backoffu. Expirace se z lokálního času neodvozuje;
vždy ji potvrzuje až autoritativní podepsané ČSOB `payment/status`.

## Oficiální aktivační minimum ČSOB

Aktuální ČSOB checklist (wiki naposledy upravená 2026-06-30) požaduje v
integration prostředí:

- GET echo;
- POST echo;
- úspěšně autorizovanou platbu;
- platbu zrušenou zákazníkem;
- expirovanou platbu (`resultCode=130`, `paymentStatus=6`);
- `payment/reverse` s výsledným `paymentStatus=5`;
- následné potvrzení všech scénářů v POS Merchant a review ČSOB.

Po schválení se přepíná na production API, ověří se produkční signing/verification
klíče a před otevřením uživatelům se provede kontrolovaný produkční test.

Oficiální zdroj:
https://github.com/csob/paymentgateway/wiki/Activation-of-the-production-environment
