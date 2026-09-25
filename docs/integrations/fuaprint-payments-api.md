# FUA Print Payments API

## Hranice důvěry

FUA Pay je jediná finanční autorita. FUA Print je samostatná služba, která přes
interní HTTPS API žádá o rezervaci a vypořádání kreditu. FUA Pay neobsahuje CUPS
klienta, tiskový broker, řízení tiskárny ani FUA Print journal.

Každý endpoint pod `/api/print-payments` používá výhradně autentizační schéma a
policy `FuaPrintService`. Cookie ani antiforgery autentizace se na tuto
machine-to-machine skupinu nepoužívají. Rate limit je 120 požadavků za minutu pro
jednu vzdálenou IP, ale IP adresa není identita služby a na autentizaci nemá vliv.

Identita služby a identita studenta jsou dvě různé hranice:

- opaque bearer credential autentizuje FUA Print a server-side určí
  `printSourceId`;
- původní reserve cesta používá přesný stabilní Microsoft Entra klíč
  `provider + tid + oid`;
- alternativní reserve cesta používá zákazníkem předem nastavenou dvojici
  kanonizovaný aktuální e-mail Access profilu + trvalý šestimístný tiskový kód.

Print cesta nikdy nepoužívá login/JIT službu. Neznámá identita se nevytvoří,
role ani profil se nemění a resolver nic nezapisuje. E-mailové párování je
povoleno výhradně v credential cestě a pouze proti zvláštní tabulce
`credits.print_credentials`; obyčejný profilový index `access.users.email` není
považován za unikátní autentizační klíč.

## Stav integrace k 2026-09-14

FUA Pay strana kontraktu je implementovaná a security-hardened. Existují
endpointy pro `Reserve`, read-only recovery lookup, `ResolutionRequired`,
`Capture` a `Release`; finanční lifecycle je durabilní, idempotentní a používá
stejný autoritativní výpočet disponibilního kreditu jako ostatní kreditní cesty.

Tento stav ale neznamená, že je FUA Print end-to-end integrace nasazená. Feature
je v committed konfiguraci stále defaultně vypnutá a na druhé straně musí být
nejdřív ověřen skutečný CUPS/IPP lifecycle, zdroj ceny, stabilní `jobUuid`,
durable recovery journal a způsob získání přesné uživatelské identity, kterou
kontrakt očekává. Stávající FUA Pay API se kvůli tiskové autentizaci nemá obcházet
přímým DB přístupem, e-mailem, hostname/MAC identitou ani důvěrou v klientem
dodaný `ownerId`.

Aktivační pořadí je záměrně konzervativní:

1. read-only audit aktuálního FUA Print runtime a zdrojového kódu;
2. zachovat stávající CUPS hold a command-broker security boundary, dokud není
   explicitně nahrazena ověřenou migrací;
3. opravit na FUA Print straně všechny potvrzené mezery nutné pro bezpečné
   svázání mutation s očekávaným IPP `job-uuid` a pro durable recovery;
4. zapojit buď stávající `microsoft-entra + tid + oid`, nebo zde popsaný
   zákazníkem spravovaný e-mailový tiskový credential;
5. implementovat FUA Print klienta tohoto API s durable command IDs a recovery
   podle `jobUuid`;
6. teprve poté vytvořit jeden `printSourceId`, service credential a zapnout
   `PrintPayments` na stagingu;
7. end-to-end acceptance musí pokrýt dostatek/nedostatek kreditu, duplicate
   request, ztracenou odpověď, restart, jistý úspěch, jisté selhání a nejasný
   fyzický výsledek bez více než jednoho debitu.

Ruční administrátorské navýšení kreditu je samostatný funding/operations tok a
není důvod kvůli němu měnit PrintPayments API. FUA Print vždy pracuje se stejným
aktuálním disponibilním kreditem bez ohledu na to, zda byl kredit dříve získán
kartou nebo oprávněným administrativním zásahem.

## Service credential a konfigurace

Feature je v committed konfiguraci defaultně vypnutá. Deployment secret store
nastaví například:

```text
PrintPayments__Enabled=true
PrintCredentials__Enabled=true
PrintCredentials__PepperBase64=<base64 alespoň 32 náhodných bytů>
PrintPayments__Sources__0__PrintSourceId=<non-empty GUID>
PrintPayments__Sources__0__CredentialSha256=<64 hexadecimal characters>
```

FUA Print dostane raw opaque token s alespoň 256 bity náhodné entropie. Raw token
patří pouze do secret store FUA Print; není v Gitu ani v konfiguraci FUA Pay. FUA
Pay uchovává jen jeho SHA-256 digest. Příchozí token přijímá jako
`Authorization: Bearer <token>`, znovu ho zahashuje SHA-256 a digest porovná přes
`CryptographicOperations.FixedTimeEquals`. Validátor projde všechny
nakonfigurované digesty, token ani Authorization header neloguje a chybová
odpověď je nikdy nevrací.

Token používá base64url znaky a má délku 43 až 128 znaků; celý Authorization
header je omezen na 135 znaků. Chybějící, neplatný nebo příliš dlouhý credential
selže jako `401 service_authentication_failed`. Při zapnuté feature zastaví
startup prázdný source seznam, prázdný nebo neplatný GUID, jiný než 64znakový
hexadecimální digest a duplicitní source ID nebo digest.

Persistentní tiskové credentialy mají samostatný přepínač a jsou defaultně
vypnuté. `PrintPayments__Enabled=true` samo o sobě pepper nevyžaduje a zachovává
původní API. `PrintCredentials__Enabled=true` vyžaduje zapnuté PrintPayments a
platný pepper; jinak aplikace fail-closed zastaví startup. Vypnutá credential
feature nezobrazuje zákaznickou navigaci/formulář a credential reserve vrací
`404 print_credentials_disabled` bez ověřování e-mailu nebo PINu. Tento `404`
vzniká až po úspěšné autentizaci service beareru FUA Print; chybějící nebo
neplatný bearer skončí dříve na hranici služby jako
`401 service_authentication_failed`.

Pepper tiskových kódů je samostatný deployment secret FUA Pay. Nesmí být sdílen
s FUA Print ani s učebnovými počítači. Při zapnutých PrintCredentials chybějící,
neplatný nebo kratší než 32bytový pepper zastaví startup. Kód se před pomalým
ASP.NET Core password hasherem předzpracuje HMAC-SHA-256 s pepperem. V databázi
je pouze náhodně solený verifier; plaintext, verifier ani pepper se nezapisují do
auditu nebo logu.

Pepper musí být uložen mimo Git i release artefakt a musí stabilně přežít
restarty i nasazení dalších verzí. Jeho ztráta nebo rotace zneplatní všechny
existující verifiery. Bezpečná obnova není pokus o zpětné získání PINů, ale
vyžádání nového nastavení tiskového PINu od zákazníků.

Bezpečný základ pro vytvoření 256bitového tokenu a digestu na důvěryhodném
administračním stroji je:

```bash
TOKEN="$(openssl rand -base64 32 | tr '+/' '-_' | tr -d '=')"
printf '%s' "$TOKEN" | sha256sum
```

Hodnota `TOKEN` se bezpečným kanálem uloží do FUA Print secret store; do FUA Pay
secret konfigurace se přenese pouze první, 64znakové pole výstupu `sha256sum`.
Token se nesmí vypsat do logu ani uložit do shell historie, souboru v repozitáři
nebo committed konfigurace. Rotace v aktuálním malém modelu nepovoluje dva
credentialy pro stejné source ID: v řízeném servisním okně se vytvoří nový token,
vymění digest ve FUA Pay a raw token ve FUA Print a obě služby se restartují.
Původní token se poté zneplatní a odstraní ze secret store.

Bearer credential předpokládá důvěryhodný HTTPS transport. Samotná znalost IP,
hostname nebo MAC adresy není náhradou autentizace.

## API kontrakt

Všechny úspěšné odpovědi vracejí stabilní reservation DTO:

```text
reservationId, jobUuid, amountMinorUnits, currency, status,
reserveCommandId, resolutionCommandId, terminalCommandId,
debitOperationId, createdAt, stateChangedAt
```

DTO neobsahuje owner ID, source ID, e-mail ani zobrazované jméno. JSON mutation
body je omezen na 8 KiB a neznámé vlastnosti jsou odmítnuty. Klient tedy nemůže
vložit `ownerId` ani `printSourceId`.

### Reserve

`POST /api/print-payments/reservations`

```json
{
  "reserveCommandId": "11111111-1111-1111-1111-111111111111",
  "jobUuid": "urn:uuid:22222222-2222-2222-2222-222222222222",
  "userIdentity": {
    "provider": "microsoft-entra",
    "tenantId": "33333333-3333-3333-3333-333333333333",
    "objectId": "44444444-4444-4444-4444-444444444444"
  },
  "amountMinorUnits": 1234,
  "currency": "CZK"
}
```

Provider i `CZK` jsou case-sensitive. `tid`, `oid` a command ID musí být
neprázdné GUID, amount musí být kladný a job UUID projde existující IPP
normalizací. Owner vzniká read-only identity lookupem a source pouze z ověřeného
service credentialu. Endpoint pak deleguje na
`PrintReservationService.ReserveAsync`.

### Reserve podle tiskového credentialu

`POST /api/print-payments/reservations/by-credential`

```json
{
  "email": "student@tul.cz",
  "printCode": "123456",
  "reserveCommandId": "11111111-1111-1111-1111-111111111111",
  "jobUuid": "urn:uuid:22222222-2222-2222-2222-222222222222",
  "amountMinorUnits": 1234,
  "currency": "CZK"
}
```

Také tato cesta nejprve vyžaduje service bearer credential. Kanonizace e-mailu
má v aplikaci, PostgreSQL lookupu i invariantu `credits.print_credentials`
stejnou deterministickou definici: Unicode FormKC/NFKC, odstranění pouze znaků
U+0020 SPACE z obou konců a převod pouze ASCII `A`–`Z` na `a`–`z`. Ostatní
Unicode znaky včetně jejich velikosti zůstávají beze změny. PostgreSQL používá
explicitní `C` kolaci, takže identitu neurčuje locale databáze. Full-width ASCII
se díky NFKC sjednotí; nepodporovaná ne-ASCII case-ekvivalence se nehádá a selže
uzavřeně.

Musí existovat právě jeden aktivní tiskový credential a právě jeden odpovídající
aktuální Access profil; jeho ID musí být uloženým vlastníkem a vlastník musí být
stále aktivní efektivní `Customer`. Profilový e-mail je synchronizovatelný a
měnitelný: po změně, odstranění nebo přeřazení adresy jinému účtu autentizace
uspěje jen tehdy, když se aktuální profil pod stejnou kanonizací stále vyhodnotí
jednoznačně k uloženému vlastníkovi. Stará, přeřazená nebo nejednoznačná adresa
selže uzavřeně. Neznámý e-mail, chybný kód, zneplatněný/nenastavený credential,
neaktivní vlastník a nejednoznačný profil selžou bez rezervace stejnou odpovědí
`401 print_credential_authentication_failed`.

Access e-mail je volitelný a tato feature sama nemá `@tul.cz` ani jiný doménový
allowlist. Nastavení nebo změna credentialu vyžaduje použitelný jednoznačný
aktuální e-mail uložený v Access profilu a synchronizovaný z ověřeného Entra
profilu. Ztráta této způsobilosti nemění stabilního interního vlastníka:
aktivní `Customer` proto může existující aktivní credential vždy zneplatnit i
při chybějícím, neplatném, změněném nebo nejednoznačném aktuálním e-mailu. UI
nezobrazuje uložený starý e-mail jako aktuální identitu.

Kromě obecného limitu 120/min/IP platí pro credential cestu minutové in-memory
hranice 30 **neúspěšných** pokusů pro serverem určený `printSourceId` a 6
neúspěšných pokusů pro normalizovaný e-mail + `printSourceId`. Úspěšné tisky tyto
počty nezvyšují. Překročení vrací `429 print_credential_rate_limited`; nejde o
trvalý account lockout. Zastaralé klíče expirují a počet source i e-mailových
klíčů má pevný horní limit, takže náhodné adresy nemohou neomezeně zvětšovat
paměť procesu. Po odstranění expirovaných položek se existující živý klíč dál
počítá běžně. Je-li příslušná kapacita plná, nový klíč žádnou živou položku
nevytěsní: aktuální neúspěšná autentizace fail-closed vrátí `429` a existující
blokace zůstávají v platnosti do své normální expirace.

Po ověření se sestaví stejný `ReservePrintCreditCommand` a ihned se volá
`PrintReservationService`; nevzniká druhý ledger, Payment ani idempotency model.
Konfliktní replay zůstává konfliktem a stejný `jobUuid` nemůže vytvořit dvě
rezervace. Tiskový kód se úspěchem nespotřebuje.

Po ztracené nebo nejednoznačné reserve odpovědi FUA Print nejprve provede
autentizovaný `GET /api/print-payments/reservations?jobUuid=...`. Pokud rezervace
existuje, použije její stav. Jen když bezpečný recovery kontrakt prokáže, že
rezervace commitnuta nebyla, smí zopakovat tutéž reserve operaci se zachovaným
`reserveCommandId` a `jobUuid`. Přímý replay se starým PINem není garantován,
pokud zákazník mezitím PIN změnil nebo zneplatnil.

## Nastavení zákazníkem a zamýšlený tok

Přihlášený efektivní zákazník otevře v FUA Pay stránku **Tiskový kód**. Owner ID
se bere výhradně z autentizované session a tiskový e-mail z aktuálního
Entra-synchronizovaného Access profilu; formulář nepřijímá owner ID ani cizí
e-mail. Zákazník zvolí šest číslic, zadá potvrzení a může credential později
změnit nebo zneplatnit. Stejný kód smějí mít různí zákazníci. Změna okamžitě
nahradí předchozí verifier.

Celý tok je: Entra login → jednorázové nastavení credentialu ve FUA Pay; poté
pro každý tisk Windows print → FUA Print held job → quote → zadání e-mailu a
trvalého kódu → service call do FUA Pay → existující Reserve → fyzický tisk →
existující Capture / Release / ResolutionRequired. FUA Pay není nutné navštívit
před každým tiskem.

### Recovery a lifecycle

- `GET /api/print-payments/reservations?jobUuid=<IPP job UUID>` provede read-only
  lookup v rozsahu autentizovaného source;
- `POST /api/print-payments/reservations/{reservationId}/resolution-required`
  přijímá pouze `resolutionCommandId`;
- `POST /api/print-payments/reservations/{reservationId}/capture` přijímá pouze
  `terminalCommandId`;
- `POST /api/print-payments/reservations/{reservationId}/release` přijímá pouze
  `terminalCommandId`.

Všechny mutation endpointy pouze sestaví existující aplikační command se
server-side source ID a delegují na `PrintReservationService`. HTTP vrstva nemá
vlastní ledger ani finanční stavový automat.

Lookup slouží k recovery po restartu nebo ztracené HTTP odpovědi. Stav
`ResolutionRequired` je durable a dál blokuje dostupný kredit, dokud FUA Print
jednoznačně neprovede capture nebo release. Reserve, resolution i terminální
commandy zachovávají existující idempotenci: stejný command se stejným payloadem
vrátí uložený stav bez další rezervace, debitu nebo auditu; odlišný význam téhož
command ID je konflikt.

FUA Print používá sdílený autoritativní výpočet disponibilního kreditu. Vedle
aktivních print rezervací proto respektuje také aktivní finanční return holdy;
HTTP kontrakt se tím nemění.

## Chybové odpovědi

Business a validační chyby jsou `application/problem+json` se stabilním polem
`code`:

- `401`: `service_authentication_failed`;
- `401`: `print_credential_authentication_failed` (credential reserve);
- `429`: `print_credential_rate_limited` (credential reserve);
- `404`: `print_credentials_disabled` (credential reserve feature je vypnutá;
  pouze po úspěšné service autentizaci);
- `400`: `invalid_request`, `invalid_job_uuid`, `invalid_amount`,
  `unsupported_currency`, `invalid_identity`;
- `403`: `user_not_eligible`;
- `404`: `identity_not_linked`, `reservation_not_found`;
- `409`: `insufficient_credit`, `idempotency_conflict`, `print_job_conflict`,
  `reservation_conflict`, `invalid_lifecycle_transition`.

Pokus source A číst nebo měnit rezervaci source B se navenek chová jako
`reservation_not_found`, aby neprozrazoval existenci cizí rezervace. Neočekávané
chyby nejsou převáděny broad catchem na business 4xx; zůstávají standardní 500
bez stack trace a citlivých detailů v response.


## Handoff checkpoint po platební acceptance 2026-09-25

Fresh ČSOB/payment acceptance release
`e84d851a31083a67f947e4d83b1ff37d2e5871e6` neměnil PrintPayments API ani
persistentní PrintCredentials kontrakt. Během celého payment okna zůstaly:

```text
PrintPayments__Enabled=false
PrintCredentials__Enabled=false
```

FUA Pay strana Print kontraktu je tedy stále oddělená od právě dokončené ČSOB
acceptance a nebyla jejím průchodem aktivována.

Bezpečný handoff do samostatného FUA Print acceptance okna nastane až po
kanonickém closeoutu payment okna:

- `Pending=0`;
- due ČSOB reconciliation `=0`;
- aktivní staging env bitově vrácen na fail-closed baseline;
- `fuapay-staging.service` zastavený a disabled;
- dočasný UFW allow pro `:8443` odstraněný;
- `127.0.0.1:5081` neposlouchá;
- Production health/current/env/Nginx/301 guard beze změny.

Poté se smí otevřít nový krátký izolovaný Print staging window nad stejným
`fuapay_staging` a staging HTTPS edge. V něm se zapnou pouze potřebné
PrintPayments/PrintCredentials flagy a secrets; ČSOB se kvůli Print acceptance
nemusí zapínat.

Cílový fresh end-to-end důkaz je:

1. zákazník v FUA Pay nastaví trvalý šestimístný tiskový kód;
2. FUA Print se autentizuje service bearer credentialem;
3. held print job použije credential reserve cestu s aktuálním e-mailem + PIN;
4. rezervace sníží disponibilní kredit, ale nevytvoří ledger debit;
5. fyzický výsledek tisku vede právě jednou do `Capture`, `Release` nebo
   `ResolutionRequired`;
6. úspěšný fyzický tisk vytvoří právě jeden debit, žádný duplicate debit;
7. ztracená/nejednoznačná odpověď se řeší read-only lookupem podle `jobUuid`
   před případným replayem mutation.

ČSOB acceptance sama o sobě není důkazem FUA Print end-to-end PASS; Print window
má vlastní evidence a closeout.
