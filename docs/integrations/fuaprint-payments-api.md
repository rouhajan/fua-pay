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
- přesný stabilní Microsoft Entra klíč `provider + tid + oid` pouze dohledá již
  existujícího aktivního FUA Pay uživatele s rolí `Customer`.

Print cesta nikdy nepoužívá login/JIT službu. Neznámá identita se nevytvoří,
e-mail ani profilové údaje se nepoužijí k párování, role ani profil se nemění a
resolver nic nezapisuje.

## Service credential a konfigurace

Feature je v committed konfiguraci defaultně vypnutá. Deployment secret store
nastaví například:

```text
PrintPayments__Enabled=true
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

## Chybové odpovědi

Business a validační chyby jsou `application/problem+json` se stabilním polem
`code`:

- `401`: `service_authentication_failed`;
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
