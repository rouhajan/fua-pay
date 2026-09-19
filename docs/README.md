# Dokumentace FUA Pay

Aktuální dokumentace je záměrně malá:

- [architektura a produktové hranice](architecture.md);
- [Microsoft Entra ID](integrations/entra-id.md);
- [FUA Print Payments API](integrations/fuaprint-payments-api.md);
- [ČSOB eAPI](integrations/csob.md);
- [vrácení finančního vypořádání](features/payment-returns.md);
- [ruční dobití kreditu](features/manual-credit-topups.md);
- [finanční doklady](features/financial-documents.md);
- [PDF potvrzení o úhradě](features/payment-receipts.md);
- [PostgreSQL, EF migrace a testy](development/database.md);
- [produkční konfigurace a provoz](deployment/production-configuration.md);
- [čistý produkční cutover](deployment/production-cutover-plan.md);
- [legacy SafeQ kreditní migrace](deployment/legacy-safeq-credit-migration.md);
- [release a databázové artefakty](deployment/release-artifacts.md);
- [demo / staging deployment](deployment/demo-staging.md);
- [ověření 2026-08-18](testing/verification-2026-08-18.md);
- [security servicing 2026-08-20](testing/verification-2026-08-20.md);
- [GitHub repository hardening 2026-08-20](testing/github-repository-hardening-2026-08-20.md);
- [bezpečnostní ověření 2026-08-21](testing/security-verification-2026-08-21.md);
- [ověření serializace kreditního účtu 2026-08-25](testing/credit-account-locking-verification-2026-08-25.md);
- [ověření lifecycle rezervací tisku 2026-08-26](testing/print-reservation-lifecycle-verification-2026-08-26.md);
- [ověření FUA Print Payments API 2026-08-27](testing/fuaprint-payments-api-verification-2026-08-27.md);
- [ČSOB expiry acceptance 2026-09-14](testing/csob-expiry-acceptance-2026-09-14.md);
- [ověření persistentního FUA Print credentialu 2026-09-15](testing/print-credential-verification-2026-09-15.md);
- [staging acceptance ručního dobití kreditu 2026-09-15](testing/manual-credit-topup-acceptance-2026-09-15.md);
- [FinancialDocuments v2 Stage D staging acceptance 2026-09-18](testing/financial-documents-v2-stage-d-staging-acceptance-2026-09-18.md);
- [ověření zákaznického popisu tiskového debit pohybu 2026-09-19](testing/print-credit-movement-description-verification-2026-09-19.md);
- [bezpečnost](../SECURITY.md);
- [vendored frontendové závislosti](development/third-party-frontend-inventory.md).
