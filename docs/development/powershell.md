# Windows PowerShell 5.1

Lokální ověřovací skripty FUA Pay musí fungovat také ve Windows
PowerShell 5.1. PowerShell 7 není podmínkou lokálního vývoje.

## Kódování PowerShell skriptů

Windows PowerShell 5.1 nemusí správně rozpoznat UTF-8 zdrojový
soubor bez BOM. U skriptu s českými texty pak dochází k poškození
diakritiky už při načtení zdrojového souboru.

Proto repozitář pro soubory `*.ps1` používá:

```ini
[*.ps1]
charset = utf-8-bom
```

`scripts/verify.ps1` musí zůstat uložený jako UTF-8 s BOM.
Nejde o změnu logiky ověřování, ale o kompatibilitu s Windows
PowerShell 5.1.

## Spuštění ověření

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass `
    -File .\scripts\verify.ps1
```

České texty musí zůstat čitelné, například:

`Ověření FUA Pay prošlo.`
