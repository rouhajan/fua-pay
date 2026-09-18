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
Má se evidovat jako převod/oprava počátečního stavu, ne jako nové dobití.

Výchozí produkční postup proto používá existující **Administrativní korekci**:

- kladná korekce připíše převáděný SafeQ zůstatek;
- nevzniká ČSOB Payment;
- nevzniká falešná zakázka;
- nevzniká nový `FinancialDocument`;
- operace zůstává neměnným kreditním pohybem a je auditovaná.

Důvod korekce musí jednoznačně říkat, že jde o legacy převod, například:

`Převod zůstatku ze SafeQ při migraci, SafeQ ID <id>, snapshot <id>`

Zákaznické UI nemá zobrazovat technické GUID ani interní identifikátory. Pro
zákazníka má být pohyb prezentovaný lidsky jako `Převod zůstatku ze SafeQ`.

Pokud účetní/provozní vlastník před cutoverem stanoví jinou evidenční semantiku,
musí být výslovně schválena a tento dokument se před prvním převodem upraví.

## Ochranné invarianty

Párovací nástroj je read-only. Samotný finanční převod vzniká až po explicitním
lidském schválení konkrétního páru a následném provedení administrativní
korekce.

Platí minimálně:

- cílový FUA Pay účet musí už existovat po legitimním Entra přihlášení;
- SafeQ login/jméno nikdy nejsou automatickou autoritou identity;
- nejasné párování nevytváří finanční efekt;
- částka pochází výhradně z finálního SafeQ balance snapshotu;
- nulový zůstatek není potřeba zapisovat;
- záporný nebo jinak nestandardní zůstatek vyžaduje samostatné ruční rozhodnutí;
- jeden SafeQ účet se nesmí bez výslovného review převést vícekrát;
- více SafeQ účtů pro jeden FUA Pay účet se nesmí automaticky sloučit;
- schválené párování a provedená korekce musí být dohledatelné v pracovní
  migrační evidenci.

## Doporučený provozní workflow

Pro první produkční rollout se předem nepřipravuje kredit nikomu, kdo se do
čistého FUA Pay ještě nepřihlásil.

Výchozí claim-on-demand proces s cílem odbavit člověka do 24–48 hodin:

1. student nebo pracovník se sám přihlásí do FUA Pay přes TUL Entra;
2. JIT vytvoří standardní Customer účet a stabilní FUA Pay `UserId`;
3. administrátor kdykoli spustí read-only párování **aktuálně existujících FUA
   Pay Customer účtů** proti SafeQ migračnímu podkladu;
4. nástroj u každého nepřevedeného FUA Pay účtu nabídne SafeQ kandidáty a
   rozdělí je například na `JASNÉ`, `KONTROLA` a `BEZ SHODY`;
5. skóre může využít přesný login/e-mailový local-part, normalizované tokeny
   jména, diakritiku, různé pořadí částí jména a jednoznačnost kandidáta;
6. i `JASNÉ` shody se pouze předvyberou - nic se finančně nepřipíše bez
   lidského potvrzení;
7. administrátor jasné shody rychle vizuálně prolétne a schválí; nejasné řeší
   po jednom jako `ano / ne / odložit`;
8. pro schválené páry se z finálního SafeQ balance snapshotu vezme přesná částka;
9. převod se provede existující **Administrativní korekcí** s důvodem
   `Převod zůstatku ze SafeQ...`;
10. pracovní tabulka zaznamená, že konkrétní SafeQ účet byl spárován, schválen a
    převeden;
11. zákazník následně vidí nový zůstatek a lidsky pojmenovaný kreditní pohyb.

Párování lze spouštět opakovaně každý den. Nově přihlášení uživatelé se pouze
objeví mezi kandidáty; uživatel, který se nikdy nepřihlásil, se vůbec neřeší.

Recent účty lze odbavovat prioritně. Dormant účty zůstávají v migračním podkladu
a mohou být řešeny až ve chvíli, kdy se jejich vlastník skutečně přihlásí.

Heuristika musí počítat s víceslovnými a kulturně odlišnými jmény, změnou pořadí
částí jména, diakritikou a historicky nepravidelnými SafeQ loginy. Jejím cílem
je zrychlit lidské review, ne nahradit autoritu Entra identity.

## Implementační forma

Výchozí varianta záměrně nepřidává žádné nové migrační UI, endpoint ani
produkční databázovou tabulku.

Stačí:

- offline SafeQ párovací tabulka mimo veřejný Git;
- malý repository-owned **read-only matching/report tool**, který načte
  existující FUA Pay Customer účty a porovná je se SafeQ podkladem;
- výstup s kandidáty, vysvětlením shody a stavem
  `JASNÉ / KONTROLA / BEZ SHODY`;
- lidské potvrzení páru administrátorem;
- samotný finanční zápis přes již existující administrativní korekci.

Pro nižší stovky legacy účtů je ruční provedení schválených korekcí přijatelný
a nejméně invazivní výchozí model.

Pokud by objem v prvních dnech ukázal, že ruční přepis schválených částek je
nepraktický, lze později doplnit jednorázový batch/CLI nástroj. Ten ale smí
automatizovat pouze **již schválené páry** a musí použít stejnou semantiku
administrativní korekce; nesmí zavádět automatické rozhodování identity.

## Acceptance před použitím

Před prvním skutečným převodem se ověří minimálně:

- matching tool je read-only vůči finančním datům;
- zobrazí pouze FUA Pay účty, které už skutečně existují po Entra přihlášení;
- přesná shoda jména/loginu sama nic nepřipíše;
- nejednoznačné případy zůstanou k ručnímu rozhodnutí;
- schválený pár lze jednoznačně dohledat zpět na SafeQ user ID a balance
  snapshot;
- administrativní korekce připíše přesně schválenou částku;
- korekce nevytvoří `FinancialDocument`;
- audit zachytí administrátora a důvod legacy převodu;
- Customer UI nezobrazuje technické GUID a ukazuje lidský význam
  `Převod zůstatku ze SafeQ`;
- pracovní migrační evidence zabrání nechtěnému druhému převodu téhož SafeQ
  účtu;
- součet skutečně provedených převodů lze porovnat se součtem schválených
  položek.

