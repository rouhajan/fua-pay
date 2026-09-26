# ČSOB Payment Gateway eAPI 1.9

Status: 2026-09-24

Pokud je ČSOB aktivní, FUA Pay jej používá jako provider adaptér nad interním
provider-neutral modelem platby. Browserový návrat nikdy není finanční autorita;
autoritativní stav se vždy ověřuje serverovým `payment/status` a teprve ověřený
stav může změnit lokální platbu, kredit nebo zakázku.

Produkční spuštění aplikace a aktivace produkčního ČSOB jsou dva nezávislé
milníky. První FUA Pay production go-live je podporovaný s
`Payments__Provider=None` a `Csob__Enabled=false`; nevytváří nové karetní platby
a není blokován bankovním activation checklistem. Tento checklist zůstává plně
závazný pro pozdější přepnutí na skutečný produkční ČSOB provoz.

Podrobný aktuální checklist od integračního prostředí až po produkci je v
[`csob-production-readiness.md`](csob-production-readiness.md). Tento dokument
popisuje stabilní integrační kontrakt a aktuální implementační stav, aby se
stejné provozní TODO neduplikovalo na více místech.

## Historicky ověřené integrační prostředí

Následující body jsou historická evidence původního demo/staging runtime do
2026-09-19. Od 2026-09-20 existuje samostatně izolovaný staging runtime na
stejném VM, standardně disabled/inactive, popsaný v
[`../deployment/runtime-state-2026-09-20.md`](../deployment/runtime-state-2026-09-20.md):

- Merchant ID: `M1EPAY2213`.
- ČSOB integration API: `https://iapi.iplatebnibrana.csob.cz/`.
- Return URL: `https://fuapay.tul.cz/payments/csob/return`.
- Aktivní staging provider: `Csob`; simulované platby jsou vypnuté.
- Merchant private key i integration gateway public key jsou mimo Git/release a
  čitelné účtem služby.
- GET `echo`: ověřeno proti živému integračnímu prostředí.
- POST `echo`: implementováno se stejným podpisem, ověřením odpovědi a časovým
  oknem jako GET; živě ověřeno s `resultCode=0`.
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
Povolí pouze pole eAPI 1.9, odmítne chybějící, duplicitní, nekanonické nebo
neznámé parametry a ověří jejich podpis v předepsaném pořadí, podpis brány a
čerstvost `dttm`. Přijímané okno ±5 minut je vlastní FUA replay-hardening policy,
nikoli zde tvrzený požadavek ČSOB. Neplatný, pozměněný nebo starý return skončí
bez naplánování.
Ověřený return používá `payId` jen jako podnět pro persistovanou reconciliation
frontu. Přesná kombinace `resultCode=130`, `paymentStatus=6` se pro stejné
`payId` uloží jednou jako podpisová evidence; opakování ji nepřepisuje. Nová
expiry evidence u uzavřeného recovery znovu naplánuje kontrolu. U aktivního lease
zůstane vlastnictví lease zachováno, ale jeho dokončení pozná novější evidence a
ponechá položku naplánovanou pro nový autoritativní dotaz. Částka, identita, účel
ani browserový stav nejsou důkazem platby. Browser pouze urychluje server-side
ověření; background worker vždy znovu volá podepsané `payment/status` a teprve
jeho ověřený výsledek smí změnit finanční stav.

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
zůstává běžné `Failed`, pokud pro stejnou platbu neexistuje durabilní, dříve
ověřená browser-return evidence `130/6`. Evidence se načítá před novým
autoritativním dotazem, takže odpověď z dotazu zahájeného před jejím uložením ji
nemůže zpětně použít jako autoritu. Jen nový podepsaný serverový status `0/6`
potvrdí `Expired`; úzká oprava z `Failed` navíc vyžaduje durabilní původ právě v
ČSOB statusu 6 a absenci kreditního, job-payment i return efektu. Return sám
nikdy nespouští settlement ani finanční změnu. Všechny ostatní nenulové kombinace
zůstávají `RequiresAttention`.

## Potvrzené UX poznatky z reálného integračního testu

### Návrat z brány

Return endpoint pouze naplánuje nebo urychlí reconciliation a okamžitě
přesměruje browser na detail platby s nedůvěryhodným UI markerem. Reconciliation
může doběhnout až o několik sekund později. Pouze post-return detail s markerem a
lokálním stavem `Pending` zapne bounded polling owner-scoped GET status handleru.
Marker nespouští provider call, reconciliation ani zápis a není finanční ani
security autoritou. Status handler čte pouze lokální payment read model a u
dobití aktuální lokální kredit. Odpověď má `Cache-Control: no-store`; handler
nevolá ČSOB, reconciliation ani settlement a nic nemění.

Self-hosted skript pod stávající CSP načítá po návratu z brány pouze lokální stav
po dvou sekundách, nejvýše 30krát. Běžný `Pending` detail před přechodem na bránu
automaticky nepolluje. Skript v místě aktualizuje status badge, čas, pending-only
akce a případný kredit v shellu. Končí při terminálním stavu, chybě, ztrátě
přístupu/not-found nebo vyčerpání limitu; continuation link na ČSOB a ruční
refresh zůstávají dostupné. Jde o post-return UX, nikoli důkaz živého ČSOB
scénáře ani zdroj finančního rozhodnutí.

Pending-only action container má explicitní `.form-actions[hidden]` pravidlo,
takže po terminálním výsledku pollingu jeho `display:flex` nepřebije HTML
`hidden`. Pravidlo je přímo součástí aktuálně nasazeného release
`774b324c48d8f874db21f115479f3b317c2a73d0` v `wwwroot/css/components.css`.

### Přechod na platební bránu

Po úspěšné nové nebo bezpečně obnovené inicializaci a okamžitém podepsaném
`payment/status` ověření vede top-up i přímá CardJob cesta rovnou na důvěryhodnou
`payment/process` URI. Detail platby zůstává recovery cesta pro již existující
`Pending` platbu a znovu ověřuje persistovanou URI stejnou provider hranicí před
zobrazením odkazu. Browser/form/query vstup cíl redirectu neurčuje.

Staging revision `768aec26c72bc77ca43d554c8e8bab20f60678b6` prokázala při
čerstvém single-click testu přímé CardJob platby `PLT-2026-000006` tento
standardní browserový tok:

```text
FUA Pay
→ HTTP 302
→ ČSOB payment/process API origin
→ HTTP 303
→ ČSOB payment-page origin
```

Chrome následoval `302` na integrační
`https://iapi.iplatebnibrana.csob.cz/api/v1.9/payment/process/...` a následné
`303` na `https://iplatebnibrana.csob.cz/pay/...`. Úspěšný návrat se bez ručního
F5 během několika sekund promítl do právě jedné `Succeeded` platby.

ČSOB browser trust boundary proto při aktivním provideru obsahuje právě dva
pevné originy pro dané prostředí:

- Integration: `https://iapi.iplatebnibrana.csob.cz` a
  `https://iplatebnibrana.csob.cz`;
- Production: `https://api.platebnibrana.csob.cz` a
  `https://platebnibrana.csob.cz`.

Bez aktivního ČSOB provideru zůstává `form-action 'self'`. Originy vlastní
fail-closed `CsobGatewayConfiguration`; neodvozují se z browser vstupu ani z
runtime `Location`. Produkční dvojici potvrzuje oficiální ČSOB eAPI dokumentace
[`payment/process`](https://github.com/csob/paymentgateway/wiki/Basic-Methods#paymentprocess-method),
která popisuje browserový GET na produkční API a následný `303` na produkční
payment page. Integrační dvojice je nasazena a celý single-click flow je live
PASS; produkční provoz zůstává vypnutý.

Je správně, že lokální `Pending` záznam může existovat ještě před zadáním karty:
v té chvíli už byla platba skutečně založena u ČSOB a má `payId`. Opuštěné
`Pending` pokusy proto nejsou samy o sobě chyba a musí je řešit reconciliation /
expiry lifecycle, ne mazání historie.

Fresh izolovaný staging acceptance 2026-09-22 navíc prokázal recovery a
idempotence vlastnosti tohoto návrhu. Restart staging procesu během živé
`Pending` platby neztratil durabilní payment/reconciliation stav; worker po
restartu znovu naběhl a následné dokončení stejné platby vytvořilo právě jeden
finanční efekt. Při samostatném lost-return testu byl inbound staging `:8443`
pro testovací klientskou cestu dočasně uzavřen, takže browser return
prokazatelně skončil timeoutem a `last_browser_return_at` zůstal prázdný.
Background worker přesto z podepsaného `payment/status 0/7` platbu bezpečně
dokončil právě jednou.

Duplicate-return acceptance použila skutečný čerstvý podepsaný POST zachycený v
browseru a replayovala beze změny pouze jeho form payload, bez session cookies.
Původní i opakovaný POST vrátily HTTP 303 na stejný payment detail; terminální
platba ani reconciliation se nezměnily a nevznikl druhý kreditní pohyb ani druhý
finanční dokument. To je live důkaz, že opakovaný validní browser return není
finanční autorita a nad již terminální platbou je idempotentní.

Živý test také upřesnil význam chyby na platební stránce: odmítnutí konkrétní
karetní autorizace nemusí ukončit celou ČSOB payment session. Testovací karta s
CVC `200` zobrazila zamítnutí vydavatelem, ale autoritativní status zůstal `0/2`
a gateway nabídla jinou kartu. FUA Pay proto správně zůstal `Pending` bez
finančního efektu; až explicitní zrušení session skončilo `0/3` / `Cancelled`.
Browserový text o odmítnutí sám nesmí odvodit `Failed` ani finanční efekt.
Terminální větev `0/6 -> Failed` zůstává samostatná od tohoto declined-attempt
scénáře.

Pozdější multi-user playtest téhož dne navíc provedl dvě nové přímé CardJob
platby z různých pracovišť (3D 120 Kč a Plotr 520 Kč) přes dva lidské uživatele.
Obě skončily `Succeeded`, reconciliation `Completed`, gateway `0/7`, příslušná
zakázka `Paid` a vznikl právě jeden `DirectJobCardPayment` finanční dokument.
Read-only kontrola potvrdila na každou zakázku právě jednu payment, jednu
`Succeeded`, nula `Pending` a přesnou shodu payment ID, job settlement reference,
document source ID, částky a provider reference. Jde o fresh end-to-end důkaz
běžného uživatelského CardJob flow nad izolovaným stagingem.

## CardJob Reverse → full Refund a partial Refund

Administrátor může z přehledu plateb spustit plnou nebo částečnou vratku úspěšné
ČSOB platby zakázky. POST s antiforgery předá idempotentní operation ID,
identifikátor vybírané platby, důvod a u partial refundu částku. Zákazníka,
zakázku, měnu, provider, `payId` a vratný limit služba vždy znovu odvodí z
autoritativní uložené platby a vypořádání zakázky. Samostatná CardTopUp cesta
používá stejný providerový stavový automat, ale navíc před PUT rezervuje celý
původní top-up přes `CreditReturnHold` a při potvrzení jej přesně jednou odečte.

`SettlementReturn` a jeho Reverse provider attempt se nejdřív v jedné databázové
transakci uloží jako `InProgress`. Teprve po commitu smí první oprávněný request
odeslat podepsaný PUT `payment/reverse`; databázová transakce se přes HTTP nedrží.
Čerstvá podepsaná odpověď pro stejné `payId` s `resultCode=0`,
`paymentStatus=5` potvrzuje Reverse a dokončuje vratku.

Jakmile PUT mohl být odeslán, timeout, zrušení, transportní chyba, neplatná
odpověď nebo chyba lokálního zápisu vedou do `Uncertain` /
`RequiresAttention`. Replay `InProgress` nebo `Uncertain` volá pouze podepsané
`payment/status` a PUT nikdy automaticky neopakuje.

Přímá odpověď reverse potvrzuje pouze `0/5`. Dokumentované `150/8` nebo
status-only recovery `0/8` prokáže zúčtovaný stav: historický Reverse attempt se
zamítne a v jedné transakci se založí a zahájí nový Refund attempt. Až po commitu
se odešle `payment/refund` pro stejné autoritativní `payId` s vynechaným
`amount`. Jde výhradně o plný refund. Přímé `0/10` z tohoto konkrétního PUT může
Refund potvrdit a dokončit vratku; přímé `0/9` ponechá aktivní zpracovávaný Refund.

Stav 9 nebo 10 zjištěný na Reverse cestě znamená možný externí/předchozí refund.
Reverse se uzavře s auditovatelnou diagnostikou, vratka vyžaduje pozornost a
`payment/refund` se nevolá. Jiné nenulové či neznámé kombinace, například
`160/8`, zůstávají nejasné. Z lokálního času se žádný finanční výsledek
neodvozuje.

Po jakémkoli možná odeslaném refund PUT vedou timeout, síťová chyba, cancellation,
neplatný podpis, freshness, jiné `payId`, malformed odpověď nebo lokální chyba
potvrzení do `Uncertain` / `RequiresAttention`. Replay pak volá výhradně
`payment/status`; druhý refund PUT se neposílá. Status 9 zůstává zpracovávaný.
Status 8 a nesouvisející kombinace zůstávají fail-closed. Samotný status 10 při
recovery není dostatečný k automatickému dokončení: veřejná primární dokumentace
nepublikuje jednoznačnou strojově čitelnou `statusDetail` hodnotu dokazující plný
refund, kterou by současný response model mohl bezpečně ověřit.

Administrátorský přehled načítá provider-neutral stav existující vratky a
zobrazuje historii jednotlivých částek, zbývající bezpečně rezervovatelnou
částku a rozlišuje dokončený Reverse, zpracovávaný Refund, dokončený Refund a stav
vyžadující pozornost včetně předem existující providerové vratky. Aktivní
`InProgress` / `Uncertain` attempt nabídne jen stavové ověření s původním
uloženým request ID. Žádný replay nenabídne nový Reverse ani druhý Refund PUT.

Partial refund má vlastní `SettlementReturn` a jediný request-bound Refund
attempt. Před PUT služba v databázové transakci zamkne autoritativní job a
rezervuje částku. Do kumulativního limitu se počítají `Requested`, `InProgress`,
`RequiresAttention` a `Completed`; pouze definitivně `Rejected` se uvolní.
Stejné pořadí zámků brání oversubscription při souběhu. Částka musí být kladná a
podle veřejného kontraktu přísně menší než zbývající rozdíl; stejný `RequestId`
s jinou částkou je konflikt.

Čerstvá podepsaná přímá odpověď partial PUT `0/10` potvrzuje právě odeslanou
částku, `0/9` znamená processing. Po nejasném výsledku je replay status-only.
Status 10 z pozdějšího `payment/status` bez publikovaného machine-readable
důkazu konkrétní partial operaci nepotvrdí. Po existující partial vratce se plný
refund bez `amount` neposílá, protože by mohl překročit zbytek.

## Protokolová hranice payment/refund

Klient ČSOB podporuje eAPI 1.9 `PUT api/v1.9/payment/refund`. Plný refund
vynechá `amount` z JSON i podpisového řetězce; částečný refund přijímá pouze
kladnou částku v celých haléřích (`long`). Odpověď pro stejné `payId` projde
stejným HTTP, RSA/SHA-256, freshness a fail-closed ověřením jako ostatní
gateway operace a vrací strukturovaný protokolový výsledek včetně volitelných
`authCode` a `statusDetail`.

CardJob i CardTopUp aplikační orchestrace tuto hranici používají pro výše popsaný plný refund
po bezpečně prokázaném stavu 8 i pro přímo zahájené partial refundy s částkou.
Samotný HTTP 200 není autorita; služba pracuje jen s výsledkem po RSA/SHA-256,
freshness a `payId` ověření. Status-only recovery 10 nadále automaticky
nerozlišuje ani nepotvrzuje konkrétní plný/částečný návrat.

## Známé implementační mezery před production readiness

Skutečné ČSOB `payment/reverse` pro plnou CardJob vratku je implementované nad
provider-neutral persistence a živý scénář `resultCode=0`, `paymentStatus=5` byl
ověřen 2026-09-12. Expiry lifecycle byl znovu fresh live ověřen 2026-09-22:
ověřený browser return `130/6` a následný serverový status `0/6` uzavřely
platbu jako `Expired` bez finančního efektu. Fresh live terminální `Failed`
`0/6` bez expiry evidence se standardním browser decline scénářem vyvolat
nepodařilo; konkrétní declined authorization zůstala na provider stavu `2` a
session umožnila další kartu. Opakovaný provider `payment/status` nad již
`Succeeded` platbou se také uměle nevynucoval, protože nasazená aplikace nemá
veřejný/operator endpoint pro takový probe; ruční DB zápis ani testovací bypass
se kvůli acceptance nepřidával. Finanční exactly-once hranice je však přímo
krytá implementací a PostgreSQL integration testem celé
reconciliation/settlement cesty: stav 7/8 nad již `Succeeded` platbou vrátí
`StateChanged=false`, provede pouze read-only status call a zachová právě jeden
pohyb/dokument nebo job settlement. Tato evidence nenahrazuje chybějící fresh
live provider replay.

Plný i částečný/opakovaný CardJob Refund a plná CardTopUp vratka jsou
implementované v aplikaci včetně
durabilního attemptu, rezervace, status-only recovery, auditu a admin UI, ale
nebyly ověřeny živým gateway testem. Jednoznačná recovery konkrétního refundu ze
samotného statusu 10 zůstává záměrně fail-closed. Refund navíc není
součástí povinného ČSOB production-activation checklistu; tento lokálně
otestovaný slice proto sám o sobě nedokládá production readiness ani bankovní
acceptance.

## Konfigurace

První produkční profil bez karetních plateb:

```text
Payments__Provider=None
Csob__Enabled=false
```

V tomto profilu nejsou vyžadovány ČSOB merchant ID, klíče, API ani return URL.
ČSOB klient, return processing a reconciliation worker nejsou aktivní a CSP
zůstává na `form-action 'self'`.

Integration profil při ručně otevřeném acceptance okně izolovaného stagingu:

```text
Csob__ApiBaseUrl=https://iapi.iplatebnibrana.csob.cz/
Payments__Provider=Csob
Csob__Enabled=true
Csob__MerchantId=M1EPAY2213
Csob__PrivateKeyPath=/var/lib/fuapay-staging/secrets/csob-integration-private.pem
Csob__GatewayPublicKeyPath=/var/lib/fuapay-staging/secrets/csob-integration-gateway-public.pem
Csob__ReturnUrl=https://fuapay.fa.tul.cz:8443/payments/csob/return
```

Tento profil smí být aktivní pouze v řízeném staging testovacím okně. Po
acceptance i následném multi-user playtestu 2026-09-22 byl aktivní
`/etc/fuapay-staging/staging.env` pokaždé vrácen na fail-closed
`Payments__Provider=None`, `Csob__Enabled=false`, staging služba byla zastavena
a dočasné UFW allow pro `8443` odstraněno. Production zůstává
nezávisle na `Payments__Provider=None` a `Csob__Enabled=false`, dokud nenastane
samostatný production activation milník.

Staging browser edge je ověřený jako
`https://fuapay.fa.tul.cz:8443` -> Nginx -> `127.0.0.1:5081`. Mimo testovací
okno UFW nemá žádné `8443` allow; při otevřeném staging okně se port povoluje
jen z explicitně schválené klientské IPv4. `payment/init` posílá
`returnUrl` přímo jako součást podepsaného requestu; browser návrat proto při
výše uvedené konfiguraci cílí na staging `:8443`, nikoli na Production
`:443`.

Veřejné eAPI 1.9 příklady ČSOB používají `returnUrl` jako běžný parametr
`payment/init` a referenční model uvádí maximální délku 300 znaků. Podpora
explicitního staging portu se proto neodvozovala jen z dokumentace: 2026-09-22
ji přímo potvrdil kontrolovaný integration `payment/init` a následné skutečné
browser návraty přes
`https://fuapay.fa.tul.cz:8443/payments/csob/return`.

Pozdější aktivní produkční ČSOB profil používá
`Payments__Provider=Csob`, `Csob__Enabled=true` a pouze:

```text
Csob__ApiBaseUrl=https://api.platebnibrana.csob.cz/
```

Při aktivaci produkčního ČSOB se musí použít produkční konfigurace/klíče
schválené ČSOB a produkční veřejný klíč brány. Žádný privátní klíč ani secret
nesmí být v Git/release. Neúplná nebo konfliktní ČSOB konfigurace musí zastavit
startup; `Development` provider není fallback.

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
klíče a před otevřením karetního toku uživatelům se provede kontrolovaný
produkční test. To je pozdější ČSOB activation gate, nikoli podmínka prvního FUA
Pay go-live bez karet.

Oficiální zdroj:
https://github.com/csob/paymentgateway/wiki/Activation-of-the-production-environment


## Fresh live acceptance 2026-09-25

Dřívější odstavec v sekci „Známé implementační mezery před production readiness“
popisuje stav před finálním fresh runem. Pro release
`e84d851a31083a67f947e4d83b1ff37d2e5871e6` jej v live-evidence části
superseduje checkpoint
[`csob-final-staging-acceptance-2026-09-25.md`](../testing/csob-final-staging-acceptance-2026-09-25.md).

Čerstvě živě byly ověřeny:

- GET a POST echo;
- success, cancel a 30minutová expiry;
- fresh Reverse `0/5`;
- full CardJob Reverse -> Refund s přímým potvrzeným Refund `0/10`;
- dvě opakované partial CardJob Refund operace, každá s vlastním
  `SettlementReturn` a přímým `0/10`;
- plná CardTopUp vratka s potvrzeným providerovým výsledkem a přesně jedním
  odečtením kreditu;
- cross-customer/cross-requester URL isolation;
- skutečný browser double-click/concurrent CardJob creation;
- dva nové podepsané provider `payment/status` dotazy nad již `Succeeded`
  platbou, oba `0/8`, oba `StateChanged=false`, bez druhého finančního efektu;
- účetní/reconciliation CSV nad skutečnými payment/return/provider-attempt daty.

Tím je pro aktuální release živě uzavřen i dříve chybějící provider-status replay
nad `Succeeded` a live refund acceptance. Záměrně fail-closed recovery pravidlo
pro nejasný refund zůstává stejné: samotný pozdější status 10 bez jednoznačného
důkazu konkrétní operace není autorita pro automatické lokální dokončení.

Před final closeoutem zůstávají 3 existující reconciliation řádky ve stavu
`RequiresAttention`; jejich původ se musí read-only klasifikovat. V okamžiku
checkpointu bylo `Pending=0` a due reconciliation `=0`.
