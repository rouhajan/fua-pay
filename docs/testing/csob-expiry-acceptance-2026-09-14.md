# ČSOB expiry acceptance – 2026-09-14

## Rozsah

Tento closeout dokumentuje samostatné staging acceptance ověření expiry větve
ČSOB plateb po hardeningu v PR #44. Nejde o součást persistentního FUA Print
credential feature a nemá se s ním slučovat.

Ověřovaný staging build odpovídal merge commitu
`bc868276aead1b350033303ce8e80bc6ce9a5894` (`Merge pull request #44 ... fix:
harden CSOB expired payment reconciliation`). Pozdější `main` už obsahoval další
změny; ty nebyly součástí tohoto acceptance scénáře.

Před samotným expiry čekáním bylo znovu prokázáno, že autentizované merchant eAPI
volání ČSOB funguje a `payment/init` je funkční. To obnovilo možnost provést čistý
end-to-end expiry test po předchozím incidentu.

## Fresh payment scénář

Byla vytvořena nová platba:

- interní payment ID: `a5ca6847-4021-43ac-a7f5-9a71630c7556`;
- částka: 100 CZK;
- provider reference: `dfbff477a0b0@LI`.

Browser se z platební brány vrátil po `1806.001 s`, tedy přibližně 30 minut a
6 sekund od vytvoření platby.

Browser return evidence:

- `resultCode = 130`;
- `paymentStatus = 6`.

Následný podepsaný server-side status check vrátil:

- `resultCode = 0`;
- `paymentStatus = 6`.

FUA Pay po reconciliation uložil interní stav `Expired`.

## Finanční invarianty

Expiry scénář nevytvořil žádný kreditní pohyb a nepřipsal hodnotu zákazníkovi.
Zůstatek po testu zůstal 20 000 minor units, tedy 200 CZK. Nebyl uložen ani
obecný failure text nahrazující explicitní expiry stav.

Výsledek je proto pro 30min expiry acceptance čistý PASS:

| Kontrola | Výsledek |
|---|---|
| ČSOB merchant autentizace | PASS |
| `payment/init` | PASS |
| Browser return po expiry okně | PASS – 1806.001 s |
| Browser evidence | PASS – `130/6` |
| Podepsaný server status | PASS – `0/6` |
| Interní FUA Pay stav | PASS – `Expired` |
| Credit movement | PASS – žádný |
| Zůstatek | PASS – beze změny, 200 CZK |

## Interpretace předchozího incidentu

Dřívější výpadek je konzistentní s externím ČSOB merchant/gateway incidentem,
ale tento closeout neprokazuje jeho root cause. Nesmí se proto dokumentovat jako
jistota, že příčina byla na straně ČSOB. Prokázáno je pouze to, že po obnovení
merchant eAPI fungoval nový init i následný expiry/reconciliation scénář.

## Hranice acceptance

Tento důkaz ověřuje konkrétní staging expiry scénář a finanční fail-safe
chování. Neprokazuje production deployment pozdějšího `main`, persistentní print
credential feature ani jiné změny, které byly mergeovány následně.
