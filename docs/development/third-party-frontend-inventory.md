# Third-party frontend inventory

## Účel

Tento dokument eviduje frontendové knihovny uložené přímo v repozitáři pod
`src/FuaPay.Web/wwwroot/lib`. V repozitáři zůstávají jen soubory načítané
aplikací za běhu a odpovídající licence; ostatní součásti upstream distribucí se
nepublikují. Vendored runtime soubory se neupravují kosmetickými změnami.

## Evidované knihovny

| Produkt | Verze | Licence | Zdroj | Runtime soubor | Způsob aktualizace |
|---|---:|---|---|---|---|
| jQuery | 3.7.1 | MIT | `https://github.com/jquery/jquery/releases` | `wwwroot/lib/jquery/dist/jquery.min.js` | Stáhnout oficiální release, ponechat runtime soubor a licenci, ověřit verzi a spustit úplnou bránu. |
| jQuery Validation | 1.21.0 | MIT | `https://github.com/jquery-validation/jquery-validation/releases` | `wwwroot/lib/jquery-validation/dist/jquery.validate.min.js` | Stáhnout oficiální release, ponechat runtime soubor a licenci, ověřit verzi a spustit úplnou bránu. |
| jQuery Validation Unobtrusive | 4.0.0 | MIT | `https://github.com/aspnet/jquery-validation-unobtrusive/releases` | `wwwroot/lib/jquery-validation-unobtrusive/dist/jquery.validate.unobtrusive.min.js` | Stáhnout oficiální release, ponechat runtime soubor a licenci, ověřit verzi a spustit úplnou bránu. |

## Pravidla

- Každá knihovna musí mít v adresáři odpovídající licenční soubor.
- `wwwroot/lib` smí obsahovat pouze runtime soubory uvedené výše a jejich licence.
- Vendored soubory se neformátují a nepřevádějí se jim konce řádků.
- Aktualizace knihovny je samostatná dohledatelná změna; nesmí být přimíchána k
  nesouvisející funkci.
- Po aktualizaci se ověří klientská validace formulářů a načtení skriptů bez
  chyb v konzoli prohlížeče.
