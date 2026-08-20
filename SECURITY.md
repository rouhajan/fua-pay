# Bezpečnost FUA Pay

FUA Pay zpracovává univerzitní identitu, zakázky, kredit a platební záznamy.
Tento dokument popisuje implementované hranice a minimální data; není tvrzením,
že systém nemůže být napaden.

## Microsoft Entra ID

Autentizace používá standardní ASP.NET Core OpenID Connect handler,
tenant-specific Entra v2.0 authority, authorization code flow, PKCE a HTTPS
metadata. FUA Pay je důvěrný webový klient. Heslo uživatele zadává a zpracovává
pouze Microsoft; aplikace je nepřijímá.

Požadované OIDC scopes jsou pouze:

- `openid` pro ověřenou identitu;
- `profile` pro zobrazované jméno;
- `email` pro volitelný kontaktní údaj, pokud jej tenant vydá.

Bezpečnostní klíč identity tvoří přesně ověřené GUID claims `tid` (tenant) a
`oid` (objekt uživatele). Chybějící, prázdné, duplicitní nebo cizí `tid`/`oid`
se odmítnou. Jméno, `preferred_username` ani e-mail se nikdy nepoužívají pro
párování nebo autorizaci.

FUA Pay ukládá:

- kanonický provider `microsoft-entra`, tenant ID a object ID;
- vlastní interní `UserId`, stav účtu a lokální role;
- zobrazované jméno, volitelný e-mail a čas posledního přístupu.

FUA Pay nepoužívá Microsoft Graph, nečte adresář, poštu, OneDrive, Teams,
kontakty ani skupiny a neimportuje osoby, které se nikdy nepřihlásily. Entra
role/skupiny se nepřebírají jako aplikační oprávnění. OIDC access token ani ID
token se neukládá do autentizační cookie, databáze nebo aplikačních logů
(`SaveTokens=false`). Token je pouze dočasně zpracován standardním handlerem
při přihlášení.

Development se simulovanými platbami neregistruje Entra OIDC handler ani ČSOB
HTTP klienta či reconciliation worker. V Production komunikuje Entra pouze
prostřednictvím standardního OIDC challenge/callback toku (metadata a token
endpoint); aplikace neprovádí vlastní adresářové dotazy. ČSOB HTTP klient se
použije jen při skutečné inicializaci platby nebo při serverovém ověření
persistované reconciliation/recovery položky. Samotný worker bez nalezené due
položky bránu nevolá. `/health/live` je čistě lokální a `/health/ready` ověřuje
pouze PostgreSQL; žádný health endpoint nekontaktuje Entra ani ČSOB. Tyto
hranice hlídají automatické testy s počítadlem všech odchozích HTTP požadavků.

První ověřené přihlášení vytvoří právě jeden lokální účet s rolí Customer.
Databázová unikátnost a opětovné načtení řeší souběžný první login. Admin může
auditovaně připojit přesné tenant/object ID k existujícímu účtu, aniž by se
měnily jeho role, kredit či historie. Jeden Entra objekt nemůže patřit dvěma
účtům a jeden účet nemůže mít dvě identity stejného providera a tenantu.
Automatické slučování podle jména nebo e-mailu neexistuje.

## Lokální relace a autorizace

Po OIDC přihlášení vznikne lokální osmihodinová cookie `FuaPay.Session` s
`HttpOnly`, `SameSite=Lax`, bez sliding expiration a v ne-vývojovém prostředí
vždy `Secure`. Při chráněném dynamickém požadavku se stav interního účtu a role
znovu synchronizují s databází; zablokovaný účet je odhlášen.

Fallback policy vyžaduje autentizaci. Veřejné jsou pouze úvodní/právní a
chybové stránky, statické soubory, health endpointy, OIDC callbacky obsloužené
handlerem a při zapnuté ČSOB integraci její return endpoint. Admin stránky
vyžadují roli Admin, management zakázek Requester nebo Admin. Requester dotazy
a příkazy kontrolují přiřazené pracoviště. Customer dotazy filtrují podle
interního `UserId`; objekt z URL se bez této kontroly nepoužije.

PDF potvrzení úhrady je dostupné pouze přihlášenému Customer pro jeho vlastní zakázku. Endpoint znovu ověří konkrétní settlement proti kreditnímu debetu nebo úspěšné přímé platbě; při rozporu se dokument nevygeneruje. PDF se neukládá do veřejných souborů a odpověď používá `Cache-Control: private, no-store`.

Změnové Razor formuláře používají antiforgery ochranu. Odhlášení je POST.
Odpovědi nastavují HSTS mimo Development, CSP, zákaz frame, MIME sniffingu a
omezenou referrer/permissions policy. Při reverzní proxy se důvěřují pouze
výslovně uvedené IP adresy.

## Peníze a ČSOB

FUA Pay ukládá peníze jako celé haléře; jedinou měnou systému je CZK. Rozsahy
částek, Customer, zakázku a účel určuje server. Browser nemůže dodat
autoritativní částku, vlastníka ani výsledek platby.

Pro ČSOB se ukládají jen údaje potřebné pro korelaci a bezpečné zpracování:

- interní `PaymentId`, Customer `UserId`, účel a případný `JobId`;
- částka v haléřích (CZK), provider, stav a časové údaje;
- ČSOB `payId`, interní jedinečné `orderNo`, iniciační/recovery stav,
  technické časy a omezená diagnostika chyby;
- vazba na pracoviště je dohledatelná přes zakázku.

Neukládají se PAN/číslo karty, CVV/CVC, PIN, údaje 3-D Secure ani jiné
autentizační údaje držitele karty. Formulář karty je na platební bráně, ne ve
FUA Pay.

Požadavky ČSOB jsou podepsané obchodníkovým RSA klíčem. Každá úspěšná API
odpověď musí mít platný podpis brány a čerstvé `dttm`. Return z browseru pouze
zařadí `payId` k serverovému `payment/status`; sám nemění peníze. Settlement
probíhá v databázové transakci a unikátní operace/constraints zajišťují jeden
účinek i při opakování nebo souběhu. Nejasná inicializace se automaticky
neopakuje a nebezpečné reverse/refund stavy vyžadují zásah operátora.

## Tajemství, klíče a logy

Do Gitu, chatu, logů ani běžných konfiguračních souborů nepatří:

- Entra client secret;
- privátní ČSOB klíč a skutečné merchant údaje;
- connection string s heslem;
- tokeny, hesla nebo produkční osobní data.

Lokálně se používají User Secrets nebo proměnné prostředí. V produkci má
tajemství dodat chráněná deployment konfigurace/secret store. Soubor
privátního ČSOB klíče musí být mimo release a čitelný jen účtem služby.
Data Protection key ring musí být trvalý a chráněný. Logy používají běžné
aplikační události a nemají obsahovat tokeny, credentials ani karetní data;
detailní finanční a administrativní změny patří do auditních tabulek.

## Produkční fail-closed pravidla

Production startup selže, pokud chybí konkrétní `AllowedHosts`, trvalý Data
Protection adresář, úplná Entra konfigurace nebo právě jeden platný payment
provider. Vývojové přihlášení, seed/reset dat a simulované platby nejsou v
Production povolené. ČSOB integrační URL je povolená jen mimo Production a
produkční URL jen v Production.

Deployment a rotace tajemství jsou popsány v
[produkční konfiguraci](docs/deployment/production-configuration.md).

## Hlášení problému

Potenciální bezpečnostní zranitelnost hlaste soukromě prostřednictvím GitHub
Private Vulnerability Reporting tohoto repozitáře. Dosud nezveřejněnou
zranitelnost nehlaste veřejným issue.

Provozovatel TUL musí před nasazením určit interní incidentní kontakt,
oprávnění k auditním záznamům a postup rotace Entra/ČSOB credentials.
