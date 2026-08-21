# Bezpečnostní ověření FUA Pay – 2026-08-21

Revision:

`b90d2f5b5665e2a1547383fb32de0e519208ec89`

Staging release:

`/opt/fuapay/releases/b90d2f5b5665`

Reference: OWASP ASVS 5.0.0 L2, OWASP Top 10:2025 a
NIST SP 800-218 SSDF 1.1.

Nejde o formální ASVS certifikaci.

## Výsledek

**GO pro současný code/security/staging baseline.**

| Oblast | Stav |
|---|---|
| Build, testy, EF kontrola a Linux publish | PASS |
| PostgreSQL integrační gate | PASS |
| NuGet vulnerability audit | PASS |
| Protected `main`, required CI, Dependabot | PASS |
| CodeQL C# a GitHub Actions | PASS |
| Autorizace a ownership kontroly | PASS |
| CSRF a změnové requesty | PASS |
| HTTP response hardening | PASS |
| Finanční idempotence a concurrency | PASS |
| Staging runtime perimeter | PASS |
| Entra production acceptance | EXTERNAL / PENDING |
| MFA / Conditional Access | EXTERNAL |
| ČSOB production acceptance | EXTERNAL / PENDING |
| Provozní alerting a incidentní postup | PENDING |
| Production backup / restore | PENDING |

## Opravené nálezy

- Entra failure redirect používá důvěryhodný application root.
- HSTS používá roční `max-age`.
- CSP používá `base-uri 'none'`.
- Autentizované dynamické odpovědi používají `no-store`.
- Administrativní CSV exporty jsou POST-only a používají antiforgery.

## Vědomá rozhodnutí

Bez konkrétního požadavku se nezavádí:

- vlastní MFA nad TUL Entra;
- server-side session store;
- plošný rate limiting administrace;
- `__Host-` cookie prefix při podporovaném `Hosting:PathBase`;
- HSTS `includeSubDomains` bez potvrzeného rozsahu DNS.

## Před production GO

Zbývá:

1. TUL Entra live login, logout a claims;
2. ověřit stav MFA / Conditional Access na TUL IdP;
3. ČSOB production konfigurace a klíče;
4. `/etc/fuapay/production.env` a ACL tajemství;
5. PostgreSQL backup a restore;
6. log retention, alerting a incidentní kontakt;
7. production smoke test.
