# Microsoft Entra ID

## Implementovaný tok

FUA Pay používá [oficiální ASP.NET Core OpenID Connect
middleware](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/configure-oidc-web-authentication?view=aspnetcore-10.0)
jako tenant-specific důvěrný webový klient nad [Microsoft identity platform
OIDC](https://learn.microsoft.com/en-us/entra/identity-platform/v2-protocols-oidc):

```text
Entra authorization code + PKCE
→ validace protokolu, issueru a podpisu
→ přesné claims tid + oid
→ existující externí vazba nebo JIT Customer
→ lokální FUA Pay cookie a lokální role
```

Authority má tvar `https://login.microsoftonline.com/{tenantId}/v2.0`.
Response type je `code`, response mode `form_post`, metadata vyžadují HTTPS a
tokeny se neukládají. Požadované scopes jsou jen `openid profile email`.
Microsoft Graph se nepoužívá.

`tid` a `oid` musí být právě jednou přítomná neprázdná GUID a `tid` musí
odpovídat konfiguraci. Interní klíč je:

```text
microsoft-entra / tenant GUID / object GUID
```

`name`, případně `preferred_username`, je jen zobrazované jméno. Claim `email`
je volitelný profilový údaj. Změna těchto hodnot nemění identitu ani oprávnění.
Entra role a skupiny se do lokálních rolí nepřenášejí.

## JIT a existující účty

Neznámý stabilní klíč vytvoří interního uživatele a roli Customer. Unikátní
primární klíč externí identity řeší souběžná první přihlášení: druhý pokus
načte již vytvořený účet.

Na stránce Admin → Uživatelé může Administrator připojit přesné Entra object
ID k existujícímu účtu nakonfigurovaného tenantu. Operace je auditovaná a
databáze vynucuje:

- jedno `tenant + oid` nejvýše pro jeden FUA Pay účet;
- nejvýše jednu Entra identitu daného tenantu pro jeden FUA Pay účet.

Propojení nemění interní `UserId`, kredit, zakázky, platby ani role. Jméno a
e-mail nejsou párovací klíče. SafeQ import ani automatické slučování nejsou
implementované, protože zdrojová data a schválená pravidla nebyla dodána.

## Konfigurace aplikace

Tajemství patří do deployment secret store, ne do `appsettings.json`:

```text
Entra__Enabled=true
Entra__TenantId=<TUL tenant GUID>
Entra__ClientId=<app registration client GUID>
Entra__ClientSecret=<secret value, ne secret ID>
Entra__CallbackPath=/signin-oidc
Entra__SignedOutCallbackPath=/signout-callback-oidc
```

V Production je Entra povinná. Chybějící/neplatná hodnota zastaví startup.
Entra a interaktivní vývojové/testovací přihlášení nesmějí být zapnuté
současně.

## Aktuální živý stav

Microsoft Entra přihlášení je na `https://fuapay.tul.cz` živě nasazené a
používané. App registration, Tenant ID, Client ID a přihlašovací redirect tedy
nejsou otevřené onboarding úkoly.

Provozní pravidla zůstávají:

- client secret patří mimo Git/release a musí se řízeně rotovat;
- identita se stále váže podle `tid + oid`, ne podle jména nebo e-mailu;
- Microsoft Graph permissions ani directory scopes nejsou pro FUA Pay potřeba;
- při změně Entra konfigurace nebo produkčním cutoveru se má pouze znovu
  ověřit již fungující login/logout a lokální role, nikoli znovu otevírat
  registraci aplikace jako nedokončenou funkci.

Jednotlivé scénáře, například ruční propojení konkrétního existujícího účtu,
se mají evidovat jen tehdy, pokud pro ně existuje konkrétní neověřený případ;
nejsou důkazem, že samotná Entra integrace není živá.
