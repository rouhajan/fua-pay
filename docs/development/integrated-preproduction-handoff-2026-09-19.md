# FUA Pay — integrovaný předprodukční handoff 2026-09-19

Status: auditovaný plán před prvním produkčním otevřením. Tento dokument nemění
GO/NO-GO stav bez skutečného produkčního acceptance.

## Kontext

Audit byl proveden společně nad živými repozitáři:

- `rouhajan/fua-pay`;
- `rouhajan/fua-print`;
- `rouhajan/fua-classroom`.

Cílem je během přibližně následujících 30 hodin otevřít FUA Pay studentům
minimálně pro první produkční Entra login, aby vznikly skutečné produkční
identity pro následné párování a migraci SafeQ kreditu. V dalších dnech má být
provoz postupně rozšířen o bezpečné ruční dobití, účetní doklady a produkční
placený tisk.

První produkční FUA Pay nesmí být blokován ČSOB ani FUA Print. Obě integrace
mají vlastní pozdější activation gate.

## Aktuální FUA Pay baseline

Aktuální auditovaný `main`:

`774b324c48d8f874db21f115479f3b317c2a73d0`

Na tomto SHA dne 2026-09-19 prošly GitHub Actions:

- CI: PASS;
- CodeQL: PASS.

První produkční profil zůstává:

- `ASPNETCORE_ENVIRONMENT=Production`;
- Entra zapnuta;
- development sign-in/data vypnuté;
- `Payments__Provider=None`;
- `Csob__Enabled=false`;
- `PrintPayments__Enabled=false`;
- `PrintCredentials__Enabled=false`.

Tento profil dovoluje spustit FUA Pay produkci bez karetních plateb a bez
závislosti na dokončení FUA Print.

## Co je již připravené pro provoz

### Entra / první login

Produkční identita vzniká legitimním prvním Entra loginem do čisté produkční
databáze. Demo/staging `UserId` se nepřenášejí.

To je požadovaný předpoklad pro SafeQ migraci: SafeQ účet se finančně páruje až
na existující produkční FUA Pay `UserId`.

### Ruční dobití kreditu

Administrátor má samostatnou operaci `Dobít kredit`:

- pouze kladná částka;
- povinná provozní poznámka;
- atomický canonical credit movement;
- audit `credit.manual-topup`;
- idempotentní command;
- žádný falešný provider payment.

Ruční dobití je nezávislé na ČSOB.

Po FinancialDocuments cutoveru nový úspěšný ruční top-up vytváří právě jeden
kanonický `FinancialDocument` typu `Doklad o úhradě`.

### FinancialDocuments v2

Současný produktový rozsah počítá s:

- persistentním neměnným dokumentem;
- číslem `FUA-YYYY-NNNNNN`;
- snapshotem vystavitele;
- 21% DPH snapshotem;
- PDF renderem z persistentního dokumentu;
- idempotencí zdroje;
- atomickou vazbou finančního efektu a dokumentu.

Legacy `Receipts` nejsou účetní source of truth.

## SafeQ migrace

Implementační finanční jádro je hotové a lokální PostgreSQL acceptance je PASS.

Existuje:

- `LegacySafeQCreditTransferCommand`;
- `LegacySafeQCreditTransferService`;
- PostgreSQL `credits.legacy_safeq_credit_transfers`;
- unikátní SafeQ user ID;
- idempotentní replay;
- canonical credit movement;
- audit;
- explicitní zákaz `Payment` a `FinancialDocument` efektu.

Cílené SafeQ aplikační testy: 14/14 PASS.
SafeQ persistence je součástí kanonického DB gate; celý PostgreSQL gate měl
287/287 PASS.

### Co ještě chybí před prvním skutečným převodem

1. Student se musí nejprve přihlásit do čisté produkce FUA Pay.
2. SafeQ se musí provozně zmrazit.
3. Musí vzniknout nový finální immutable balance snapshot.
4. Musí se zaznamenat jeho SHA-256.
5. Nulové, záporné a nestandardní položky musí projít review.
6. Každé `SafeQ user ID -> FUA Pay UserId` musí explicitně potvrdit člověk.
7. Musí existovat schválený operátorský vstup do
   `LegacySafeQCreditTransferService`.

Poslední bod je dnes skutečný rest: produkční CLI/admin workflow pro předání
schválených transferů do existující aplikační služby zatím v repozitáři není.

Po finálním snapshotu lze potvrzené kladné převody provádět postupně během více
dnů. Všechny převody cutoveru musí odkazovat na stejný finální snapshot a jeden
SafeQ user ID lze finančně převést právě jednou.

## Cíl pro nejbližších ~30 hodin

Priorita není přidávat nové produktové funkce. Priorita je dokončit existující
production-cutover gate:

1. definovat a ověřit bootstrap prvního produkčního Administratora;
2. definovat počáteční reálné ServiceUnits a role assignment;
3. připravit čistou produkční PostgreSQL DB;
4. vytvořit/ověřit backup a restore identitu;
5. aplikovat kanonický migration chain;
6. potvrdit čistou `FinancialDocument` číselnou řadu;
7. ověřit production env/secrets a filesystem permissions;
8. ověřit Nginx/TLS/HARICA;
9. spustit final release candidate full gate;
10. provést bounded health + Entra login/logout + role smoke;
11. otevřít studentům první produkční login.

Pokud všechny tyto body projdou, není důvod čekat na ČSOB nebo FUA Print.

## Následující provozní krok po otevření loginu

Paralelně:

- studenti vytvářejí produkční identity prvním Entra loginem;
- připraví se read-only SafeQ matching;
- dokončí se minimální operátorský SafeQ transfer workflow;
- provede se finální SafeQ freeze/snapshot;
- poté se schválené kladné zůstatky převádějí postupně.

Ruční dobití + `FinancialDocuments v2` jsou zamýšlenou součástí první
produkční verze, ale reálné peníze se mají přijímat až po čistém production
smoke jejich konkrétní cesty.

## FUA Print

FUA Print není blocker prvního FUA Pay go-live.

Produkční aktivace FUA Print vyžaduje samostatně:

- dokončený cross-repo audit;
- finální FUA Print E2E acceptance;
- production PrintSource/service credential/hash;
- production PrintCredentials pepper;
- společné zapnutí `PrintPayments` + `PrintCredentials`;
- kontrolovaný print smoke.

Aktuální FUA Print už na PC4 prokázal autonomní device-confirmed
`Reserve -> physical output -> Capture`, ale před classroom rolloutem ještě
musí rozšířit testovací fyzický profil na běžnou podporovanou tiskovou matici a
uzavřít rollout/provisioning edge cases.

## ČSOB

ČSOB zůstává samostatný pozdější milestone. Nesmí brzdit:

- první produkční Entra login;
- SafeQ identity/migraci;
- ruční hotovostní dobití;
- FinancialDocuments;
- pozdější FUA Print spotřebu existujícího kreditu.

## Guardrail

Tento dokument je plán a checkpoint. Žádný checkbox z produkčního cutoveru se
nesmí uzavřít jen proto, že je implementace v Gitu. Každý bod se uzavírá až po
konkrétním ověření na cílovém produkčním prostředí.
