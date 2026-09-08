# ČSOB Payment Gateway eAPI 1.9

Status: 2026-09-08

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

Zákazník pokračuje přes podepsanou HTTPS `payment/process` URI vytvořenou ze
známého ČSOB API hostu. FUA Pay nezobrazuje formulář karty a neukládá PAN,
CVV/CVC, PIN, expiraci ani 3-D Secure údaje.

Return endpoint přijímá GET nebo malý `application/x-www-form-urlencoded` POST.
Z browseru používá pouze `payId` jako podnět pro persistovanou reconciliation
frontu. Částka, identita, účel ani browserový stav nejsou důkazem platby.
Background worker vždy znovu volá serverové `payment/status`.

Ověřené stavy ČSOB se mapují do interního lifecycle. Úspěšné stavy mohou vstoupit
pouze do jediné settlement služby; settlement, kredit/job, audit a outbox jsou
atomické a opakovaný return/status má nejvýše jeden finanční účinek. Nejasný
`payment/init` se slepě neopakuje; známý `payId` jde do recovery a neznámý výsledek
vyžaduje operátora.

## Potvrzené UX poznatky z reálného integračního testu

### Návrat z brány

Return endpoint pouze naplánuje reconciliation a okamžitě přesměruje browser na
detail platby. Reconciliation může doběhnout až o několik sekund později. Dne
2026-09-08 proto detail po návratu krátce zobrazil starý `Pending` stav a nový
kredit se projevil až po ručním F5.

To není finanční chyba, ale UX mezera. Cílový návrh je aktualizovat jen relevantní
data asynchronně bez reloadu celé stránky: stav platby, zobrazený kredit a akce
na detailu. Polling musí mít omezený čas, po terminálním stavu skončit a při
nedostupnosti ponechat bezpečný fallback na ruční refresh.

### Přechod na platební bránu

Aktuálně po zadání částky vznikne a inicializuje se platba, browser je přesměrován
na interní detail a teprve tam uživatel kliká na „Pokračovat na zabezpečenou
platební bránu ČSOB“.

Cílový UX je po úspěšné inicializaci přesměrovat rovnou na `payment/process`.
Detail platby zůstane zachovaný jako recovery cesta pro již existující `Pending`
platbu, takže uživatel může pokračovat na bránu i po přerušení toku.

Je správně, že lokální `Pending` záznam může existovat ještě před zadáním karty:
v té chvíli už byla platba skutečně založena u ČSOB a má `payId`. Opuštěné
`Pending` pokusy proto nejsou samy o sobě chyba a musí je řešit reconciliation /
expiry lifecycle, ne mazání historie.

## Známé implementační mezery před production readiness

1. POST `echo` zatím není implementovaný; současný klient má pouze GET echo.
2. `payment/reverse` zatím nemá skutečné ČSOB síťové volání. Existující
   provider-neutral Reverse/Refund persistence je základ, nikoli dokončená ČSOB
   operace.
3. Expired activation scénář ČSOB očekává `resultCode=130` a `paymentStatus=6`.
   Současná reconciliation nejdřív odmítá každý nenulový `resultCode`, takže
   kombinace `130/6` se dnes správně nepřeloží na interní `Expired`. Toto je
   konkrétní integrační gap, který musí být opraven před expired testem.
4. Po returnu chybí asynchronní dotažení UI do terminálního stavu.
5. Po úspěšném `payment/init`/ověření chybí přímý redirect na `payment/process`;
   interní detail vytváří jeden zbytečný klik navíc.

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
