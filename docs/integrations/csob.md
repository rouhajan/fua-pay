# ČSOB Payment Gateway eAPI 1.9

Implementace navazuje na [oficiální ČSOB API Integration
wiki](https://github.com/csob/paymentgateway/wiki/API-Integration). Neobsahuje
alternativní platební systém; ČSOB je adaptér nad provider-neutral platbou.

## Podporované použití

- přímá platba konkrétní zakázky přihlášeným Customerem;
- dobití kreditu přihlášeného Customera.

Po vytvoření platby se inicializace persistuje před síťovým voláním. Adaptér
volá podepsané `payment/init`, ověří podpis a čas odpovědi, uloží `payId` a
bezprostředně jej ověří podepsaným `payment/status`. Zákazník pokračuje přes
podepsanou HTTPS `payment/process` URI vytvořenou ze známého oficiálního API
hostu.

Návratový endpoint přijímá GET nebo malý `application/x-www-form-urlencoded`
POST. Browserové `payId` je pouze podnět pro persistovanou reconciliation
frontu. Browserový stav, částka, identita, job, `merchantData` ani podpis se
nepoužijí jako důkaz platby. Background worker vždy provede serverové
`payment/status`.

Stavy 7/8 mohou vstoupit do jediné lokální settlement služby. Stav 3 platbu
zruší, stav 6 ji označí jako neúspěšnou, rozpracované stavy zůstávají pending.
Reverse/refund nebo neznámé stavy se automaticky nezaúčtují a přejdou k
operátorskému řešení.

## Podpisy a recovery

- requesty se podepisují obchodníkovým RSA SHA-256 privátním klíčem;
- každá HTTP 200 odpověď musí projít ověřením veřejným klíčem brány;
- podepsané `dttm` musí odpovídat request window a časové zóně Europe/Prague;
- HTTP redirecty API klient automaticky nenásleduje;
- timeout/nejasný `payment/init` se nikdy slepě neopakuje;
- známé `payId` se po restartu ověří, neznámý výsledek skončí
  `RequiresAttention`;
- lease, backoff a databázová unikátnost dovolují bezpečný restart i více
  instancí;
- settlement, kredit/job, audit a outbox se zapisují atomicky a opakovaný
  return/status má nejvýše jeden finanční účinek.

ČSOB eAPI 1.9 neposkytuje implementovanému toku bezpečný lookup neznámého
`payId` podle `orderNo`. Při pádu přesně po přijetí initu bránou a před
uložením odpovědi se proto automaticky nevytváří nová platba; stav vyžaduje
operátora.

## Ukládaná data

FUA Pay ukládá interní `PaymentId`, Customer `UserId`, účel, částku v haléřích
(systémová měna CZK), stav, provider, časové údaje a případný `JobId`.
Pracoviště i konkrétní tvůrce zakázky jsou dohledatelní přes Job. Pro ČSOB se
dále ukládá hlavní korelační reference `payId`, jedinečné `orderNo`, stav a
časy inicializace/recovery, počet pokusů/lease a omezený popis technické chyby.

Neukládá se PAN, CVV/CVC, PIN, datum platnosti, 3-D Secure údaj ani jiný
citlivý kartový/autentizační údaj. FUA Pay formulář karty nezobrazuje.

Aktivní refund API v současném produktu není. Nejednoznačné nebo reverse
stavy se automaticky neopakují ani nepromítají do kreditu. Před zavedením
refundů je nutné schválit účetní pravidla, oprávnění a bezpečné řešení timeoutu
proti původnímu `payId`; do té doby je řeší operátor podle autoritativního
stavu ČSOB.

## Konfigurace

Integrační prostředí smí použít pouze:

```text
Csob__ApiBaseUrl=https://iapi.iplatebnibrana.csob.cz/
```

Production smí použít pouze:

```text
Csob__ApiBaseUrl=https://api.platebnibrana.csob.cz/
```

Povinné hodnoty při zapnutí:

```text
Payments__Provider=Csob
Csob__Enabled=true
Csob__MerchantId=<merchant ID>
Csob__PrivateKeyPath=<absolutní cesta k privátnímu PEM klíči obchodníka>
Csob__GatewayPublicKeyPath=<absolutní cesta k veřejnému PEM klíči brány>
Csob__ReturnUrl=https://fuapay.tul.cz/payments/csob/return
```

Privátní klíč musí být mimo Git/release a čitelný jen účtem služby. Neúplná,
konfliktní nebo prostředí neodpovídající konfigurace zastaví startup.
`Development` provider není produkční fallback.

## Co je potřeba před první skutečnou integrační platbou

1. Získat od ČSOB přístup a Merchant ID pro integrační prostředí.
2. Vygenerovat/registrovat obchodníkův veřejný
   klíč a bezpečně dodat odpovídající privátní PEM a aktuální veřejný klíč
   brány.
3. Zpřístupnit a u ČSOB nastavit veřejnou HTTPS return URL integrační instance.
4. Nastavit výše uvedené proměnné s integrační API URL a spustit explicitní
   `echo`:

   ```powershell
   $env:FUA_PAY_CSOB_SANDBOX_TESTS_ALLOWED = "1"
   ./scripts/verify.ps1 -RunCsobSandboxTests
   ```

5. V integrační instanci provést skutečnou testovací top-up i job platbu a
   ověřit success, zamítnutí/zrušení, duplicitní nebo ztracený return, restart
   a přesně jeden lokální účinek.

Teprve úspěšné předepsané integrační scénáře patří mezi podklady pro
[aktivaci produkčního prostředí](https://github.com/csob/paymentgateway/wiki/Activation-of-the-production-environment).

Automatizované testy používají deterministické fake klienty. Opt-in síťový test
volá pouze `echo`; nevytváří transakci. Bez skutečných merchant údajů tedy
ČSOB není živě externě otestovaná.
