# Migrace zůstatků ze SafeQ do FUA Pay

Status: implementační základ a lokální PostgreSQL acceptance PASS 2026-09-19;
produkční cutover dosud neproběhl.

Tento dokument popisuje jednorázový převod existujících zákaznických kreditů
ze starého SafeQ do čisté produkční databáze FUA Pay.

Raw SafeQ exporty a pracovní párovací tabulka obsahují osobní údaje a nesmějí
být ukládány do veřejného Git repozitáře. V repozitáři je pouze proces,
agregátní evidence, implementační invarianty a testovací důkazy.

## Zdrojové podklady

Historický SafeQ report obsahuje:

- 33 890 řádků tiskové historie;
- 628 unikátních SafeQ user ID;
- historickou tiskovou aktivitu.

Tento report není finanční autoritou pro aktuální kredit. Historické ceny tisku
se nesmějí použít jako částka k migraci.

Samostatný pracovní balance export použitý pro návrh procesu obsahuje:

- 835 řádků a 835 unikátních SafeQ user ID;
- 834 unikátních loginů;
- 703 kladných zůstatků;
- 69 nulových zůstatků;
- 63 záporných zůstatků;
- součet kladných zůstatků 77 111,50 Kč;
- součet záporných zůstatků -30 731,00 Kč;
- čistý součet 46 380,50 Kč.

SHA-256 tohoto pracovního balance exportu je:

`5305EEFCFE4B2D6DAF86DF11897626B5F2819FE422F33463D9AA53DD5072F6FA`

Tento export je plánovací podklad, nikoli finální produkční finanční autorita.

Historických 628 SafeQ ID je podmnožinou balance exportu. Balance export navíc
obsahuje 207 účtů bez nalezené tiskové historie. Migrace proto vychází z
finálního balance snapshotu, nikoli pouze z historického reportu.

## Finální SafeQ freeze a autoritativní snapshot

Bezprostředně před produkční migrací musí vzniknout nový finální snapshot:

1. provozně uzavřít používání legacy SafeQ kreditu;
2. zastavit možnost starý kredit běžně utrácet nebo dobíjet;
3. až potom vytvořit čerstvý balance export;
4. spočítat jeho SHA-256;
5. uložit tento hash jako součást migrační evidence;
6. všechny skutečné převody provádět výhradně podle tohoto immutable snapshotu.

Pracovní snapshot z vývoje se nesmí bez nového freeze/exportu prohlásit za
finální produkční autoritu.

Pokud SafeQ nelze během vytvoření snapshotu zmrazit, jednoduchý jednorázový
cutover nestačí a musí být navržena samostatná delta reconciliation.

## Identita a párování

FUA Pay identita je založena na Microsoft Entra identitě a interním stabilním
`UserId`.

Produkční pořadí je:

1. uživatel se nejprve sám přihlásí do čisté produkce FUA Pay;
2. JIT vytvoří standardní FUA Pay účet;
3. účet musí být aktivní `Customer`;
4. administrátor porovná SafeQ účet s existujícím FUA Pay účtem;
5. SafeQ login, zobrazované jméno nebo podobnost e-mailu jsou pouze pomocné
   informace;
6. administrátor explicitně potvrdí vztah
   `SafeQ user ID -> FUA Pay UserId`;
7. finanční převod lze provést až po tomto potvrzení.

Nejasná identita nevytváří žádný finanční efekt.

Automatická heuristika může pomáhat hledat kandidáta, ale nesmí sama autoritativně
rozhodnout finanční párování.

## Finanční semantika

Legacy SafeQ kredit není nový příjem peněz v okamžiku migrace.

Proto má vlastní dedikovanou operaci
`LegacySafeQCreditTransfer`, nikoli běžné ruční dobití ani obecnou
administrativní korekci.

Úspěšný převod:

- vytvoří právě jeden canonical `Credit` movement;
- používá `operation_id = command_id`;
- připíše přesnou schválenou kladnou částku;
- vytvoří audit event `credit.legacy-safeq-transfer`;
- uloží administrátora, cílového vlastníka, SafeQ user ID, SHA-256 snapshotu,
  částku, čas a command ID;
- nevytvoří `Payment`;
- nevytvoří `FinancialDocument`;
- zákazníkovi zobrazí přesný popis:

`Převod kreditu ze SafeQ`

Technické SafeQ ID, snapshot SHA ani command GUID se nemají zobrazovat jako
zákaznický popis pohybu.

## Trvalá migrační evidence

Dedikovaná databázová evidence je:

`credits.legacy_safeq_credit_transfers`

Obsahuje:

- `command_id`;
- `administrator_user_id`;
- `owner_id`;
- `safeq_user_id`;
- `snapshot_sha256`;
- `amount_minor_units`;
- `accepted_at`.

`command_id` je primární klíč.

`safeq_user_id` má samostatný unikátní constraint. To znamená, že konkrétní
legacy SafeQ účet lze finančně převést právě jednou za celý život migračního
procesu, nikoli jednou pro každý snapshot.

SHA-256 snapshotu je provenance/evidence zdroje částky. Není součástí klíče,
který by dovoloval opakovaný převod stejného SafeQ účtu z jiného snapshotu.

Exact replay stejného `command_id` a stejných dat je idempotentní. Stejný
`command_id` s jinými daty je konflikt.

## Částky

Automatický finanční převod je pouze kladný.

- kladný zůstatek lze po potvrzení identity převést;
- nulový zůstatek nevytváří finanční pohyb;
- záporný zůstatek vyžaduje samostatné ruční rozhodnutí a nesmí být automaticky
  importován jako SafeQ credit transfer.

Interní technický guardrail `LegacySafeQCreditTransfer` je:

- minimum: 1 minor unit;
- maximum: 10 000 000 minor units, tedy 100 000 Kč.

Tento limit je interní ochranná hranice FUA Pay. Není to tvrzení o maximální
hodnotě podporované nebo povolené zdrojovým SafeQ.

Pokud by finální SafeQ snapshot obsahoval legitimní kladný zůstatek nad tímto
guardrailem, převod se zastaví k ručnímu review; limit se nesmí obcházet
rozdělením jednoho SafeQ účtu do více importů.

## Chybné párování zjištěné po převodu

Již převedený SafeQ účet se nesmí znovu importovat.

Pokud se později prokáže chybné přiřazení, oprava musí být provedena jako
samostatná auditovaná korekce nebo storno podle schváleného provozního postupu.

Původní `LegacySafeQCreditTransfer` a jeho auditní historie zůstávají
neměnné.

## Pracovní párovací evidence

Offline pracovní tabulka může obsahovat pouze údaje potřebné k review, například:

- SafeQ user ID;
- SafeQ login a zobrazované jméno;
- aktuální kredit ze snapshotu;
- historickou aktivitu pouze jako pomocnou informaci;
- kontrolní flagy;
- navržený FUA Pay účet;
- FUA Pay `UserId`;
- důvod a jistotu návrhu;
- rozhodnutí administrátora;
- kdo a kdy párování potvrdil;
- import status;
- výsledný credit operation ID;
- poznámku.

Historická cena tisku musí být vždy oddělena od finančního zůstatku.

Pracovní tabulka s osobními údaji zůstává mimo veřejný Git.

## Produkční workflow

Doporučený jednorázový proces:

1. připravit čistou produkční FUA Pay databázi;
2. nechat uživatele legitimně vytvořit své účty přihlášením přes Entra;
3. provést SafeQ freeze;
4. vytvořit a zahashovat finální balance snapshot;
5. pracovat pouze s tímto snapshotem;
6. administrátor provede explicitní párování;
7. nulové a záporné zůstatky se oddělí od automatického převodu;
8. každý schválený kladný pár se provede přes
   `LegacySafeQCreditTransfer`;
9. po každém úspěšném převodu existuje durable SafeQ transfer record,
   canonical credit movement a audit;
10. provedené převody se reconciliují proti schválené pracovní evidenci a
    finálnímu snapshotu.

Uživatel, který se ještě nepřihlásil do FUA Pay, se finančně nepřevádí.

## Implementační stav

Implementované finanční jádro obsahuje:

- `LegacySafeQCreditTransferCommand`;
- `LegacySafeQCreditTransferService`;
- `ILegacySafeQCreditTransferRepository`;
- PostgreSQL persistence
  `credits.legacy_safeq_credit_transfers`;
- unikátnost SafeQ user ID;
- idempotentní command replay;
- konflikt při změně payloadu;
- kontrolu aktivního Customer účtu;
- audit;
- canonical credit movement;
- samostatnou amount policy;
- explicitní zákaz `FinancialDocument` efektu.

Produkční párovací/importní operátorský workflow není tímto dokumentem
prohlášen za live ani za dokončený. Před produkčním cutoverem musí být explicitně
ověřen způsob, kterým schválené položky administrátor skutečně předá finančnímu
jádru.

## Lokální acceptance 2026-09-19

Cílené aplikační testy SafeQ převodu:

- 14/14 PASS.

Cílené testy finanční amount policy:

- 5/5 PASS.

Cílené PostgreSQL testy SafeQ persistence ověřují mimo jiné:

- úspěšný převod a persistence round-trip;
- exact replay bez druhého finančního efektu;
- zákaz druhého převodu stejného SafeQ user ID;
- skutečný PostgreSQL unique constraint i pro jiný snapshot;
- rollback celé finanční transakce při injektovaném selhání po zápisu kreditu;
- souběžný pokus o převod stejného SafeQ user ID s právě jedním vítězným
  finančním efektem;
- právě jeden audit;
- žádný `FinancialDocument`.

SafeQ PostgreSQL persistence obsahuje 4 testy; všechny prošly jako součást kanonického DB gate.

Kanonický lokální gate:

`.\scripts\verify.ps1 -RunDatabaseTests`

Výsledek:

- Release build PASS;
- formatting PASS;
- web/application tests PASS;
- EF pending-model check PASS;
- PostgreSQL integrační testy 286/286 PASS;
- finální výsledek `Ověření FUA Pay prošlo.`

## Acceptance před produkčním použitím

Před prvním skutečným SafeQ převodem musí být ještě prokázáno:

- čistá produkční databáze a správná produkční konfigurace;
- finální SafeQ freeze;
- nový finální balance export;
- SHA-256 finálního exportu;
- review nulových, záporných a nestandardních položek;
- explicitní lidské párování SafeQ ID na existující FUA Pay `UserId`;
- schválený operátorský způsob spuštění transferu;
- součet provedených převodů odpovídá schváleným kladným položkám;
- zákaznické UI zobrazuje `Převod kreditu ze SafeQ`;
- žádný převod nevytvoří `Payment` ani `FinancialDocument`;
- audit a durable transfer evidence umožní dohledat administrátora, účet,
  snapshot, částku, čas a command ID.

Teprve po tomto acceptance lze skutečný SafeQ cutover označit za dokončený.
