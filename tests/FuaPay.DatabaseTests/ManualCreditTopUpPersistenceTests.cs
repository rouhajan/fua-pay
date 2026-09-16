using FuaPay.Web.BuildingBlocks.Domain;
using FuaPay.Web.BuildingBlocks.Persistence;
using FuaPay.Web.Modules.Credits.Application;
using FuaPay.Web.Modules.Credits.Domain;
using FuaPay.Web.Modules.FinancialDocuments.Application;
using FuaPay.Web.Modules.FinancialDocuments.Domain;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Npgsql;

namespace FuaPay.DatabaseTests;

public sealed class ManualCreditTopUpPersistenceTests :
    IClassFixture<WebApplicationFactory<Program>>
{
    private const int SuccessYear = 2081;
    private const int ConcurrentYear = 2082;
    private const int BeforeAllocationFailureYear = 2083;
    private const int AfterAllocationFailureYear = 2084;
    private const int LegacyReplayYear = 2085;
    private const int LegacyConflictYear = 2086;
    private const int PostCutoverCorruptionYear = 2087;
    private const int AmbientTransactionYear = 2088;

    private readonly WebApplicationFactory<Program> _factory;

    public ManualCreditTopUpPersistenceTests(
        WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task TopUp_SuccessReplayConflictAndSnapshotRoundTripThroughPostgreSql()
    {
        var command = CreateCommand("Accepted cash");
        var customer = CreateCustomer(
            command.OwnerId,
            "Original customer",
            "original@example.test");
        using var factory = CreateTimeFactory(
            SuccessYear,
            month: 3,
            day: 1);

        await DeleteCounterAsync(SuccessYear);

        try
        {
            var first = await TopUpAsync(factory, command, customer);
            var firstDocument = await FindDocumentAsync(
                factory,
                command.CommandId);

            var replay = await TopUpAsync(
                factory,
                command,
                CreateCustomer(
                    command.OwnerId,
                    "Changed mutable name",
                    "changed@example.test"));
            var replayDocument = await FindDocumentAsync(
                factory,
                command.CommandId);

            Assert.Equal(first, replay);
            Assert.Equal(CreditMovementType.Credit, first.MovementType);
            Assert.Equal(command.Amount, first.Amount);
            Assert.Equal(command.Amount, first.BalanceAfter);
            Assert.Equal(
                "Ru\u010dn\u00ed dobit\u00ed kreditu",
                first.Description);
            Assert.DoesNotContain(
                "Administrativn\u00ed korekce",
                first.Description);

            Assert.Equal(firstDocument.DocumentId, replayDocument.DocumentId);
            Assert.Equal(
                firstDocument.DocumentNumber,
                replayDocument.DocumentNumber);
            Assert.Equal(
                FinancialDocumentType.ManualCreditTopUp,
                firstDocument.DocumentType);
            Assert.Equal(
                FinancialDocumentSourceType.ManualCreditTopUp,
                firstDocument.SourceType);
            Assert.Equal(command.CommandId, firstDocument.SourceId);
            Assert.Equal(customer, firstDocument.Customer);
            Assert.Equal(
                command.Amount.MinorUnits,
                firstDocument.AmountMinorUnits);
            Assert.Equal(Money.CurrencyCode, firstDocument.Currency);
            Assert.Equal(first.RecordedAt, firstDocument.FinancialEventAt);
            Assert.True(firstDocument.IssuedAt >= first.RecordedAt);
            Assert.Equal(
                FinancialDocumentSettlementMethod.ManualCreditTopUp,
                firstDocument.SettlementMethod);
            Assert.Null(firstDocument.Issuer);
            Assert.Null(firstDocument.Provider);
            Assert.Null(firstDocument.Job);
            Assert.Equal(
                FinancialDocument.CurrentSchemaVersion,
                firstDocument.SchemaVersion);
            Assert.Equal(
                FinancialDocument.CurrentRenderVersion,
                firstDocument.RenderVersion);

            var persistedCommand = await FindCommandAsync(
                factory,
                command.CommandId);
            Assert.True(persistedCommand.FinancialDocumentRequired);

            await AssertPersistedEffectCountsAsync(factory, command, 1);
            Assert.Equal(1, await ReadCounterValueAsync(factory, SuccessYear));

            var conflicting = new ManualCreditTopUpCommand(
                command.CommandId,
                command.AdministratorUserId,
                command.OwnerId,
                new Money(command.Amount.MinorUnits + 1),
                command.Note);

            await Assert.ThrowsAsync<ManualCreditTopUpCommandConflictException>(
                () => TopUpAsync(factory, conflicting, customer));
            await AssertPersistedEffectCountsAsync(factory, command, 1);
            Assert.Equal(1, await ReadCounterValueAsync(factory, SuccessYear));
        }
        finally
        {
            await DeleteAsync(command);
            await DeleteCounterAsync(SuccessYear);
        }
    }

    [Fact]
    public async Task CutoverMarker_IsNotNullableHasNoDefaultAndCannotBeOmitted()
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider
            .GetRequiredService<FuaPayDbContext>();
        await using var transaction =
            await dbContext.Database.BeginTransactionAsync();

        try
        {
            var isNullable = await dbContext.Database
                .SqlQueryRaw<string>(
                    """
                    SELECT is_nullable AS "Value"
                    FROM information_schema.columns
                    WHERE table_schema = 'credits'
                      AND table_name = 'manual_topup_commands'
                      AND column_name = 'financial_document_required'
                    """)
                .SingleAsync();
            var hasNoDefault = await dbContext.Database
                .SqlQueryRaw<bool>(
                    """
                    SELECT column_default IS NULL AS "Value"
                    FROM information_schema.columns
                    WHERE table_schema = 'credits'
                      AND table_name = 'manual_topup_commands'
                      AND column_name = 'financial_document_required'
                    """)
                .SingleAsync();

            Assert.Equal("NO", isNullable);
            Assert.True(hasNoDefault);

            var exception = await Assert.ThrowsAsync<PostgresException>(
                () => dbContext.Database.ExecuteSqlInterpolatedAsync(
                    $"""
                    INSERT INTO credits.manual_topup_commands
                    (
                        command_id, administrator_user_id, owner_id,
                        amount_minor_units, note, accepted_at
                    )
                    VALUES
                    (
                        {Guid.NewGuid()}, {Guid.NewGuid()}, {Guid.NewGuid()},
                        {1L}, {"Omitted cutover marker"},
                        {DateTimeOffset.UtcNow}
                    )
                    """));

            Assert.Equal(
                PostgresErrorCodes.NotNullViolation,
                exception.SqlState);
            Assert.Equal(
                "financial_document_required",
                exception.ColumnName);
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    [Fact]
    public async Task TopUp_ConcurrentDuplicateCreatesOneEffectAndOneAllocation()
    {
        var command = CreateCommand("Concurrent manual top-up");
        var customer = CreateCustomer(
            command.OwnerId,
            "Concurrent customer",
            null);
        using var factory = CreateTimeFactory(
            ConcurrentYear,
            month: 4,
            day: 1);

        await DeleteCounterAsync(ConcurrentYear);

        try
        {
            var results = await Task.WhenAll(
                TopUpAsync(factory, command, customer),
                TopUpAsync(factory, command, customer));

            Assert.Equal(results[0], results[1]);
            await AssertPersistedEffectCountsAsync(factory, command, 1);
            Assert.Equal(
                1,
                await ReadCounterValueAsync(factory, ConcurrentYear));

            var document = await FindDocumentAsync(
                factory,
                command.CommandId);
            Assert.Equal(
                $"FUA-{ConcurrentYear:D4}-000001",
                document.DocumentNumber);
        }
        finally
        {
            await DeleteAsync(command);
            await DeleteCounterAsync(ConcurrentYear);
        }
    }

    [Fact]
    public async Task TopUp_FailureBeforeNumberAllocationRollsBackEverythingAndDoesNotConsumeNumber()
    {
        var command = CreateCommand("Failure before allocation");
        var customer = CreateCustomer(
            command.OwnerId,
            "Pre-allocation failure customer",
            null);
        using var timeFactory = CreateTimeFactory(
            BeforeAllocationFailureYear,
            month: 5,
            day: 1);
        using var failureFactory =
            CreateCreditRepositoryFailureFactory(
                timeFactory,
                command.OwnerId);

        await DeleteCounterAsync(BeforeAllocationFailureYear);

        try
        {
            var exception = await Assert.ThrowsAsync<TimeoutException>(
                () => TopUpAsync(failureFactory, command, customer));

            Assert.Equal(
                ThrowAfterWriteCreditAccountRepository.FailureMessage,
                exception.Message);
            await AssertPersistedEffectCountsAsync(_factory, command, 0);
            Assert.Equal(
                0,
                await CountCountersAsync(
                    _factory,
                    BeforeAllocationFailureYear));
            Assert.Equal(
                0,
                await CountAccountsAsync(_factory, command.OwnerId));
        }
        finally
        {
            await DeleteAsync(command);
            await DeleteCounterAsync(BeforeAllocationFailureYear);
        }
    }

    [Fact]
    public async Task TopUp_FailureAfterAllocationConsumesNumberAndRollsBackBusinessState()
    {
        var failedCommand = CreateCommand("Failure after allocation");
        var successfulCommand = CreateCommand("Success after allocation gap");
        var failedCustomer = CreateCustomer(
            failedCommand.OwnerId,
            "Failed customer",
            "failed@example.test");
        var successfulCustomer = CreateCustomer(
            successfulCommand.OwnerId,
            "Successful customer",
            "successful@example.test");
        using var timeFactory = CreateTimeFactory(
            AfterAllocationFailureYear,
            month: 6,
            day: 1);
        using var failureFactory =
            CreateFinancialDocumentPersistenceFailureFactory(timeFactory);

        await DeleteCounterAsync(AfterAllocationFailureYear);

        try
        {
            var exception = await Assert.ThrowsAsync<TimeoutException>(
                () => TopUpAsync(
                    failureFactory,
                    failedCommand,
                    failedCustomer));

            Assert.Equal(
                ThrowAfterPersistFinancialDocumentRepository.FailureMessage,
                exception.Message);
            await AssertPersistedEffectCountsAsync(
                _factory,
                failedCommand,
                0);
            Assert.Equal(
                1,
                await ReadCounterValueAsync(
                    _factory,
                    AfterAllocationFailureYear));

            await TopUpAsync(
                timeFactory,
                successfulCommand,
                successfulCustomer);

            var document = await FindDocumentAsync(
                timeFactory,
                successfulCommand.CommandId);
            Assert.Equal(
                $"FUA-{AfterAllocationFailureYear:D4}-000002",
                document.DocumentNumber);
            await AssertPersistedEffectCountsAsync(
                _factory,
                successfulCommand,
                1);
            Assert.Equal(
                2,
                await ReadCounterValueAsync(
                    _factory,
                    AfterAllocationFailureYear));
        }
        finally
        {
            await DeleteAsync(failedCommand, successfulCommand);
            await DeleteCounterAsync(AfterAllocationFailureYear);
        }
    }

    [Fact]
    public async Task TopUp_LegacyReplayReturnsOriginalResultWithoutBackfillOrAllocation()
    {
        var command = CreateCommand("Legacy replay");
        var originalCustomer = CreateCustomer(
            command.OwnerId,
            "Historical customer unavailable",
            null);
        using var factory = CreateTimeFactory(
            LegacyReplayYear,
            month: 7,
            day: 1);

        await DeleteCounterAsync(LegacyReplayYear);

        try
        {
            var original = await PrepareDocumentlessReplayAsync(
                factory,
                command,
                originalCustomer,
                financialDocumentRequired: false,
                businessYear: LegacyReplayYear);
            var beforeReplay = await FindCommandAsync(
                factory,
                command.CommandId);

            var replay = await TopUpAsync(
                factory,
                command,
                CreateCustomer(
                    command.OwnerId,
                    "Current mutable customer",
                    "current@example.test"));
            var afterReplay = await FindCommandAsync(
                factory,
                command.CommandId);

            Assert.Equal(original, replay);
            Assert.Equal(beforeReplay, afterReplay);
            Assert.False(afterReplay.FinancialDocumentRequired);
            await AssertPersistedEffectCountsAsync(
                factory,
                command,
                expected: 1,
                expectedDocuments: 0);
            Assert.Equal(
                0,
                await CountCountersAsync(factory, LegacyReplayYear));
        }
        finally
        {
            await DeleteAsync(command);
            await DeleteCounterAsync(LegacyReplayYear);
        }
    }

    [Fact]
    public async Task TopUp_LegacyReplayWithDifferentPayloadStillConflicts()
    {
        var command = CreateCommand("Legacy conflict");
        var customer = CreateCustomer(
            command.OwnerId,
            "Legacy conflict customer",
            null);
        using var factory = CreateTimeFactory(
            LegacyConflictYear,
            month: 8,
            day: 1);

        await DeleteCounterAsync(LegacyConflictYear);

        try
        {
            await PrepareDocumentlessReplayAsync(
                factory,
                command,
                customer,
                financialDocumentRequired: false,
                businessYear: LegacyConflictYear);
            var conflicting = new ManualCreditTopUpCommand(
                command.CommandId,
                command.AdministratorUserId,
                command.OwnerId,
                new Money(command.Amount.MinorUnits + 1),
                command.Note);

            await Assert.ThrowsAsync<ManualCreditTopUpCommandConflictException>(
                () => TopUpAsync(factory, conflicting, customer));

            await AssertPersistedEffectCountsAsync(
                factory,
                command,
                expected: 1,
                expectedDocuments: 0);
            Assert.Equal(
                0,
                await CountCountersAsync(factory, LegacyConflictYear));
        }
        finally
        {
            await DeleteAsync(command);
            await DeleteCounterAsync(LegacyConflictYear);
        }
    }

    [Fact]
    public async Task TopUp_PostCutoverCommandWithoutDocumentFailsClosed()
    {
        var command = CreateCommand("Post-cutover corruption");
        var customer = CreateCustomer(
            command.OwnerId,
            "Post-cutover customer",
            "customer@example.test");
        using var factory = CreateTimeFactory(
            PostCutoverCorruptionYear,
            month: 9,
            day: 1);

        await DeleteCounterAsync(PostCutoverCorruptionYear);

        try
        {
            await PrepareDocumentlessReplayAsync(
                factory,
                command,
                customer,
                financialDocumentRequired: true,
                businessYear: PostCutoverCorruptionYear);

            await Assert.ThrowsAsync<InvalidDataException>(
                () => TopUpAsync(
                    factory,
                    command,
                    CreateCustomer(
                        command.OwnerId,
                        "Changed current customer",
                        "changed@example.test")));

            var persisted = await FindCommandAsync(
                factory,
                command.CommandId);
            Assert.True(persisted.FinancialDocumentRequired);
            await AssertPersistedEffectCountsAsync(
                factory,
                command,
                expected: 1,
                expectedDocuments: 0);
            Assert.Equal(
                0,
                await CountCountersAsync(
                    factory,
                    PostCutoverCorruptionYear));
        }
        finally
        {
            await DeleteAsync(command);
            await DeleteCounterAsync(PostCutoverCorruptionYear);
        }
    }

    [Fact]
    public async Task TopUp_InsideAmbientTransactionFailsBeforeCallbackAndLeavesSessionUsable()
    {
        var command = CreateCommand("Ambient transaction rejection");
        var customer = CreateCustomer(
            command.OwnerId,
            "Ambient transaction customer",
            null);
        using var factory = CreateTimeFactory(
            AmbientTransactionYear,
            month: 10,
            day: 1);

        await DeleteCounterAsync(AmbientTransactionYear);

        try
        {
            using (var scope = factory.Services.CreateScope())
            {
                var dbContext = scope.ServiceProvider
                    .GetRequiredService<FuaPayDbContext>();
                var service = scope.ServiceProvider
                    .GetRequiredService<ManualCreditTopUpService>();
                await using var transaction =
                    await dbContext.Database.BeginTransactionAsync();

                await Assert.ThrowsAsync<InvalidOperationException>(
                    () => service.TopUpAsync(command, customer));

                var harmlessResult = await dbContext.Database
                    .SqlQueryRaw<int>("SELECT 1 AS \"Value\"")
                    .SingleAsync();
                Assert.Equal(1, harmlessResult);

                await transaction.RollbackAsync();
            }

            await AssertPersistedEffectCountsAsync(factory, command, 0);
            Assert.Equal(
                0,
                await CountAccountsAsync(factory, command.OwnerId));
            Assert.Equal(
                0,
                await CountCountersAsync(factory, AmbientTransactionYear));
        }
        finally
        {
            await DeleteAsync(command);
            await DeleteCounterAsync(AmbientTransactionYear);
        }
    }

    private WebApplicationFactory<Program> CreateTimeFactory(
        int year,
        int month,
        int day)
    {
        var currentTime = new DateTimeOffset(
            year,
            month,
            day,
            10,
            0,
            0,
            TimeSpan.Zero);

        return _factory.WithWebHostBuilder(
            builder =>
                builder.ConfigureServices(
                    services =>
                    {
                        services.RemoveAll<TimeProvider>();
                        services.AddSingleton<TimeProvider>(
                            new FixedTimeProvider(currentTime));
                    }));
    }

    private static WebApplicationFactory<Program>
        CreateCreditRepositoryFailureFactory(
            WebApplicationFactory<Program> factory,
            Guid targetOwnerId)
    {
        return factory.WithWebHostBuilder(
            builder =>
                builder.ConfigureServices(
                    services =>
                    {
                        var originalDescriptor = services.Last(
                            descriptor =>
                                descriptor.ServiceType ==
                                typeof(ICreditAccountRepository));
                        var implementationType =
                            originalDescriptor.ImplementationType ??
                            throw new InvalidOperationException(
                                "ICreditAccountRepository must be registered by implementation type.");

                        services.RemoveAll<ICreditAccountRepository>();
                        services.AddScoped<ICreditAccountRepository>(
                            serviceProvider =>
                                new ThrowAfterWriteCreditAccountRepository(
                                    (ICreditAccountRepository)
                                        ActivatorUtilities.CreateInstance(
                                            serviceProvider,
                                            implementationType),
                                    targetOwnerId));
                    }));
    }

    private static WebApplicationFactory<Program>
        CreateFinancialDocumentPersistenceFailureFactory(
            WebApplicationFactory<Program> factory)
    {
        return factory.WithWebHostBuilder(
            builder =>
                builder.ConfigureServices(
                    services =>
                    {
                        var originalDescriptor = services.Last(
                            descriptor =>
                                descriptor.ServiceType ==
                                typeof(IFinancialDocumentRepository));
                        var implementationType =
                            originalDescriptor.ImplementationType ??
                            throw new InvalidOperationException(
                                "IFinancialDocumentRepository must be registered by implementation type.");

                        services.RemoveAll<IFinancialDocumentRepository>();
                        services.AddScoped<IFinancialDocumentRepository>(
                            serviceProvider =>
                                new ThrowAfterPersistFinancialDocumentRepository(
                                    (IFinancialDocumentRepository)
                                        ActivatorUtilities.CreateInstance(
                                            serviceProvider,
                                            implementationType)));
                    }));
    }

    private static ManualCreditTopUpCommand CreateCommand(string note) =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            new Money(2_500),
            note);

    private static FinancialDocumentCustomerSnapshot CreateCustomer(
        Guid ownerId,
        string displayName,
        string? email) =>
        new(ownerId, displayName, email);

    private static async Task<ManualCreditTopUpResult> TopUpAsync(
        WebApplicationFactory<Program> factory,
        ManualCreditTopUpCommand command,
        FinancialDocumentCustomerSnapshot customer)
    {
        using var scope = factory.Services.CreateScope();

        return await scope.ServiceProvider
            .GetRequiredService<ManualCreditTopUpService>()
            .TopUpAsync(command, customer);
    }

    private static async Task<FinancialDocument> FindDocumentAsync(
        WebApplicationFactory<Program> factory,
        Guid commandId)
    {
        using var scope = factory.Services.CreateScope();
        var repository = scope.ServiceProvider
            .GetRequiredService<IFinancialDocumentRepository>();

        return Assert.IsType<FinancialDocument>(
            await repository.FindBySourceAsync(
                FinancialDocumentSourceType.ManualCreditTopUp,
                commandId));
    }

    private static async Task<PersistedManualCreditTopUpCommand>
        FindCommandAsync(
            WebApplicationFactory<Program> factory,
            Guid commandId)
    {
        using var scope = factory.Services.CreateScope();
        var repository = scope.ServiceProvider
            .GetRequiredService<IManualCreditTopUpCommandRepository>();

        return Assert.IsType<PersistedManualCreditTopUpCommand>(
            await repository.FindAsync(commandId));
    }

    private static async Task<ManualCreditTopUpResult>
        PrepareDocumentlessReplayAsync(
            WebApplicationFactory<Program> factory,
            ManualCreditTopUpCommand command,
            FinancialDocumentCustomerSnapshot customer,
            bool financialDocumentRequired,
            int businessYear)
    {
        var result = await TopUpAsync(factory, command, customer);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider
            .GetRequiredService<FuaPayDbContext>();
        await using var transaction =
            await dbContext.Database.BeginTransactionAsync();

        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            DELETE FROM financial_documents.documents
            WHERE source_type = 1
              AND source_id = {command.CommandId}
            """);
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE credits.manual_topup_commands
            SET financial_document_required = {financialDocumentRequired}
            WHERE command_id = {command.CommandId}
            """);
        await transaction.CommitAsync();

        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM financial_documents.number_counters WHERE business_year = {businessYear}");

        return result;
    }

    private static async Task AssertPersistedEffectCountsAsync(
        WebApplicationFactory<Program> factory,
        ManualCreditTopUpCommand command,
        int expected,
        int? expectedDocuments = null)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider
            .GetRequiredService<FuaPayDbContext>();

        Assert.Equal(
            expected,
            await CountCommandsAsync(dbContext, command.CommandId));
        Assert.Equal(
            expected,
            await CountMovementsAsync(dbContext, command.CommandId));
        Assert.Equal(
            expected,
            await CountAuditsAsync(dbContext, command.OwnerId));
        Assert.Equal(
            expectedDocuments ?? expected,
            await CountDocumentsAsync(dbContext, command.CommandId));
    }

    private static Task<int> CountCommandsAsync(
        FuaPayDbContext dbContext,
        Guid commandId) =>
        dbContext.Database
            .SqlQuery<int>(
                $"""
                SELECT count(*)::int AS "Value"
                FROM credits.manual_topup_commands
                WHERE command_id = {commandId}
                """)
            .SingleAsync();

    private static Task<int> CountMovementsAsync(
        FuaPayDbContext dbContext,
        Guid commandId) =>
        dbContext.Database
            .SqlQuery<int>(
                $"""
                SELECT count(*)::int AS "Value"
                FROM credits.movements
                WHERE operation_id = {commandId}
                """)
            .SingleAsync();

    private static Task<int> CountAuditsAsync(
        FuaPayDbContext dbContext,
        Guid ownerId) =>
        dbContext.Database
            .SqlQuery<int>(
                $"""
                SELECT count(*)::int AS "Value"
                FROM audit.events
                WHERE action = 'credit.manual-topup'
                  AND entity_id = {ownerId.ToString()}
                """)
            .SingleAsync();

    private static Task<int> CountDocumentsAsync(
        FuaPayDbContext dbContext,
        Guid commandId) =>
        dbContext.Database
            .SqlQuery<int>(
                $"""
                SELECT count(*)::int AS "Value"
                FROM financial_documents.documents
                WHERE source_type = 1
                  AND source_id = {commandId}
                """)
            .SingleAsync();

    private static async Task<int> CountAccountsAsync(
        WebApplicationFactory<Program> factory,
        Guid ownerId)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider
            .GetRequiredService<FuaPayDbContext>();
        return await dbContext.Database
            .SqlQuery<int>(
                $"""
                SELECT count(*)::int AS "Value"
                FROM credits.accounts
                WHERE owner_id = {ownerId}
                """)
            .SingleAsync();
    }

    private static async Task<int> CountCountersAsync(
        WebApplicationFactory<Program> factory,
        int year)
    {
        using var scope = factory.Services.CreateScope();
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

    private static async Task<int> ReadCounterValueAsync(
        WebApplicationFactory<Program> factory,
        int year)
    {
        using var scope = factory.Services.CreateScope();
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

    private async Task DeleteCounterAsync(int year)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider
            .GetRequiredService<FuaPayDbContext>();
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM financial_documents.number_counters WHERE business_year = {year}");
    }

    private async Task DeleteAsync(
        params ManualCreditTopUpCommand[] commands)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider
            .GetRequiredService<FuaPayDbContext>();
        await using var transaction =
            await dbContext.Database.BeginTransactionAsync();

        foreach (var command in commands)
        {
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"""
                DELETE FROM financial_documents.documents
                WHERE source_type = 1
                  AND source_id = {command.CommandId}
                """);
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"""
                DELETE FROM audit.events
                WHERE action = 'credit.manual-topup'
                  AND entity_id = {command.OwnerId.ToString()}
                """);
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"""
                DELETE FROM credits.manual_topup_commands
                WHERE command_id = {command.CommandId}
                """);
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"""
                DELETE FROM credits.movements
                WHERE operation_id = {command.CommandId}
                """);
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"""
                DELETE FROM credits.accounts
                WHERE owner_id = {command.OwnerId}
                """);
        }

        await transaction.CommitAsync();
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _time;

        public FixedTimeProvider(DateTimeOffset time)
        {
            _time = time;
        }

        public override DateTimeOffset GetUtcNow() => _time;
    }

    private sealed class ThrowAfterWriteCreditAccountRepository :
        ICreditAccountRepository
    {
        public const string FailureMessage =
            "Injected timeout after manual top-up credit write.";

        private readonly ICreditAccountRepository _inner;
        private readonly Guid _targetOwnerId;

        public ThrowAfterWriteCreditAccountRepository(
            ICreditAccountRepository inner,
            Guid targetOwnerId)
        {
            _inner = inner;
            _targetOwnerId = targetOwnerId;
        }

        public Task<CreditAccount?> FindByOwnerIdAsync(
            Guid ownerId,
            CancellationToken cancellationToken) =>
            _inner.FindByOwnerIdAsync(ownerId, cancellationToken);

        public Task<CreditAccount?> FindByOwnerIdForUpdateAsync(
            Guid ownerId,
            CancellationToken cancellationToken) =>
            _inner.FindByOwnerIdForUpdateAsync(ownerId, cancellationToken);

        public Task<CreditAccount?> FindByIdForUpdateAsync(
            Guid accountId,
            CancellationToken cancellationToken) =>
            _inner.FindByIdForUpdateAsync(accountId, cancellationToken);

        public Task LockOwnerForAccountCreationAsync(
            Guid ownerId,
            CancellationToken cancellationToken) =>
            _inner.LockOwnerForAccountCreationAsync(
                ownerId,
                cancellationToken);

        public async Task AddAsync(
            CreditAccount account,
            CancellationToken cancellationToken)
        {
            await _inner.AddAsync(account, cancellationToken);
            ThrowForTarget(account.OwnerId);
        }

        public async Task SaveAsync(
            CreditAccount account,
            CancellationToken cancellationToken)
        {
            await _inner.SaveAsync(account, cancellationToken);
            ThrowForTarget(account.OwnerId);
        }

        private void ThrowForTarget(Guid ownerId)
        {
            if (ownerId == _targetOwnerId)
            {
                throw new TimeoutException(FailureMessage);
            }
        }
    }

    private sealed class ThrowAfterPersistFinancialDocumentRepository :
        IFinancialDocumentRepository
    {
        public const string FailureMessage =
            "Injected timeout after financial document persistence.";

        private readonly IFinancialDocumentRepository _inner;

        public ThrowAfterPersistFinancialDocumentRepository(
            IFinancialDocumentRepository inner)
        {
            _inner = inner;
        }

        public Task<FinancialDocument?> FindBySourceAsync(
            FinancialDocumentSourceType sourceType,
            Guid sourceId,
            CancellationToken cancellationToken = default) =>
            _inner.FindBySourceAsync(
                sourceType,
                sourceId,
                cancellationToken);

        public void Stage(FinancialDocument document) =>
            _inner.Stage(document);

        public async Task PersistStagedAsync(
            FinancialDocument document,
            CancellationToken cancellationToken = default)
        {
            await _inner.PersistStagedAsync(document, cancellationToken);
            throw new TimeoutException(FailureMessage);
        }
    }
}
