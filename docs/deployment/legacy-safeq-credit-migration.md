# Migrace zůstatků ze SafeQ do FUA Pay

Status: návrh cutover procesu 2026-09-18

Tento dokument popisuje jednorázový převod existujících zákaznických kreditů
ze starého SafeQ do čisté produkční databáze FUA Pay.

Raw SafeQ exporty obsahují osobní údaje a nesmějí být ukládány do veřejného Git
repozitáře. V repozitáři je pouze proces, agregátní evidence a implementační
invarianty.

## Zdrojové podklady

Analyzovaný historický SafeQ report
`SafeQReport-CSV-2026-08-14-13-31-39` obsahuje:

- 33 890 řádků tiskové historie;
- 628 unikátních SafeQ user ID;
- 628 unikátních loginů;
- žádný zjištěný případ jednoho SafeQ ID s více loginy;
- žádný zjištěný případ jednoho loginu svázaného s více SafeQ ID;
- tiskovou historii od 2013-08-20 do 2026-08-06.

Tento report neobsahuje autoritativní aktuální zůstatek účtu. Historickou cenu
tisku proto nelze použít jako částku k migraci.

Pro skutečný převod je před cutoverem nutný samostatný finální SafeQ balance
export po provozním freeze starého systému. Ideální klíč tohoto exportu je stejné
stabilní SafeQ user ID; login a jméno jsou pouze pomocné atributy.

## Aktivita a priorita kontroly

Poslední datum v historickém reportu je poslední zaznamenaná tisková aktivita,
nikoli poslední login ani poslední dobití.

Pro prioritizaci ručního review lze účty rozdělit:

- 181 účtů: poslední tisková aktivita v posledních 2 letech;
- 98 účtů: poslední tisková aktivita před 2–5 lety;
- 349 účtů: poslední tisková aktivita před více než 5 lety.

Toto rozdělení je pouze priorita. Staré účty se automaticky nevyřazují a jejich
zůstatek se nezahazuje.

## Párovací princip

FUA Pay identita zůstává založená na Microsoft Entra `tid + oid` a interním
`UserId`. SafeQ login typu `jmeno.prijmeni`, zobrazované jméno ani podobnost
e-mailu se nesmějí stát automatickou finanční autoritou.

Doporučený model:

1. uživatel se nejprve sám přihlásí do čisté produkce FUA Pay přes Entra;
2. JIT vytvoří standardní Customer účet a stabilní interní FUA Pay `UserId`;
3. migrační tabulka nabídne legacy SafeQ účet jako kandidáta;
4. administrátor porovná SafeQ ID/login/jméno a FUA Pay profil;
5. případná shoda SafeQ loginu s Entra e-mailovým local-partem je pouze pomocný
   signál, ne automatické schválení;
6. administrátor explicitně potvrdí konkrétní pár
   `SafeQ user ID -> FUA Pay UserId`;
7. teprve schválený pár může dostat finanční převod.

Pokud je identita nejasná, nic se nepřipíše. Záznam zůstane ve stavu
`NeedsReview` a lze jej vyřešit později.

## Párovací tabulka

Offline pracovní tabulka má obsahovat pouze údaje potřebné k review:

- SafeQ user ID;
- SafeQ login;
- SafeQ zobrazované jméno;
- první/poslední tiskovou aktivitu;
- review kohortu podle stáří;
- kontrolní flagy;
- aktuální SafeQ zůstatek z finálního balance snapshotu;
- identifikaci tohoto snapshotu/data;
- cílové FUA Pay `UserId`;
- zobrazené FUA Pay jméno/e-mail pouze pro review;
- stav párování;
- schvalujícího administrátora a čas;
- import status / batch ID;
- poznámku.

Historické tiskové ceny mohou být v pracovní tabulce jen jako diagnostická
informace a musí být viditelně označené jako **NENÍ KREDIT**.

Tabulka s osobními údaji zůstává mimo veřejný Git.

## Finální SafeQ freeze a balance snapshot

Aby nedošlo k dvojímu použití nebo změně zůstatku během migrace:

1. oznámit konec používání starého SafeQ kreditu;
2. zastavit nebo jinak provozně uzavřít možnost starý kredit dále utrácet či
   dobíjet;
3. až potom vytvořit finální balance export;
4. spočítat a uložit SHA-256 tohoto exportu;
5. pracovat pouze s tímto immutable snapshotem;
6. po snapshotu neprovádět další běžné finanční změny ve starém SafeQ.

Pokud starý systém nelze před snapshotem zmrazit, je nutné navrhnout samostatnou
delta reconciliation; jednoduchý jednorázový import pak nestačí.

## Finanční semantika v FUA Pay

Převod existujícího SafeQ zůstatku není nový příjem peněz v okamžiku cutoveru.
Nemá proto používat:

- běžné administrátorské ruční dobití;
- ČSOB Payment;
- falešnou zakázku;
- automatické vystavení nového `FinancialDocument`.

Preferovaná implementace je dedikovaná idempotentní operace například
`LegacySafeQCreditTransfer`.

Jeden potvrzený převod musí atomicky vytvořit:

- durable migration záznam se zdrojem `SafeQ`;
- kladný canonical credit movement;
- auditní událost;
- vazbu na SafeQ user ID, import batch/snapshot, cílový FUA Pay UserId,
  částku a schvalujícího administrátora.

Uživatelský popis kreditního pohybu má být například:

`Převod kreditu ze SafeQ`

a nesmí obsahovat technické interní GUID.

Tento převod sám nevytváří `FinancialDocument`, protože při cutoveru nevzniká
nový externí příjem. Pokud účetní/provozní vlastník vyžádá jinou evidenci
legacy zůstatků, musí být před implementací výslovně schválena jako samostatné
pravidlo.

## Idempotence a ochranné invarianty

Import musí fail-closed vynutit minimálně:

- jeden `SafeQ user ID + balance snapshot` lze finančně převést nejvýše jednou;
- replay stejného import commandu nevytvoří druhý pohyb;
- stejný legacy účet s jinou částkou je konflikt;
- cílový FUA Pay účet musí existovat a být aktivní Customer;
- částka se převádí v celých minor units CZK;
- více než dvě desetinná místa se odmítnou;
- záporný zůstatek se automaticky nepřevádí a vyžaduje ruční rozhodnutí;
- nulový zůstatek nevytváří kreditní pohyb;
- nejasné párování nikdy nevytváří finanční efekt;
- audit a credit movement vzniknou ve stejné transakční business operaci.

Více legacy SafeQ účtů směrovaných na jeden FUA Pay UserId se nesmí automaticky
sloučit. Takový případ musí být explicitně schválen a každý zdrojový SafeQ účet
zůstane samostatně dohledatelný.

## Doporučený provozní workflow

Pro první produkční rollout není nutné automaticky migrovat všech 628 účtů.

Doporučený claim-on-demand proces:

1. otevřít čistý FUA Pay;
2. vyzvat studenty a pracovníky, kteří chtějí služby používat, aby se přihlásili
   přes TUL Entra;
3. admin u nově vzniklého FUA Pay účtu dohledá legacy SafeQ kandidáta;
4. kandidáta ručně potvrdí;
5. schválený řádek se zařadí do import batch;
6. idempotentní importer provede `Převod kreditu ze SafeQ`;
7. admin i zákazník zkontrolují výsledný kreditní pohyb a zůstatek.

Recent účty lze odbavovat prioritně. Dormant účty zůstanou v immutable migračním
podkladu a mohou být vyřešeny později, pokud se jejich vlastník přihlásí.

Tento model minimalizuje riziko chybného hromadného párování a nevyžaduje
spoléhat na heuristiku jméno/příjmení.

## Implementační forma

Nejmenší důstojná varianta je:

- offline párovací tabulka pro review;
- schválený import CSV obsahující jen stabilní identifikátory a částku;
- malý repository-owned one-time importer nebo application command, který
  používá stejné transakční a auditní hranice jako zbytek FUA Pay;
- durable tabulka importů v FUA Pay pro idempotenci a budoucí dohledatelnost.

Importer nesmí být obecný SQL skript, který přímo přidává řádky do
`credits.movements`. Musí respektovat aplikační invarianty a vytvořit
auditovatelnou business operaci.

Po uzavření migračního období může být write cesta odstraněna/zakázána, ale
durable evidence již provedených převodů zůstane.

## Acceptance před použitím

Před prvním skutečným převodem se automaticky a ručně ověří:

- validní transfer přidá přesnou částku právě jednou;
- replay nepřidá nic;
- jiná částka pro stejný SafeQ source je konflikt;
- neexistující/neaktivní Customer je odmítnut;
- nulová/záporná/neplatná částka neprojde automaticky;
- transakční chyba nezanechá movement bez migration evidence ani naopak;
- Customer vidí pouze lidský popis `Převod kreditu ze SafeQ`;
- Administrator vidí auditní vazbu na legacy zdroj a schvalujícího admina;
- `FinancialDocument` se při legacy transferu nevytvoří;
- součet schválených importovaných částek se shoduje s import reportem;
- source snapshot hash a batch ID jsou archivované mimo veřejný Git.
