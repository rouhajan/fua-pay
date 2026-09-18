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
        Assert.Equal(0, (int)FinancialDocumentTaxTreatment.Unknown);
        Assert.Equal(
            1,
            (int)FinancialDocumentTaxTreatment.StandardRateIncluded);
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
            new FinancialDocumentTaxSnapshot(
                FinancialDocumentTaxTreatment.StandardRateIncluded,
                2_100,
                10_202,
                2_143),
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
            2,
            2);

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
    public void CreateManualCreditTopUp_UsesCanonicalShapeAndVersions()
    {
        var documentId = Guid.NewGuid();
        var commandId = Guid.NewGuid();
        var customer = new FinancialDocumentCustomerSnapshot(
            Guid.NewGuid(),
            "Customer",
            "customer@example.test");

        var document = FinancialDocument.CreateManualCreditTopUp(
            documentId,
            "FUA-2026-000001",
            commandId,
            customer,
            2_500,
            "CZK",
            IssuedAt.AddMinutes(-1),
            IssuedAt,
            CreateTestIssuer(),
            FinancialDocumentTaxPolicy.CreateApprovedSnapshot(2_500));

        Assert.Equal(documentId, document.DocumentId);
        Assert.Equal(commandId, document.SourceId);
        Assert.Equal(
            FinancialDocumentType.ManualCreditTopUp,
            document.DocumentType);
        Assert.Equal(
            FinancialDocumentSourceType.ManualCreditTopUp,
            document.SourceType);
        Assert.Equal(
            FinancialDocumentSettlementMethod.ManualCreditTopUp,
            document.SettlementMethod);
        Assert.Equal(
            FinancialDocument.CurrentSchemaVersion,
            document.SchemaVersion);
        Assert.Equal(
            FinancialDocument.CurrentRenderVersion,
            document.RenderVersion);
        Assert.NotNull(document.Issuer);
        Assert.NotNull(document.Tax);
        Assert.Equal(2_066, document.Tax.TaxBaseMinorUnits);
        Assert.Equal(434, document.Tax.VatAmountMinorUnits);
        Assert.Null(document.Provider);
        Assert.Null(document.Job);
    }

    [Fact]
    public void CreateCardWalletTopUp_UsesCanonicalShapeAndVersions()
    {
        var paymentId = Guid.NewGuid();
        var provider = new FinancialDocumentProviderSnapshot(
            "Csob",
            "pay-id",
            "123456789");

        var document = FinancialDocument.CreateCardWalletTopUp(
            Guid.NewGuid(),
            "FUA-2026-000001",
            paymentId,
            new FinancialDocumentCustomerSnapshot(
                Guid.NewGuid(),
                "Customer",
                "customer@example.test"),
            2_500,
            "CZK",
            IssuedAt.AddMinutes(-1),
            IssuedAt,
            CreateTestIssuer(),
            FinancialDocumentTaxPolicy.CreateApprovedSnapshot(2_500),
            provider);

        Assert.Equal(
            FinancialDocumentType.CardWalletTopUp,
            document.DocumentType);
        Assert.Equal(
            FinancialDocumentSourceType.Payment,
            document.SourceType);
        Assert.Equal(paymentId, document.SourceId);
        Assert.Equal(
            FinancialDocumentSettlementMethod.PaymentProvider,
            document.SettlementMethod);
        Assert.Equal(
            FinancialDocument.CurrentSchemaVersion,
            document.SchemaVersion);
        Assert.Equal(
            FinancialDocument.CurrentRenderVersion,
            document.RenderVersion);
        Assert.Same(provider, document.Provider);
        Assert.Null(document.Job);
    }

    [Fact]
    public void CreateDirectJobCardPayment_UsesCanonicalShapeAndVersions()
    {
        var paymentId = Guid.NewGuid();
        var provider = new FinancialDocumentProviderSnapshot(
            "Csob",
            "pay-id",
            "123456789");
        var job = new FinancialDocumentJobSnapshot(
            Guid.NewGuid(),
            "FUA-2026-000001",
            "Model",
            "Tisk modelu",
            "Dílna");

        var document = FinancialDocument.CreateDirectJobCardPayment(
            Guid.NewGuid(),
            "FUA-2026-000001",
            paymentId,
            new FinancialDocumentCustomerSnapshot(
                Guid.NewGuid(),
                "Customer",
                null),
            2_500,
            "CZK",
            IssuedAt.AddMinutes(-1),
            IssuedAt,
            CreateTestIssuer(),
            FinancialDocumentTaxPolicy.CreateApprovedSnapshot(2_500),
            provider,
            job);

        Assert.Equal(
            FinancialDocumentType.DirectJobCardPayment,
            document.DocumentType);
        Assert.Equal(
            FinancialDocumentSourceType.Payment,
            document.SourceType);
        Assert.Equal(paymentId, document.SourceId);
        Assert.Equal(
            FinancialDocumentSettlementMethod.PaymentProvider,
            document.SettlementMethod);
        Assert.Equal(
            FinancialDocument.CurrentSchemaVersion,
            document.SchemaVersion);
        Assert.Equal(
            FinancialDocument.CurrentRenderVersion,
            document.RenderVersion);
        Assert.Same(provider, document.Provider);
        Assert.Same(job, document.Job);
    }

    [Fact]
    public void CreateDirectJobCardPayment_RequiresProviderAndJobSnapshots()
    {
        var customer = new FinancialDocumentCustomerSnapshot(
            Guid.NewGuid(),
            "Customer",
            null);
        var issuer = CreateTestIssuer();
        var tax = FinancialDocumentTaxPolicy.CreateApprovedSnapshot(2_500);
        var provider = new FinancialDocumentProviderSnapshot(
            "Csob",
            "pay-id",
            "123456789");
        var job = new FinancialDocumentJobSnapshot(
            Guid.NewGuid(),
            "FUA-2026-000001",
            "Model",
            "Tisk modelu",
            "Dílna");

        Assert.Throws<ArgumentException>(
            () => FinancialDocument.CreateDirectJobCardPayment(
                Guid.NewGuid(),
                "FUA-2026-000001",
                Guid.NewGuid(),
                customer,
                2_500,
                "CZK",
                IssuedAt,
                IssuedAt,
                issuer,
                tax,
                null!,
                job));
        Assert.Throws<ArgumentException>(
            () => FinancialDocument.CreateDirectJobCardPayment(
                Guid.NewGuid(),
                "FUA-2026-000001",
                Guid.NewGuid(),
                customer,
                2_500,
                "CZK",
                IssuedAt,
                IssuedAt,
                issuer,
                tax,
                provider,
                null!));
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
                null,
                1,
                1));
    }

    [Theory]
    [InlineData(1, 1, 0)]
    [InlineData(100, 83, 17)]
    [InlineData(2, 2, 0)]
    [InlineData(3, 2, 1)]
    [InlineData(120, 99, 21)]
    [InlineData(121, 100, 21)]
    [InlineData(12_100, 10_000, 2_100)]
    [InlineData(12_345, 10_202, 2_143)]
    [InlineData(
        long.MaxValue,
        7_622_621_518_061_798_188,
        1_600_750_518_792_977_619)]
    public void TaxPolicy_CalculatesDeterministicInclusiveBreakdown(
        long gross,
        long expectedBase,
        long expectedVat)
    {
        var tax = FinancialDocumentTaxPolicy.CreateApprovedSnapshot(gross);

        Assert.Equal(expectedBase, tax.TaxBaseMinorUnits);
        Assert.Equal(expectedVat, tax.VatAmountMinorUnits);
        Assert.Equal(gross, tax.TaxBaseMinorUnits + tax.VatAmountMinorUnits);
        Assert.Equal(2_100, tax.VatRateBasisPoints);
    }

    [Fact]
    public void Constructor_V2RejectsMissingOrInconsistentTaxSnapshot()
    {
        var validTax = FinancialDocumentTaxPolicy.CreateApprovedSnapshot(100);

        Assert.Throws<ArgumentException>(
            () => CreateV2ManualDocument(issuer: null, tax: validTax));
        Assert.Throws<ArgumentException>(
            () => CreateV2ManualDocument(issuer: CreateTestIssuer(), tax: null));
        Assert.Throws<ArgumentException>(
            () => CreateV2ManualDocument(
                CreateTestIssuer(),
                new FinancialDocumentTaxSnapshot(
                    FinancialDocumentTaxTreatment.StandardRateIncluded,
                    2_100,
                    9_000,
                    3_100),
                12_100));
    }

    [Theory]
    [InlineData(1, 2)]
    [InlineData(2, 1)]
    [InlineData(3, 2)]
    [InlineData(2, 3)]
    [InlineData(3, 1)]
    [InlineData(1, 3)]
    [InlineData(3, 3)]
    public void Constructor_RejectsMixedAndFutureVersionPairs(
        int schemaVersion,
        int renderVersion)
    {
        Assert.Throws<ArgumentException>(
            () => CreateManualDocumentForVersion(
                schemaVersion,
                renderVersion));
    }

    [Fact]
    public void Constructor_AcceptsBothSupportedVersionPairs()
    {
        var legacy = CreateManualDocumentForVersion(1, 1);
        var current = CreateManualDocumentForVersion(2, 2);

        Assert.Equal((1, 1), (legacy.SchemaVersion, legacy.RenderVersion));
        Assert.Equal((2, 2), (current.SchemaVersion, current.RenderVersion));
    }

    private static FinancialDocument CreateV2ManualDocument(
        FinancialDocumentIssuerSnapshot? issuer,
        FinancialDocumentTaxSnapshot? tax,
        long amountMinorUnits = 100) =>
        new(
            Guid.NewGuid(),
            "FUA-2026-000001",
            FinancialDocumentType.ManualCreditTopUp,
            FinancialDocumentSourceType.ManualCreditTopUp,
            Guid.NewGuid(),
            new FinancialDocumentCustomerSnapshot(
                Guid.NewGuid(),
                "Zákazník",
                null),
            amountMinorUnits,
            "CZK",
            IssuedAt,
            IssuedAt,
            FinancialDocumentSettlementMethod.ManualCreditTopUp,
            issuer,
            tax,
            null,
            null,
            2,
            2);

    private static FinancialDocument CreateManualDocumentForVersion(
        int schemaVersion,
        int renderVersion)
    {
        const long amountMinorUnits = 100;
        var isCurrent = schemaVersion == 2 && renderVersion == 2;

        return new FinancialDocument(
            Guid.NewGuid(),
            "FUA-2026-000001",
            FinancialDocumentType.ManualCreditTopUp,
            FinancialDocumentSourceType.ManualCreditTopUp,
            Guid.NewGuid(),
            new FinancialDocumentCustomerSnapshot(
                Guid.NewGuid(),
                "Zákazník",
                null),
            amountMinorUnits,
            "CZK",
            IssuedAt,
            IssuedAt,
            FinancialDocumentSettlementMethod.ManualCreditTopUp,
            isCurrent ? CreateTestIssuer() : null,
            isCurrent
                ? FinancialDocumentTaxPolicy.CreateApprovedSnapshot(
                    amountMinorUnits)
                : null,
            null,
            null,
            schemaVersion,
            renderVersion);
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
