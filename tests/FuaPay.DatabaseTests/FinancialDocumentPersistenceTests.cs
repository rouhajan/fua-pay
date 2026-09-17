using FuaPay.Web.BuildingBlocks.Persistence;
using FuaPay.Web.Modules.FinancialDocuments.Application;
using FuaPay.Web.Modules.FinancialDocuments.Domain;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Npgsql;

namespace FuaPay.DatabaseTests;

public sealed class FinancialDocumentPersistenceTests :
    IClassFixture<WebApplicationFactory<Program>>
{
    private const int ConcurrentYear = 2090;
    private const int SharedYear = 2091;
    private const int BoundaryBeforeYear = 2092;
    private const int BoundaryAfterYear = 2093;
    private const int RollbackYear = 2094;
    private const int FirstAllocationRaceYear = 2095;
    private const int PersistenceYear = 2096;
    private const int PaymentPersistenceYear = 2097;
    private const int DocumentNumberUniqueYear = 2098;
    private const int ExhaustionYear = 2099;
    private const int AmbiguousPersistenceYear = 2100;
    private const int TaxConstraintYear = 2101;

    private readonly WebApplicationFactory<Program> _factory;

    public FinancialDocumentPersistenceTests(
        WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Queries_EnforceCustomerOwnershipAndAdminCanReadById()
    {
        var sourceId = Guid.NewGuid();
        var document = CreateManualDocument(
            "FUA-2096-900001",
            sourceId,
            UtcInstant(PersistenceYear, 5, 1, 10, 0));

        try
        {
            await PersistAsync(document);
            using var scope = _factory.Services.CreateScope();
            var queries = scope.ServiceProvider
                .GetRequiredService<IFinancialDocumentQueries>();

            var owner = await queries.FindByIdForCustomerAsync(
                document.DocumentId,
                document.Customer.CustomerUserId);
            var foreign = await queries.FindByIdForCustomerAsync(
                document.DocumentId,
                Guid.NewGuid());
            var admin = await queries.FindByIdForAdminAsync(document.DocumentId);
            var missing = await queries.FindByIdForAdminAsync(Guid.NewGuid());

            Assert.Equal(document.DocumentId, owner?.DocumentId);
            Assert.Null(foreign);
            Assert.Equal(document.DocumentId, admin?.DocumentId);
            Assert.Null(missing);
            Assert.Empty(scope.ServiceProvider
                .GetRequiredService<FuaPayDbContext>()
                .ChangeTracker.Entries());
        }
        finally
        {
            await DeleteDocumentsBySourceAsync(sourceId);
        }
    }

    [Fact]
    public async Task TaxColumns_AreAdditiveAndConstraintEnforcesApprovedMathematicalSplit()
    {
        var sourceId = Guid.NewGuid();
        var document = CreateDirectJobPaymentDocument(
            "FUA-2101-900001",
            sourceId,
            UtcInstant(TaxConstraintYear, 5, 1, 10, 0));

        try
        {
            await PersistAsync(document);
            using var scope = _factory.Services.CreateScope();
            var dbContext = scope.ServiceProvider
                .GetRequiredService<FuaPayDbContext>();

            var nullableCount = await dbContext.Database.SqlQueryRaw<int>(
                """
                SELECT count(*)::int AS "Value"
                FROM information_schema.columns
                WHERE table_schema = 'financial_documents'
                  AND table_name = 'documents'
                  AND column_name IN (
                    'tax_treatment', 'vat_rate_basis_points',
                    'tax_base_minor_units', 'vat_amount_minor_units')
                  AND is_nullable = 'YES'
                  AND column_default IS NULL
                """).SingleAsync();
            Assert.Equal(4, nullableCount);

            var validRows = await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"""
                UPDATE financial_documents.documents
                SET amount_minor_units = 12100,
                    tax_base_minor_units = 10000,
                    vat_amount_minor_units = 2100
                WHERE document_id = {document.DocumentId}
                """);
            Assert.Equal(1, validRows);

            using var invalidScope = _factory.Services.CreateScope();
            var invalidDbContext = invalidScope.ServiceProvider
                .GetRequiredService<FuaPayDbContext>();
            var exception = await Assert.ThrowsAsync<PostgresException>(
                () => invalidDbContext.Database.ExecuteSqlInterpolatedAsync(
                    $"""
                    UPDATE financial_documents.documents
                    SET tax_base_minor_units = 9000,
                        vat_amount_minor_units = 3100
                    WHERE document_id = {document.DocumentId}
                    """));

            Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
            Assert.Equal(
                "ck_financial_documents_tax_snapshot_consistent",
                exception.ConstraintName);

            using var incompleteScope = _factory.Services.CreateScope();
            var incompleteDbContext = incompleteScope.ServiceProvider
                .GetRequiredService<FuaPayDbContext>();
            var incomplete = await Assert.ThrowsAsync<PostgresException>(
                () => incompleteDbContext.Database.ExecuteSqlInterpolatedAsync(
                    $"""
                    UPDATE financial_documents.documents
                    SET tax_treatment = NULL
                    WHERE document_id = {document.DocumentId}
                    """));

            Assert.Equal(PostgresErrorCodes.CheckViolation, incomplete.SqlState);
            Assert.Equal(
                "ck_financial_documents_tax_snapshot_consistent",
                incomplete.ConstraintName);
        }
        finally
        {
            await DeleteDocumentsBySourceAsync(sourceId);
        }
    }

    [Fact]
    public async Task LegacyV1IssuerNullDocument_RemainsPersistableAndReadableUnchanged()
    {
        var sourceId = Guid.NewGuid();
        var document = new FinancialDocument(
            Guid.NewGuid(),
            "FUA-2096-900003",
            FinancialDocumentType.ManualCreditTopUp,
            FinancialDocumentSourceType.ManualCreditTopUp,
            sourceId,
            new FinancialDocumentCustomerSnapshot(
                Guid.NewGuid(),
                "Legacy customer",
                null),
            100,
            "CZK",
            UtcInstant(PersistenceYear, 6, 1, 10, 0),
            UtcInstant(PersistenceYear, 6, 1, 10, 0),
            FinancialDocumentSettlementMethod.ManualCreditTopUp,
            null,
            null,
            null,
            null,
            1,
            1);

        try
        {
            await PersistAsync(document);
            var restored = await FindBySourceAsync(
                FinancialDocumentSourceType.ManualCreditTopUp,
                sourceId);

            Assert.Null(restored.Issuer);
            Assert.Null(restored.Tax);
            Assert.Equal(1, restored.SchemaVersion);
            Assert.Equal(1, restored.RenderVersion);
        }
        finally
        {
            await DeleteDocumentsBySourceAsync(sourceId);
        }
    }

    [Fact]
    public async Task Allocate_ConcurrentCallsProduceUniqueNumbers()
    {
        await DeleteCountersAsync(ConcurrentYear);

        try
        {
            var issuedAt = UtcInstant(ConcurrentYear, 6, 1, 12, 0);
            var first = await AllocateAsync(issuedAt);
            var allocations = await Task.WhenAll(
                Enumerable.Range(0, 8)
                    .Select(_ => AllocateAsync(issuedAt)));

            Assert.Equal(1, first.Sequence);
            Assert.Equal(8, allocations.Select(x => x.DocumentNumber).Distinct().Count());
            Assert.Equal(
                Enumerable.Range(2, 8),
                allocations.Select(x => x.Sequence).OrderBy(x => x));
        }
        finally
        {
            await DeleteCountersAsync(ConcurrentYear);
        }
    }

    [Fact]
    public async Task Allocate_DifferentDocumentTypesUseOneSharedAnnualSequence()
    {
        await DeleteCountersAsync(SharedYear);
        var manualSourceId = Guid.NewGuid();
        var paymentSourceId = Guid.NewGuid();

        try
        {
            var january = await AllocateAsync(
                UtcInstant(SharedYear, 1, 2, 8, 0));
            var december = await AllocateAsync(
                UtcInstant(SharedYear, 12, 30, 20, 0));

            Assert.Equal(SharedYear, january.BusinessYear);
            Assert.Equal(SharedYear, december.BusinessYear);
            Assert.Equal(1, january.Sequence);
            Assert.Equal(2, december.Sequence);
            Assert.Equal(1, await CountCountersAsync(SharedYear));

            await PersistAsync(
                CreateManualDocument(
                    january.DocumentNumber,
                    manualSourceId,
                    UtcInstant(SharedYear, 1, 2, 8, 0)),
                CreateWalletDocument(
                    december.DocumentNumber,
                    paymentSourceId,
                    UtcInstant(SharedYear, 12, 30, 20, 0)));

            var manual = await FindBySourceAsync(
                FinancialDocumentSourceType.ManualCreditTopUp,
                manualSourceId);
            var wallet = await FindBySourceAsync(
                FinancialDocumentSourceType.Payment,
                paymentSourceId);

            Assert.Equal(
                FinancialDocumentType.ManualCreditTopUp,
                manual.DocumentType);
            Assert.Equal(
                FinancialDocumentType.CardWalletTopUp,
                wallet.DocumentType);
            Assert.Equal(january.DocumentNumber, manual.DocumentNumber);
            Assert.Equal(december.DocumentNumber, wallet.DocumentNumber);
        }
        finally
        {
            await DeleteDocumentsBySourceAsync(
                manualSourceId,
                paymentSourceId);
            await DeleteCountersAsync(SharedYear);
        }
    }

    [Fact]
    public async Task Allocate_PragueNewYearBoundaryUsesLocalBusinessYear()
    {
        await DeleteCountersAsync(BoundaryBeforeYear, BoundaryAfterYear);

        try
        {
            var beforePragueMidnight = new DateTimeOffset(
                BoundaryBeforeYear,
                12,
                31,
                22,
                59,
                0,
                TimeSpan.Zero);
            var atPragueMidnight = new DateTimeOffset(
                BoundaryBeforeYear,
                12,
                31,
                23,
                0,
                0,
                TimeSpan.Zero);

            var before = await AllocateAsync(beforePragueMidnight);
            var after = await AllocateAsync(atPragueMidnight);

            Assert.Equal(BoundaryBeforeYear, before.BusinessYear);
            Assert.Equal(BoundaryAfterYear, after.BusinessYear);
            Assert.Equal(1, before.Sequence);
            Assert.Equal(1, after.Sequence);
        }
        finally
        {
            await DeleteCountersAsync(BoundaryBeforeYear, BoundaryAfterYear);
        }
    }

    [Fact]
    public async Task Allocate_RolledBackBusinessTransactionDoesNotRecycleNumber()
    {
        await DeleteCountersAsync(RollbackYear);
        var sourceId = Guid.NewGuid();

        try
        {
            FinancialDocumentNumberAllocation consumed;

            using (var scope = _factory.Services.CreateScope())
            {
                var dbContext = scope.ServiceProvider
                    .GetRequiredService<FuaPayDbContext>();
                await using var transaction =
                    await dbContext.Database.BeginTransactionAsync();
                var issuedAt = UtcInstant(RollbackYear, 4, 1, 10, 0);
                var allocator = scope.ServiceProvider
                    .GetRequiredService<IFinancialDocumentNumberAllocator>();
                consumed = await allocator.AllocateAsync(issuedAt);
                var repository = scope.ServiceProvider
                    .GetRequiredService<IFinancialDocumentRepository>();
                repository.Stage(CreateManualDocument(
                    consumed.DocumentNumber,
                    sourceId,
                    issuedAt));
                await dbContext.SaveChangesAsync();
                await transaction.RollbackAsync();
            }

            var next = await AllocateAsync(
                UtcInstant(RollbackYear, 4, 1, 10, 1));

            Assert.Equal(1, consumed.Sequence);
            Assert.Equal(2, next.Sequence);
            Assert.NotEqual(consumed.DocumentNumber, next.DocumentNumber);
            Assert.Equal(0, await CountDocumentsBySourceAsync(sourceId));
        }
        finally
        {
            await DeleteDocumentsBySourceAsync(sourceId);
            await DeleteCountersAsync(RollbackYear);
        }
    }

    [Fact]
    public async Task Allocate_FirstAllocationOfNewYearIsRaceSafe()
    {
        await DeleteCountersAsync(FirstAllocationRaceYear);

        try
        {
            var issuedAt = UtcInstant(
                FirstAllocationRaceYear,
                1,
                1,
                12,
                0);
            var allocations = await Task.WhenAll(
                Enumerable.Range(0, 12)
                    .Select(_ => AllocateAsync(issuedAt)));

            Assert.Equal(
                Enumerable.Range(1, 12),
                allocations.Select(x => x.Sequence).OrderBy(x => x));
            Assert.Equal(1, await CountCountersAsync(FirstAllocationRaceYear));
        }
        finally
        {
            await DeleteCountersAsync(FirstAllocationRaceYear);
        }
    }

    [Fact]
    public async Task Repository_RoundTripsSnapshotAndEnforcesSourceUniqueness()
    {
        await DeleteCountersAsync(PersistenceYear);
        var sourceId = Guid.NewGuid();

        try
        {
            var issuedAt = UtcInstant(PersistenceYear, 8, 1, 9, 0);
            var firstNumber = await AllocateAsync(issuedAt);
            var first = CreateManualDocument(
                firstNumber.DocumentNumber,
                sourceId,
                issuedAt);

            using (var scope = _factory.Services.CreateScope())
            {
                var repository = scope.ServiceProvider
                    .GetRequiredService<IFinancialDocumentRepository>();
                repository.Stage(first);
                await repository.PersistStagedAsync(first);
            }

            using (var scope = _factory.Services.CreateScope())
            {
                var repository = scope.ServiceProvider
                    .GetRequiredService<IFinancialDocumentRepository>();
                var persisted = Assert.IsType<FinancialDocument>(
                    await repository.FindBySourceAsync(
                        FinancialDocumentSourceType.ManualCreditTopUp,
                        sourceId));

                AssertDocumentSnapshot(first, persisted);
            }

            var secondNumber = await AllocateAsync(issuedAt);

            using (var scope = _factory.Services.CreateScope())
            {
                var repository = scope.ServiceProvider
                    .GetRequiredService<IFinancialDocumentRepository>();
                var duplicate = CreateManualDocument(
                    secondNumber.DocumentNumber,
                    sourceId,
                    issuedAt);
                repository.Stage(duplicate);

                var exception = await Assert.ThrowsAsync<
                    FinancialDocumentSourceAlreadyExistsException>(
                        () => repository.PersistStagedAsync(duplicate));
                Assert.Equal(
                    FinancialDocumentSourceType.ManualCreditTopUp,
                    exception.SourceType);
                Assert.Equal(sourceId, exception.SourceId);
            }

            Assert.Equal(1, await CountDocumentsBySourceAsync(sourceId));
        }
        finally
        {
            await DeleteDocumentsBySourceAsync(sourceId);
            await DeleteCountersAsync(PersistenceYear);
        }
    }

    [Fact]
    public async Task Repository_PersistRejectsAmbiguousStagedDocumentsBeforeWrite()
    {
        await DeleteCountersAsync(AmbiguousPersistenceYear);
        var firstSourceId = Guid.NewGuid();
        var secondSourceId = Guid.NewGuid();

        try
        {
            var issuedAt = UtcInstant(
                AmbiguousPersistenceYear,
                8,
                1,
                9,
                0);
            var firstNumber = await AllocateAsync(issuedAt);
            var secondNumber = await AllocateAsync(issuedAt);
            var first = CreateManualDocument(
                firstNumber.DocumentNumber,
                firstSourceId,
                issuedAt);
            var second = CreateManualDocument(
                secondNumber.DocumentNumber,
                secondSourceId,
                issuedAt);

            using (var scope = _factory.Services.CreateScope())
            {
                var repository = scope.ServiceProvider
                    .GetRequiredService<IFinancialDocumentRepository>();
                repository.Stage(first);
                repository.Stage(second);

                await Assert.ThrowsAsync<InvalidOperationException>(
                    () => repository.PersistStagedAsync(first));
            }

            Assert.Equal(
                0,
                await CountDocumentsBySourceAsync(firstSourceId));
            Assert.Equal(
                0,
                await CountDocumentsBySourceAsync(secondSourceId));
        }
        finally
        {
            await DeleteDocumentsBySourceAsync(
                firstSourceId,
                secondSourceId);
            await DeleteCountersAsync(AmbiguousPersistenceYear);
        }
    }

    [Fact]
    public async Task Repository_RoundTripsPaymentProviderIssuerAndJobSnapshots()
    {
        await DeleteCountersAsync(PaymentPersistenceYear);
        var sourceId = Guid.NewGuid();

        try
        {
            var issuedAt = UtcInstant(
                PaymentPersistenceYear,
                7,
                1,
                12,
                0);
            var number = await AllocateAsync(issuedAt);
            var expected = CreateDirectJobPaymentDocument(
                number.DocumentNumber,
                sourceId,
                issuedAt);

            await PersistAsync(expected);

            var persisted = await FindBySourceAsync(
                FinancialDocumentSourceType.Payment,
                sourceId);

            AssertDocumentSnapshot(expected, persisted);
            Assert.Equal("Csob", persisted.Provider!.Provider);
            Assert.Equal("test-pay-id@TEST", persisted.Provider.Reference);
            Assert.Equal("209700001", persisted.Provider.OrderNumber);
            Assert.NotEqual(Guid.Empty, persisted.Job!.JobId);
            Assert.Equal("TEST-2097-000001", persisted.Job.JobNumber);
            Assert.Equal("TEST JOB TITLE", persisted.Job.Title);
            Assert.Equal("TEST JOB DESCRIPTION", persisted.Job.Description);
            Assert.Equal("TEST SERVICE UNIT", persisted.Job.ServiceUnitName);
        }
        finally
        {
            await DeleteDocumentsBySourceAsync(sourceId);
            await DeleteCountersAsync(PaymentPersistenceYear);
        }
    }

    [Fact]
    public async Task Repository_DifferentSourcesRejectDuplicateDocumentNumber()
    {
        await DeleteCountersAsync(DocumentNumberUniqueYear);
        var firstSourceId = Guid.NewGuid();
        var secondSourceId = Guid.NewGuid();

        try
        {
            var issuedAt = UtcInstant(
                DocumentNumberUniqueYear,
                5,
                1,
                10,
                0);
            var number = await AllocateAsync(issuedAt);
            await PersistAsync(CreateManualDocument(
                number.DocumentNumber,
                firstSourceId,
                issuedAt));

            using var scope = _factory.Services.CreateScope();
            var repository = scope.ServiceProvider
                .GetRequiredService<IFinancialDocumentRepository>();
            var dbContext = scope.ServiceProvider
                .GetRequiredService<FuaPayDbContext>();
            repository.Stage(CreateManualDocument(
                number.DocumentNumber,
                secondSourceId,
                issuedAt));

            await Assert.ThrowsAsync<DbUpdateException>(
                () => dbContext.SaveChangesAsync());

            Assert.Equal(
                1,
                await CountDocumentsByNumberAsync(number.DocumentNumber));
        }
        finally
        {
            await DeleteDocumentsBySourceAsync(
                firstSourceId,
                secondSourceId);
            await DeleteCountersAsync(DocumentNumberUniqueYear);
        }
    }

    [Fact]
    public async Task Allocate_ExhaustedAnnualSequenceFailsClosed()
    {
        await DeleteCountersAsync(ExhaustionYear);

        try
        {
            await SetCounterValueAsync(ExhaustionYear, 999_999);

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => AllocateAsync(
                    UtcInstant(ExhaustionYear, 6, 1, 12, 0)));

            Assert.Equal(
                999_999,
                await ReadCounterValueAsync(ExhaustionYear));
        }
        finally
        {
            await DeleteCountersAsync(ExhaustionYear);
        }
    }

    private async Task<FinancialDocumentNumberAllocation> AllocateAsync(
        DateTimeOffset issuedAt)
    {
        using var scope = _factory.Services.CreateScope();
        var allocator = scope.ServiceProvider
            .GetRequiredService<IFinancialDocumentNumberAllocator>();
        return await allocator.AllocateAsync(issuedAt);
    }

    private async Task PersistAsync(
        params FinancialDocument[] documents)
    {
        using var scope = _factory.Services.CreateScope();
        var repository = scope.ServiceProvider
            .GetRequiredService<IFinancialDocumentRepository>();

        foreach (var document in documents)
        {
            repository.Stage(document);
            await repository.PersistStagedAsync(document);
        }
    }

    private async Task<FinancialDocument> FindBySourceAsync(
        FinancialDocumentSourceType sourceType,
        Guid sourceId)
    {
        using var scope = _factory.Services.CreateScope();
        var repository = scope.ServiceProvider
            .GetRequiredService<IFinancialDocumentRepository>();
        return Assert.IsType<FinancialDocument>(
            await repository.FindBySourceAsync(sourceType, sourceId));
    }

    private static FinancialDocument CreateManualDocument(
        string documentNumber,
        Guid sourceId,
        DateTimeOffset issuedAt) =>
        new(
            Guid.NewGuid(),
            documentNumber,
            FinancialDocumentType.ManualCreditTopUp,
            FinancialDocumentSourceType.ManualCreditTopUp,
            sourceId,
            new FinancialDocumentCustomerSnapshot(
                Guid.NewGuid(),
                "Testovací zákazník",
                "customer@example.test"),
            2_500,
            "CZK",
            issuedAt.AddMinutes(-1),
            issuedAt,
            FinancialDocumentSettlementMethod.ManualCreditTopUp,
            CreateTestIssuer(),
            null,
            null,
            null,
            1,
            1);

    private static FinancialDocument CreateWalletDocument(
        string documentNumber,
        Guid sourceId,
        DateTimeOffset issuedAt) =>
        new(
            Guid.NewGuid(),
            documentNumber,
            FinancialDocumentType.CardWalletTopUp,
            FinancialDocumentSourceType.Payment,
            sourceId,
            new FinancialDocumentCustomerSnapshot(
                Guid.NewGuid(),
                "Testovací zákazník",
                "wallet-customer@example.test"),
            3_500,
            "CZK",
            issuedAt.AddMinutes(-1),
            issuedAt,
            FinancialDocumentSettlementMethod.PaymentProvider,
            null,
            null,
            new FinancialDocumentProviderSnapshot(
                "TestProvider",
                "test-wallet-reference",
                "209100001"),
            null,
            1,
            1);

    private static FinancialDocument CreateDirectJobPaymentDocument(
        string documentNumber,
        Guid sourceId,
        DateTimeOffset issuedAt) =>
        new(
            Guid.NewGuid(),
            documentNumber,
            FinancialDocumentType.DirectJobCardPayment,
            FinancialDocumentSourceType.Payment,
            sourceId,
            new FinancialDocumentCustomerSnapshot(
                Guid.NewGuid(),
                "Testovací payment zákazník",
                "payment-customer@example.test"),
            12_345,
            "CZK",
            issuedAt.AddMinutes(-2),
            issuedAt,
            FinancialDocumentSettlementMethod.PaymentProvider,
            CreateTestIssuer(),
            FinancialDocumentTaxPolicy.CreateApprovedSnapshot(12_345),
            new FinancialDocumentProviderSnapshot(
                "Csob",
                "test-pay-id@TEST",
                "209700001"),
            new FinancialDocumentJobSnapshot(
                Guid.NewGuid(),
                "TEST-2097-000001",
                "TEST JOB TITLE",
                "TEST JOB DESCRIPTION",
                "TEST SERVICE UNIT"),
            2,
            2);

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

    private static void AssertDocumentSnapshot(
        FinancialDocument expected,
        FinancialDocument actual)
    {
        Assert.Equal(expected.DocumentId, actual.DocumentId);
        Assert.Equal(expected.DocumentNumber, actual.DocumentNumber);
        Assert.Equal(expected.DocumentType, actual.DocumentType);
        Assert.Equal(expected.SourceType, actual.SourceType);
        Assert.Equal(expected.SourceId, actual.SourceId);
        Assert.Equal(
            expected.Customer.CustomerUserId,
            actual.Customer.CustomerUserId);
        Assert.Equal(
            expected.Customer.DisplayName,
            actual.Customer.DisplayName);
        Assert.Equal(expected.Customer.Email, actual.Customer.Email);
        Assert.Equal(expected.AmountMinorUnits, actual.AmountMinorUnits);
        Assert.Equal(expected.Currency, actual.Currency);
        Assert.Equal(expected.FinancialEventAt, actual.FinancialEventAt);
        Assert.Equal(expected.IssuedAt, actual.IssuedAt);
        Assert.Equal(expected.SettlementMethod, actual.SettlementMethod);
        Assert.Equal(expected.SchemaVersion, actual.SchemaVersion);
        Assert.Equal(expected.RenderVersion, actual.RenderVersion);
        AssertIssuerSnapshot(expected.Issuer, actual.Issuer);
        AssertTaxSnapshot(expected.Tax, actual.Tax);
        AssertProviderSnapshot(expected.Provider, actual.Provider);
        AssertJobSnapshot(expected.Job, actual.Job);
    }

    private static void AssertTaxSnapshot(
        FinancialDocumentTaxSnapshot? expected,
        FinancialDocumentTaxSnapshot? actual)
    {
        if (expected is null)
        {
            Assert.Null(actual);
            return;
        }

        var persisted = Assert.IsType<FinancialDocumentTaxSnapshot>(actual);
        Assert.Equal(expected.Treatment, persisted.Treatment);
        Assert.Equal(expected.VatRateBasisPoints, persisted.VatRateBasisPoints);
        Assert.Equal(expected.TaxBaseMinorUnits, persisted.TaxBaseMinorUnits);
        Assert.Equal(expected.VatAmountMinorUnits, persisted.VatAmountMinorUnits);
    }

    private static void AssertIssuerSnapshot(
        FinancialDocumentIssuerSnapshot? expected,
        FinancialDocumentIssuerSnapshot? actual)
    {
        if (expected is null)
        {
            Assert.Null(actual);
            return;
        }

        var persisted = Assert.IsType<FinancialDocumentIssuerSnapshot>(actual);
        Assert.Equal(expected.LegalName, persisted.LegalName);
        Assert.Equal(expected.UnitName, persisted.UnitName);
        Assert.Equal(expected.AddressLine1, persisted.AddressLine1);
        Assert.Equal(expected.AddressLine2, persisted.AddressLine2);
        Assert.Equal(expected.Country, persisted.Country);
        Assert.Equal(
            expected.RegistrationNumber,
            persisted.RegistrationNumber);
        Assert.Equal(expected.VatNumber, persisted.VatNumber);
        Assert.Equal(expected.ContactEmail, persisted.ContactEmail);
    }

    private static void AssertProviderSnapshot(
        FinancialDocumentProviderSnapshot? expected,
        FinancialDocumentProviderSnapshot? actual)
    {
        if (expected is null)
        {
            Assert.Null(actual);
            return;
        }

        var persisted = Assert.IsType<FinancialDocumentProviderSnapshot>(
            actual);
        Assert.Equal(expected.Provider, persisted.Provider);
        Assert.Equal(expected.Reference, persisted.Reference);
        Assert.Equal(expected.OrderNumber, persisted.OrderNumber);
    }

    private static void AssertJobSnapshot(
        FinancialDocumentJobSnapshot? expected,
        FinancialDocumentJobSnapshot? actual)
    {
        if (expected is null)
        {
            Assert.Null(actual);
            return;
        }

        var persisted = Assert.IsType<FinancialDocumentJobSnapshot>(actual);
        Assert.Equal(expected.JobId, persisted.JobId);
        Assert.Equal(expected.JobNumber, persisted.JobNumber);
        Assert.Equal(expected.Title, persisted.Title);
        Assert.Equal(expected.Description, persisted.Description);
        Assert.Equal(expected.ServiceUnitName, persisted.ServiceUnitName);
    }

    private async Task DeleteCountersAsync(params int[] years)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider
            .GetRequiredService<FuaPayDbContext>();

        foreach (var year in years)
        {
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM financial_documents.number_counters WHERE business_year = {year}");
        }
    }

    private async Task<int> CountCountersAsync(int year)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider
            .GetRequiredService<FuaPayDbContext>();
        return await dbContext.Database
            .SqlQuery<int>(
                $"""
                SELECT count(*)::int AS "Value"
                FROM financial_documents.number_counters
                WHERE business_year = {year}
                """)
            .SingleAsync();
    }

    private async Task<int> CountDocumentsBySourceAsync(Guid sourceId)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider
            .GetRequiredService<FuaPayDbContext>();
        return await dbContext.Database
            .SqlQuery<int>(
                $"""
                SELECT count(*)::int AS "Value"
                FROM financial_documents.documents
                WHERE source_id = {sourceId}
                """)
            .SingleAsync();
    }

    private async Task<int> CountDocumentsByNumberAsync(
        string documentNumber)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider
            .GetRequiredService<FuaPayDbContext>();
        return await dbContext.Database
            .SqlQuery<int>(
                $"""
                SELECT count(*)::int AS "Value"
                FROM financial_documents.documents
                WHERE document_number = {documentNumber}
                """)
            .SingleAsync();
    }

    private async Task SetCounterValueAsync(int year, int value)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider
            .GetRequiredService<FuaPayDbContext>();
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO financial_documents.number_counters
                (business_year, last_value)
            VALUES ({year}, {value})
            ON CONFLICT (business_year)
            DO UPDATE SET last_value = EXCLUDED.last_value
            """);
    }

    private async Task<int> ReadCounterValueAsync(int year)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider
            .GetRequiredService<FuaPayDbContext>();
        return await dbContext.Database
            .SqlQuery<int>(
                $"""
                SELECT last_value AS "Value"
                FROM financial_documents.number_counters
                WHERE business_year = {year}
                """)
            .SingleAsync();
    }

    private async Task DeleteDocumentsBySourceAsync(params Guid[] sourceIds)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider
            .GetRequiredService<FuaPayDbContext>();

        foreach (var sourceId in sourceIds)
        {
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM financial_documents.documents WHERE source_id = {sourceId}");
        }
    }

    private static DateTimeOffset UtcInstant(
        int year,
        int month,
        int day,
        int hour,
        int minute) =>
        new(year, month, day, hour, minute, 0, TimeSpan.Zero);
}
