# SafeQ production acceptance 2026-09-28

Status: **PASS pro první schválenou produkční dávku**. SafeQ migrace jako celek
zůstává inkrementální; další uživatelé se převádějí až po legitimním Entra JIT
vzniku Production Customer identity a explicitním lidském potvrzení páru.

Tento checkpoint zachycuje první skutečné produkční použití
`LegacySafeQCreditTransfer`. Neobsahuje osobní párovací data. Raw export,
konkrétní SafeQ ID, FUA Pay UserId, jména a command ID zůstávají mimo veřejný
Git podle PII hranice kanonického migračního dokumentu.

## 1. Autoritativní hranice

- Production FUA Pay release:
  `8a9983f938581363899ef8465ec406adf116c749`.
- Jediný finální immutable SafeQ balance snapshot:
  `5305EEFCFE4B2D6DAF86DF11897626B5F2819FE422F33463D9AA53DD5072F6FA`.
- Legacy SafeQ je trvale vypnuté; nevytváří se nový freeze ani nový balance export.
- Převod používá výhradně aplikační hranici
  `LegacySafeQCreditTransferService`; žádný přímý SQL import kreditu se nepoužil.

## 2. Operátorský helper

Produkční operator byl záměrně vytvořen jako malý externí one-shot CLI helper
mimo FUA Pay Git repozitář. Není součástí FUA Pay release ani systemd webové služby.

Ověřený artifact:

- target: self-contained single-file `linux-x64`;
- velikost binárky: `113488186` B;
- SHA-256 binárky:
  `33211FF23B75C0B796A63F11DEC3F00F8CA1CD8918EB9C6DC3AC89F99AB391DB`;
- transport ZIP SHA-256:
  `E0C13CB7BE6C4A6BFCF6DF7230355E645A94D28A8E1BC727594A46C4CB3A7A6D`;
- serverová instalační cesta:
  `/var/lib/fuapay/safeq-operator-8a9983f938581363899ef8465ec406adf116c749/FuaPay.SafeQOperator`;
- server ownership/mode: `fuapay:fuapay`, `0750`.

Binárka byla před použitím znovu hashově ověřena na serveru. Neproběhl nový
FUA Pay build/deploy, změna `/opt/fuapay/current`, restart
`fuapay.service` ani aktivace stagingu.

## 3. Runtime a databázová identita

Production `fuapay.service` používá OS účet `fuapay:fuapay`, environment file
`/etc/fuapay/production.env`, databázi `fuapay` a runtime PostgreSQL roli
`fuapay_app`.

PostgreSQL `pg_hba` pro lokální spojení používá peer autentizaci s mapou
`fuapay_map`; `pg_ident` potvrzuje mapování
`sys_name=fuapay -> pg_username=fuapay_app`.

První nefinanční pokus spuštěný pod rootem byl PostgreSQL odmítnut
`Peer authentication failed for user "fuapay_app"` a neměl finanční efekt.
Akceptovaný operátorský postup proto používá transient `systemd-run` unit s
`User=fuapay`, `Group=fuapay` a
`EnvironmentFile=/etc/fuapay/production.env`.

## 4. Preview gate

Pro každý převod operator před zápisem ověřil Production environment, databázi,
role a idempotence hranice; zobrazil administrátora, cílového Customer, SafeQ ID,
částku, snapshot a command ID a vyžadoval přesnou interaktivní větu
`TRANSFER <safeq-user-id> <amount-minor-units> TO <owner-user-id>`.

Oba reálné kandidáty nejprve prošly preview a operátor zadal `ABORT`.
Operator skončil `ABORTED - no financial effect.`; následná read-only kontrola
před prvním zápisem potvrdila, že preview nevytvořilo transfer record.

## 5. První produkční dávka

Administrátor před finančním efektem explicitně potvrdil oba konkrétní páry.
Převedeny byly dvě kladné položky ze stejného immutable snapshotu.

Agregovaná evidence:

- počet schválených a provedených převodů: **2**;
- součet: **68 700 minor units = 687,00 Kč**;
- oba skutečné běhy skončily `SAFEQ TRANSFER: PASS`;
- oba procesy skončily `status=0`;
- výsledný popis obou movementů:
  `Převod kreditu ze SafeQ`.

Konkrétní identity a command ID jsou úmyslně vynechány z veřejného Git
repozitáře. Durable evidence zůstává v
`credits.legacy_safeq_credit_transfers`, credit ledgeru a auditu.

## 6. Závěrečná reconciliation dávky

Read-only transakce nad Production po obou transferech ověřila:

- `credits.legacy_safeq_credit_transfers`: přesně **2** odpovídající řádky;
- součet `amount_minor_units`: **68700**;
- `credits.movements`: přesně **2** odpovídající canonical movements;
- u obou movementů `operation_id = command_id`;
- oba movementy mají očekávanou částku, očekávaný výsledný zůstatek a přesný
  popis `Převod kreditu ze SafeQ`;
- `audit.events`: přesně **2** události
  `credit.legacy-safeq-transfer` pro tuto dávku;
- audit zachovává administrátora, cílový účet, SafeQ source, snapshot, částku a
  command ID;
- `payments.payments`: **0** řádků;
- `financial_documents.documents`: **0** řádků.

Tím je potvrzena požadovaná finanční semantika: legacy kredit není nový příjem
peněz a nevytváří `Payment` ani `FinancialDocument`.

## 7. UI evidence

Production Admin Credit view po dávce zobrazil oba nové pohyby s přesným lidským
popisem `Převod kreditu ze SafeQ`. Zobrazený popis neobsahoval SafeQ ID,
snapshot SHA ani command GUID a UI ukázalo očekávané kladné částky a odpovídající
zůstatky.

Tento checkpoint nezaměňuje Admin Credit view za samostatný browser důkaz
Customer self-service view. Pokud bude před úplným uzavřením celého SafeQ cutoveru
vyžadován i samostatný Customer-view screenshot, provede se při další vhodné
acceptance dávce; databázová a aplikační finanční evidence první dávky je PASS.

## 8. Další dávky

Další migrace se nemají provádět jako automatický hromadný match podle jména.
Doporučený dávkový postup je:

1. načíst nové Production Customer identity vzniklé legitimním Entra JIT loginem;
2. read-only porovnat účty proti finálnímu SafeQ snapshotu;
3. připravit kandidáty bez finančního efektu;
4. administrátor explicitně potvrdí každý
   `SafeQ user ID -> FUA Pay UserId` pár;
5. nulové položky nevytvářejí movement a záporné položky jdou do samostatného
   ručního rozhodnutí;
6. pro každý schválený kladný pár vytvořit stabilní command ID;
7. provést preview bez zápisu;
8. po potvrzení spustit skutečný `LegacySafeQCreditTransfer`;
9. po dávce reconciliovat počet, částky, durable transfer records, movements,
   audit a nepřítomnost neočekávaných finančních efektů;
10. součet dávky porovnat s přesně tou schválenou podmnožinou finálního snapshotu.

Tím lze pozdější dávky provádět efektivněji než první dva canary převody, ale
bez oslabení lidského potvrzení identity a bez přímých zápisů do finančního ledgeru.
