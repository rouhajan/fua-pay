using FuaPay.Web.Modules.FinancialDocuments.Domain;

namespace FuaPay.Web.Tests.Modules.FinancialDocuments.Domain;

public sealed class FinancialDocumentTests
{
    private static readonly DateTimeOffset IssuedAt =
        new(2026, 9, 16, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void PersistedEnums_PreserveNumericValues()
    {
        Assert.Equal(0, (int)FinancialDocumentType.Unknown);
        Assert.Equal(1, (int)FinancialDocumentType.ManualCreditTopUp);
        Assert.Equal(2, (int)FinancialDocumentType.CardWalletTopUp);
        Assert.Equal(3, (int)FinancialDocumentType.DirectJobCardPayment);
        Assert.Equal(0, (int)FinancialDocumentSourceType.Unknown);
        Assert.Equal(1, (int)FinancialDocumentSourceType.ManualCreditTopUp);
        Assert.Equal(2, (int)FinancialDocumentSourceType.Payment);
        Assert.Equal(0, (int)FinancialDocumentSettlementMethod.Unknown);
        Assert.Equal(
            1,
            (int)FinancialDocumentSettlementMethod.ManualCreditTopUp);
        Assert.Equal(
            2,
            (int)FinancialDocumentSettlementMethod.PaymentProvider);
    }

    [Fact]
    public void Constructor_PreservesImmutableSnapshot()
    {
        var documentId = Guid.NewGuid();
        var sourceId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var jobId = Guid.NewGuid();

        var document = new FinancialDocument(
            documentId,
            " fua-2026-000001 ",
            FinancialDocumentType.DirectJobCardPayment,
            FinancialDocumentSourceType.Payment,
            sourceId,
            new FinancialDocumentCustomerSnapshot(
                customerId,
                " Zákazník ",
                " customer@example.test "),
            12_345,
            " czk ",
            IssuedAt.AddMinutes(-1),
            IssuedAt,
            FinancialDocumentSettlementMethod.PaymentProvider,
            CreateTestIssuer(),
            new FinancialDocumentProviderSnapshot(
                "ČSOB",
                "pay-id",
                "123456789"),
            new FinancialDocumentJobSnapshot(
                jobId,
                "FUA-2026-000001",
                "Model",
                "Tisk modelu",
                "Dílna"),
            1,
            1);

        Assert.Equal(documentId, document.DocumentId);
        Assert.Equal("FUA-2026-000001", document.DocumentNumber);
        Assert.Equal(
            FinancialDocumentType.DirectJobCardPayment,
            document.DocumentType);
        Assert.Equal(sourceId, document.SourceId);
        Assert.Equal("Zákazník", document.Customer.DisplayName);
        Assert.Equal("customer@example.test", document.Customer.Email);
        Assert.Equal(12_345, document.AmountMinorUnits);
        Assert.Equal("CZK", document.Currency);
        Assert.Equal("TEST-REGISTRATION", document.Issuer!.RegistrationNumber);
        Assert.Equal("TEST-VAT", document.Issuer.VatNumber);
        Assert.Equal("pay-id", document.Provider!.Reference);
        Assert.Equal(jobId, document.Job!.JobId);
    }

    [Fact]
    public void Constructor_ManualTopUpRejectsInventedProviderOrJob()
    {
        Assert.Throws<ArgumentException>(
            () => new FinancialDocument(
                Guid.NewGuid(),
                "FUA-2026-000001",
                FinancialDocumentType.ManualCreditTopUp,
                FinancialDocumentSourceType.ManualCreditTopUp,
                Guid.NewGuid(),
                new FinancialDocumentCustomerSnapshot(
                    Guid.NewGuid(),
                    "Zákazník",
                    null),
                100,
                "CZK",
                IssuedAt,
                IssuedAt,
                FinancialDocumentSettlementMethod.ManualCreditTopUp,
                null,
                new FinancialDocumentProviderSnapshot(
                    "Neexistující provider",
                    null,
                    null),
                null,
                1,
                1));
    }

    [Fact]
    public void Constructor_PaymentRequiresProviderIdentity()
    {
        Assert.Throws<ArgumentException>(
            () => new FinancialDocument(
                Guid.NewGuid(),
                "FUA-2026-000001",
                FinancialDocumentType.CardWalletTopUp,
                FinancialDocumentSourceType.Payment,
                Guid.NewGuid(),
                new FinancialDocumentCustomerSnapshot(
                    Guid.NewGuid(),
                    "Zákazník",
                    null),
                100,
                "CZK",
                IssuedAt,
                IssuedAt,
                FinancialDocumentSettlementMethod.PaymentProvider,
                null,
                null,
                null,
                1,
                1));
    }

    [Fact]
    public void Constructor_RejectsDocumentTypeSourceMismatch()
    {
        Assert.Throws<ArgumentException>(
            () => new FinancialDocument(
                Guid.NewGuid(),
                "FUA-2026-000001",
                FinancialDocumentType.ManualCreditTopUp,
                FinancialDocumentSourceType.Payment,
                Guid.NewGuid(),
                new FinancialDocumentCustomerSnapshot(
                    Guid.NewGuid(),
                    "Zákazník",
                    null),
                100,
                "CZK",
                IssuedAt,
                IssuedAt,
                FinancialDocumentSettlementMethod.ManualCreditTopUp,
                null,
                null,
                null,
                1,
                1));
    }

    private static FinancialDocumentIssuerSnapshot CreateTestIssuer() =>
        new(
            "TEST ISSUER - NOT PRODUCTION",
            "TEST UNIT",
            "TEST ADDRESS LINE 1",
            "TEST ADDRESS LINE 2",
            "TEST COUNTRY",
            "TEST-REGISTRATION",
            "TEST-VAT",
            "issuer@example.test");
}
