# Ověření persistentního FUA Print credentialu – 2026-09-15

## Rozsah

Výchozí `main` před implementací byl
`352f884789361090e8cc63f0e2284a683d97736d` (`feat: add admin manual credit top-ups`).
Feature větev `feature/print-access-code` byla před merge přesně čtyři commity
napřed a nebyla za `main` pozadu:

1. `218a9adf969b48d8b2975cf011082a8241352850` – `feat: add persistent print credentials`;
2. `0b1b75c31816c0194a3e86bd06b352a0e8b7d903` – `fix: harden persistent print credentials`;
3. `c77ddf9865c10de9a5cb78d6b75b265344a1825f` – `fix: close persistent print credential review findings`;
4. `a903b1e4f1ccc94457f6543d10a7d56a1b4dd00b` – `fix: keep print credential revocation recoverable`.

PR #52 byl po lokálním review, plném GitHub CI a CodeQL mergeován do `main`
commitem `7364be346f402f406cde35a09c4e506fee1e9ae4`.

Feature přidává Customer-managed persistentní credential pro FUA Print:
aktuální e-mail z Access profilu a znovupoužitelný šestimístný PIN. FUA Print se
nadále autentizuje jako služba a FUA Pay zůstává jedinou autoritou nad kreditem,
rezervací a ledgerem. Existující service-authenticated `microsoft-entra/tid/oid`
reserve cesta zůstává zachována.

## Bezpečnostní a lifecycle vlastnosti

- `PrintCredentials:Enabled` je explicitní a výchozí hodnota je `false`.
- Zapnutí credentialu vyžaduje současně aktivní Print Payments konfiguraci a
  platný tajný pepper; chybějící nebo neplatná konfigurace selže fail-closed.
- PIN musí být přesně šest ASCII číslic.
- PIN se neukládá v plaintextu. Vstup je nejprve pepperován přes HMAC-SHA256 a
  výsledek je uložen pouze jako salted slow hash přes ASP.NET `PasswordHasher`.
- Plaintext PIN, pepper ani verifier nejsou zapisovány do audit trailu ani do
  aplikačního logování přidaného tímto feature.
- Známý chybný a neznámý credential oba procházejí slow verify cestou.
- Failure-only limiter je bounded a při saturaci selhává zavřeně. Limity jsou
  30 selhání na source/minutu a 6 selhání na `(source,e-mail)`/minutu; současně
  je omezen počet živých source a e-mail klíčů bez evikce aktivních counterů.
- Canonicalizace e-mailu je stejná na aplikační i PostgreSQL hranici: NFKC,
  trim pouze U+0020 SPACE, fold pouze ASCII `A-Z` na `a-z`, non-ASCII case se
  zachovává a databázová invariantní cesta používá collation `C`.
- Feature nezavádí `@tul.cz` ani jiný doménový allowlist. Používá aktuální
  e-mail uložený v Access profilu a synchronizovaný z ověřené Entra identity.
- Konfigurace nebo změna credentialu vyžaduje aktivního Customer, použitelný
  aktuální profilový e-mail a právě jeden Access profil pod canonicalizačním
  pravidlem.
- Autentizace credentialem po ztrátě, změně, syntaktické neplatnosti nebo
  ambiguaci aktuálního profilového e-mailu selže fail-closed.
- Revokace již uloženého credentialu používá pouze stabilní interní `UserId` z
  přihlášené FUA Pay session a aktivní Customer status; nevyžaduje použitelný
  aktuální e-mail. Stale uložený e-mail se v UI nezobrazuje jako současná
  identita.
- Reserve/Capture/Release/ResolutionRequired finanční semantika se nemění.
  Reserve nadále nevytváří ledger movement.
- Lost/ambiguous Reserve odpověď se nadále řeší recovery-first lookupem podle
  `jobUuid` před opakováním mutation.

## Automatizované ověření

Před finálním gatem prošly cílené testy hardeningu včetně limiter saturation,
canonicalizace, disabled-feature perimeteru, stale-profile revokace, concurrency
race a skutečných PostgreSQL lifecycle případů. Poslední cílený běh před full
gatem měl 71/71 web/application a 11/11 PostgreSQL testů.

Lokální `scripts/verify.ps1 -RunDatabaseTests` na
`a903b1e4f1ccc94457f6543d10a7d56a1b4dd00b` prošel; PostgreSQL část měla
252/252 testů, 0 failed a 0 skipped, EF pending-model check prošel a working tree
zůstal čistý.

GitHub PR gate běžel nad skutečným merge-ref
`a842cf57b08fa439eae8dbb72554972147fd3e3b`, tedy nad výsledkem spojení feature
s tehdejším `main`.

| Kontrola | Výsledek |
|---|---|
| Release build | PASS – 0 warningů, 0 chyb |
| Webové a aplikační testy | PASS – 960/960, 0 skipped |
| EF pending model changes | PASS – žádné |
| EF idempotent migration SQL | PASS |
| Exact-byte migration execution artifact verification | PASS |
| Fresh PostgreSQL 18.4 + omezené deployer/migrator role | PASS |
| Aplikace celého migration chainu na novou izolovanou DB | PASS |
| PostgreSQL integrační testy | PASS – 252/252, 0 skipped |
| NuGet vulnerability audit včetně transitive packages | PASS – 0 známých zranitelností |
| Locked restore `linux-x64` | PASS |
| Self-contained publish `linux-x64` | PASS |
| Deterministický release archive create/verify | PASS |
| Ověření vlastnictví a Unix módů po rozbalení | PASS |
| `git diff --check` a finální čistota repa | PASS |
| CodeQL C# | PASS |
| CodeQL JavaScript/TypeScript | PASS |
| CodeQL GitHub Actions | PASS |

Migrace `20260914080618_AddPersistentPrintCredentials` byla před prvním pushnutím
feature větve dokončena in-place; nevznikla opravná follow-up migrace.

## Známá provozní hranice

Self-service revokace záměrně vyžaduje, aby vlastník byl stále aktivní Customer.
Pokud Customer status ztratí ještě před revokací, aktivní credential může zůstat
persistovaný a držet svůj filtered unique `normalized_email` klíč. Feature kvůli
tomu nepřidává Admin backdoor ani obecnou credential-management službu. Případný
budoucí provozní recovery postup musí být navržen samostatně a nesmí obcházet
stávající identity a finanční hranice.

## Deployment a odložený scope

PR #52 samotný nic nenasadil. Po merge je kód v `main`, ale feature zůstává
výchozně disabled a vyžaduje explicitní konfiguraci Print Payments a pepperu.
Tento closeout neprokazuje staging ani production deployment.

FUA Pay strana credentialu je implementovaná a ověřená. Samostatně zbývá FUA
Print klientská integrace: dialog pro e-mail + PIN a napojení na nový
service-authenticated reserve endpoint. Tento krok není součástí PR #52.
