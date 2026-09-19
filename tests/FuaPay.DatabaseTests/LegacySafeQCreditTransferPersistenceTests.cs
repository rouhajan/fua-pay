using FuaPay.Web.BuildingBlocks.Domain;
using FuaPay.Web.BuildingBlocks.Persistence;
using FuaPay.Web.Modules.Access.Application;
using FuaPay.Web.Modules.Access.Domain;
using FuaPay.Web.Modules.Credits.Application;
using FuaPay.Web.Modules.Credits.Domain;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Npgsql;

namespace FuaPay.DatabaseTests;

public sealed class LegacySafeQCreditTransferPersistenceTests :
    IClassFixture<WebApplicationFactory<Program>>
{
    private const string SnapshotHash =
        "5305EEFCFE4B2D6DAF86DF11897626B5F2819FE422F33463D9AA53DD5072F6FA";

    private const string DifferentSnapshotHash =
        "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB";

    private static readonly DateTimeOffset CurrentTime =
        new(2026, 9, 19, 12, 0, 0, TimeSpan.Zero);

    private readonly WebApplicationFactory<Program> _factory;

    public LegacySafeQCreditTransferPersistenceTests(
        WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Transfer_SuccessReplayAndHistoryRoundTripThroughPostgreSql()
    {
        var ownerId = Guid.NewGuid();
        var administratorUserId = Guid.NewGuid();
        var command = new LegacySafeQCreditTransferCommand(
            Guid.NewGuid(),
            administratorUserId,
            ownerId,
            "1000000000100123",
            SnapshotHash,
            new Money(2_500));

        var identityKey = new ExternalIdentityKey(
            "test",
            Guid.NewGuid().ToString("D"),
            Guid.NewGuid().ToString("D"));

        using var factory = CreateTimeFactory();

        try
        {
            await SeedCustomerAsync(
                factory,
                ownerId,
                identityKey);

            var first = await TransferAsync(
                factory,
                command);

            var replay = await TransferAsync(
                factory,
                command);

            Assert.Equal(first, replay);
            Assert.Equal(command.CommandId, first.CommandId);
            Assert.Equal(CreditMovementType.Credit, first.MovementType);
            Assert.Equal(command.Amount, first.Amount);
            Assert.Equal(command.Amount, first.BalanceAfter);
            Assert.Equal(CurrentTime, first.RecordedAt);
            Assert.Equal(
                "Převod kreditu ze SafeQ",
                first.Description);

            using (var scope = factory.Services.CreateScope())
            {
                var repository = scope.ServiceProvider
                    .GetRequiredService<
                        ILegacySafeQCreditTransferRepository>();

                var persisted = Assert.IsType<
                    PersistedLegacySafeQCreditTransfer>(
                        await repository.FindByCommandIdAsync(
                            command.CommandId));

                Assert.Equal(
                    command.CommandId,
                    persisted.Command.CommandId);
                Assert.Equal(
                    administratorUserId,
                    persisted.Command.AdministratorUserId);
                Assert.Equal(
                    ownerId,
                    persisted.Command.OwnerId);
                Assert.Equal(
                    command.SafeQUserId,
                    persisted.Command.SafeQUserId);
                Assert.Equal(
                    command.SnapshotSha256,
                    persisted.Command.SnapshotSha256);
                Assert.Equal(
                    command.Amount,
                    persisted.Command.Amount);
                Assert.Equal(
                    CurrentTime,
                    persisted.AcceptedAt);

                var bySource = Assert.IsType<
                    PersistedLegacySafeQCreditTransfer>(
                        await repository.FindBySafeQUserIdAsync(
                            command.SafeQUserId));

                Assert.Equal(
                    command.CommandId,
                    bySource.Command.CommandId);
            }

            var secondCommand =
                new LegacySafeQCreditTransferCommand(
                    Guid.NewGuid(),
                    administratorUserId,
                    ownerId,
                    command.SafeQUserId,
                    DifferentSnapshotHash,
                    command.Amount);

            await Assert.ThrowsAsync<
                LegacySafeQCreditAlreadyTransferredException>(
                    () => TransferAsync(
                        factory,
                        secondCommand));

            using var verificationScope =
                factory.Services.CreateScope();

            var dbContext = verificationScope.ServiceProvider
                .GetRequiredService<FuaPayDbContext>();

            var transferCount =
                await dbContext.Database.SqlQuery<int>(
                    $"""
                    SELECT COUNT(*)::int AS "Value"
                    FROM credits.legacy_safeq_credit_transfers
                    WHERE command_id = {command.CommandId}
                    """)
                    .SingleAsync();

            var movementCount =
                await dbContext.Database.SqlQuery<int>(
                    $"""
                    SELECT COUNT(*)::int AS "Value"
                    FROM credits.movements
                    WHERE operation_id = {command.CommandId}
                    """)
                    .SingleAsync();

            var auditCount =
                await dbContext.Database.SqlQuery<int>(
                    $"""
                    SELECT COUNT(*)::int AS "Value"
                    FROM audit.events
                    WHERE action = 'credit.legacy-safeq-transfer'
                      AND entity_id = {ownerId.ToString()}
                    """)
                    .SingleAsync();

            var documentCount =
                await dbContext.Database.SqlQuery<int>(
                    $"""
                    SELECT COUNT(*)::int AS "Value"
                    FROM financial_documents.documents
                    WHERE source_id = {command.CommandId}
                    """)
                    .SingleAsync();

            var auditActor =
                await dbContext.Database.SqlQuery<Guid>(
                    $"""
                    SELECT actor_user_id AS "Value"
                    FROM audit.events
                    WHERE action = 'credit.legacy-safeq-transfer'
                      AND entity_id = {ownerId.ToString()}
                    """)
                    .SingleAsync();

            var auditDescription =
                await dbContext.Database.SqlQuery<string>(
                    $"""
                    SELECT description AS "Value"
                    FROM audit.events
                    WHERE action = 'credit.legacy-safeq-transfer'
                      AND entity_id = {ownerId.ToString()}
                    """)
                    .SingleAsync();

            Assert.Equal(1, transferCount);
            Assert.Equal(1, movementCount);
            Assert.Equal(1, auditCount);
            Assert.Equal(0, documentCount);
            Assert.Equal(
                administratorUserId,
                auditActor);
            Assert.Contains(
                command.SafeQUserId,
                auditDescription);
            Assert.Contains(
                command.SnapshotSha256,
                auditDescription);
            Assert.Contains(
                command.Amount.MinorUnits.ToString(),
                auditDescription);
        }
        finally
        {
            await DeleteAsync(
                factory,
                ownerId,
                command.CommandId);
        }
    }

    [Fact]
    public async Task SafeQUserId_UniqueConstraintRejectsSecondSnapshotInPostgreSql()
    {
        var firstCommandId = Guid.NewGuid();
        var secondCommandId = Guid.NewGuid();
        var administratorUserId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var safeQUserId = "1000000000100456";

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider
            .GetRequiredService<FuaPayDbContext>();

        await using var transaction =
            await dbContext.Database.BeginTransactionAsync();

        try
        {
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO credits.legacy_safeq_credit_transfers
                (
                    command_id,
                    administrator_user_id,
                    owner_id,
                    safeq_user_id,
                    snapshot_sha256,
                    amount_minor_units,
                    accepted_at
                )
                VALUES
                (
                    {firstCommandId},
                    {administratorUserId},
                    {ownerId},
                    {safeQUserId},
                    {SnapshotHash},
                    {2_500L},
                    {CurrentTime}
                )
                """);

            var exception =
                await Assert.ThrowsAsync<PostgresException>(
                    () =>
                        dbContext.Database.ExecuteSqlInterpolatedAsync(
                            $"""
                            INSERT INTO credits.legacy_safeq_credit_transfers
                            (
                                command_id,
                                administrator_user_id,
                                owner_id,
                                safeq_user_id,
                                snapshot_sha256,
                                amount_minor_units,
                                accepted_at
                            )
                            VALUES
                            (
                                {secondCommandId},
                                {administratorUserId},
                                {ownerId},
                                {safeQUserId},
                                {DifferentSnapshotHash},
                                {2_500L},
                                {CurrentTime}
                            )
                            """));

            Assert.Equal(
                PostgresErrorCodes.UniqueViolation,
                exception.SqlState);

            Assert.Equal(
                "ux_credits_legacy_safeq_credit_transfers_safeq_user_id",
                exception.ConstraintName);
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    [Fact]
    public async Task Transfer_FailureAfterCreditWriteRollsBackAllFinancialEffects()
    {
        var ownerId = Guid.NewGuid();
        var command = new LegacySafeQCreditTransferCommand(
            Guid.NewGuid(),
            Guid.NewGuid(),
            ownerId,
            "1000000000100789",
            SnapshotHash,
            new Money(3_500));

        var identityKey = new ExternalIdentityKey(
            "test",
            Guid.NewGuid().ToString("D"),
            Guid.NewGuid().ToString("D"));

        using var timeFactory = CreateTimeFactory();
        using var failureFactory =
            CreateCreditRepositoryFailureFactory(
                timeFactory,
                ownerId);

        try
        {
            await SeedCustomerAsync(
                failureFactory,
                ownerId,
                identityKey);

            var exception = await Assert.ThrowsAsync<TimeoutException>(
                () => TransferAsync(
                    failureFactory,
                    command));

            Assert.Equal(
                ThrowAfterWriteCreditAccountRepository.FailureMessage,
                exception.Message);

            using var scope = _factory.Services.CreateScope();
            var dbContext = scope.ServiceProvider
                .GetRequiredService<FuaPayDbContext>();

            var transferCount =
                await dbContext.Database.SqlQuery<int>(
                    $"""
                    SELECT COUNT(*)::int AS "Value"
                    FROM credits.legacy_safeq_credit_transfers
                    WHERE command_id = {command.CommandId}
                    """)
                    .SingleAsync();

            var movementCount =
                await dbContext.Database.SqlQuery<int>(
                    $"""
                    SELECT COUNT(*)::int AS "Value"
                    FROM credits.movements
                    WHERE operation_id = {command.CommandId}
                    """)
                    .SingleAsync();

            var accountCount =
                await dbContext.Database.SqlQuery<int>(
                    $"""
                    SELECT COUNT(*)::int AS "Value"
                    FROM credits.accounts
                    WHERE owner_id = {ownerId}
                    """)
                    .SingleAsync();

            var auditCount =
                await dbContext.Database.SqlQuery<int>(
                    $"""
                    SELECT COUNT(*)::int AS "Value"
                    FROM audit.events
                    WHERE action = 'credit.legacy-safeq-transfer'
                      AND entity_id = {ownerId.ToString()}
                    """)
                    .SingleAsync();

            var documentCount =
                await dbContext.Database.SqlQuery<int>(
                    $"""
                    SELECT COUNT(*)::int AS "Value"
                    FROM financial_documents.documents
                    WHERE source_id = {command.CommandId}
                    """)
                    .SingleAsync();

            Assert.Equal(0, transferCount);
            Assert.Equal(0, movementCount);
            Assert.Equal(0, accountCount);
            Assert.Equal(0, auditCount);
            Assert.Equal(0, documentCount);
        }
        finally
        {
            await DeleteAsync(
                _factory,
                ownerId,
                command.CommandId);
        }
    }

    [Fact]
    public async Task Transfer_ConcurrentSameSafeQUserIdCreatesExactlyOneFinancialEffect()
    {
        var firstOwnerId = Guid.NewGuid();
        var secondOwnerId = Guid.NewGuid();
        var administratorUserId = Guid.NewGuid();
        var safeQUserId = "1000000000100999";

        var firstCommand =
            new LegacySafeQCreditTransferCommand(
                Guid.NewGuid(),
                administratorUserId,
                firstOwnerId,
                safeQUserId,
                SnapshotHash,
                new Money(2_500));

        var secondCommand =
            new LegacySafeQCreditTransferCommand(
                Guid.NewGuid(),
                administratorUserId,
                secondOwnerId,
                safeQUserId,
                DifferentSnapshotHash,
                new Money(3_500));

        var firstIdentity = new ExternalIdentityKey(
            "test",
            Guid.NewGuid().ToString("D"),
            Guid.NewGuid().ToString("D"));

        var secondIdentity = new ExternalIdentityKey(
            "test",
            Guid.NewGuid().ToString("D"),
            Guid.NewGuid().ToString("D"));

        using var factory = CreateTimeFactory();

        try
        {
            await SeedCustomerAsync(
                factory,
                firstOwnerId,
                firstIdentity);

            await SeedCustomerAsync(
                factory,
                secondOwnerId,
                secondIdentity);

            var outcomes = await Task.WhenAll(
                Record.ExceptionAsync(
                    async () =>
                    {
                        _ = await TransferAsync(
                            factory,
                            firstCommand);
                    }),
                Record.ExceptionAsync(
                    async () =>
                    {
                        _ = await TransferAsync(
                            factory,
                            secondCommand);
                    }));

            Assert.Single(
                outcomes,
                exception => exception is null);

            var rejected = Assert.Single(
                outcomes,
                exception => exception is not null);

            Assert.IsType<
                LegacySafeQCreditAlreadyTransferredException>(
                    rejected);

            using var verificationScope =
                factory.Services.CreateScope();

            var dbContext = verificationScope.ServiceProvider
                .GetRequiredService<FuaPayDbContext>();

            var persistedCommandIds =
                await dbContext.Database.SqlQuery<Guid>(
                    $"""
                    SELECT command_id AS "Value"
                    FROM credits.legacy_safeq_credit_transfers
                    WHERE safeq_user_id = {safeQUserId}
                    """)
                    .ToArrayAsync();

            var winningCommandId =
                Assert.Single(persistedCommandIds);

            Assert.Contains(
                winningCommandId,
                new[]
                {
                    firstCommand.CommandId,
                    secondCommand.CommandId
                });

            var transferCount =
                await dbContext.Database.SqlQuery<int>(
                    $"""
                    SELECT COUNT(*)::int AS "Value"
                    FROM credits.legacy_safeq_credit_transfers
                    WHERE safeq_user_id = {safeQUserId}
                    """)
                    .SingleAsync();

            var movementCount =
                await dbContext.Database.SqlQuery<int>(
                    $"""
                    SELECT COUNT(*)::int AS "Value"
                    FROM credits.movements
                    WHERE operation_id = {firstCommand.CommandId}
                       OR operation_id = {secondCommand.CommandId}
                    """)
                    .SingleAsync();

            var accountCount =
                await dbContext.Database.SqlQuery<int>(
                    $"""
                    SELECT COUNT(*)::int AS "Value"
                    FROM credits.accounts
                    WHERE owner_id = {firstOwnerId}
                       OR owner_id = {secondOwnerId}
                    """)
                    .SingleAsync();

            var auditCount =
                await dbContext.Database.SqlQuery<int>(
                    $"""
                    SELECT COUNT(*)::int AS "Value"
                    FROM audit.events
                    WHERE action = 'credit.legacy-safeq-transfer'
                      AND
                      (
                          entity_id = {firstOwnerId.ToString()}
                          OR entity_id = {secondOwnerId.ToString()}
                      )
                    """)
                    .SingleAsync();

            var documentCount =
                await dbContext.Database.SqlQuery<int>(
                    $"""
                    SELECT COUNT(*)::int AS "Value"
                    FROM financial_documents.documents
                    WHERE source_id = {firstCommand.CommandId}
                       OR source_id = {secondCommand.CommandId}
                    """)
                    .SingleAsync();

            Assert.Equal(1, transferCount);
            Assert.Equal(1, movementCount);
            Assert.Equal(1, accountCount);
            Assert.Equal(1, auditCount);
            Assert.Equal(0, documentCount);
        }
        finally
        {
            await DeleteAsync(
                factory,
                firstOwnerId,
                firstCommand.CommandId);

            await DeleteAsync(
                factory,
                secondOwnerId,
                secondCommand.CommandId);
        }
    }
    [Fact]
    public async Task Persistence_CheckConstraintsRejectInvalidRows()
    {
        await AssertTransferConstraintRejectedAsync(
            Guid.Empty,
            Guid.NewGuid(),
            Guid.NewGuid(),
            "1000000000101001",
            SnapshotHash,
            1,
            "ck_credits_legacy_safeq_transfers_command_not_empty");

        await AssertTransferConstraintRejectedAsync(
            Guid.NewGuid(),
            Guid.Empty,
            Guid.NewGuid(),
            "1000000000101002",
            SnapshotHash,
            1,
            "ck_credits_legacy_safeq_transfers_administrator_not_empty");

        await AssertTransferConstraintRejectedAsync(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.Empty,
            "1000000000101003",
            SnapshotHash,
            1,
            "ck_credits_legacy_safeq_transfers_owner_not_empty");

        await AssertTransferConstraintRejectedAsync(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "   ",
            SnapshotHash,
            1,
            "ck_credits_legacy_safeq_transfers_safeq_user_not_empty");

        await AssertTransferConstraintRejectedAsync(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "1000000000101004",
            "not-a-valid-sha256",
            1,
            "ck_credits_legacy_safeq_transfers_snapshot_sha256");

        await AssertTransferConstraintRejectedAsync(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "1000000000101005",
            SnapshotHash,
            0,
            "ck_credits_legacy_safeq_transfers_amount_allowed");

        await AssertTransferConstraintRejectedAsync(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "1000000000101006",
            SnapshotHash,
            10_000_001,
            "ck_credits_legacy_safeq_transfers_amount_allowed");
    }

    private async Task AssertTransferConstraintRejectedAsync(
        Guid commandId,
        Guid administratorUserId,
        Guid ownerId,
        string safeQUserId,
        string snapshotSha256,
        long amountMinorUnits,
        string expectedConstraint)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider
            .GetRequiredService<FuaPayDbContext>();

        await using var transaction =
            await dbContext.Database.BeginTransactionAsync();

        try
        {
            var exception =
                await Assert.ThrowsAsync<PostgresException>(
                    () =>
                        dbContext.Database.ExecuteSqlInterpolatedAsync(
                            $"""
                            INSERT INTO credits.legacy_safeq_credit_transfers
                            (
                                command_id,
                                administrator_user_id,
                                owner_id,
                                safeq_user_id,
                                snapshot_sha256,
                                amount_minor_units,
                                accepted_at
                            )
                            VALUES
                            (
                                {commandId},
                                {administratorUserId},
                                {ownerId},
                                {safeQUserId},
                                {snapshotSha256},
                                {amountMinorUnits},
                                {CurrentTime}
                            )
                            """));

            Assert.Equal(
                PostgresErrorCodes.CheckViolation,
                exception.SqlState);

            Assert.Equal(
                expectedConstraint,
                exception.ConstraintName);
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }
    private WebApplicationFactory<Program> CreateTimeFactory()
    {
        return _factory.WithWebHostBuilder(
            builder =>
                builder.ConfigureServices(
                    services =>
                    {
                        services.RemoveAll<TimeProvider>();
                        services.AddSingleton<TimeProvider>(
                            new FixedTimeProvider(CurrentTime));
                    }));
    }

    private static async Task SeedCustomerAsync(
        WebApplicationFactory<Program> factory,
        Guid ownerId,
        ExternalIdentityKey identityKey)
    {
        using var scope = factory.Services.CreateScope();

        var repository = scope.ServiceProvider
            .GetRequiredService<IAccessUserRepository>();

        var user = new AccessUser(
            ownerId,
            "SafeQ databázový test",
            "safeq-database@example.test",
            CurrentTime);

        user.GrantRole(
            Guid.NewGuid(),
            AccessRole.Customer,
            CurrentTime,
            RoleChangeActor.ForProcess(
                "first-login"));

        await repository.AddAsync(
            user,
            identityKey,
            CancellationToken.None);
    }

    private static async Task<LegacySafeQCreditTransferResult>
        TransferAsync(
            WebApplicationFactory<Program> factory,
            LegacySafeQCreditTransferCommand command)
    {
        using var scope = factory.Services.CreateScope();

        return await scope.ServiceProvider
            .GetRequiredService<LegacySafeQCreditTransferService>()
            .TransferAsync(command);
    }

    private static async Task DeleteAsync(
        WebApplicationFactory<Program> factory,
        Guid ownerId,
        Guid commandId)
    {
        using var scope = factory.Services.CreateScope();

        var dbContext = scope.ServiceProvider
            .GetRequiredService<FuaPayDbContext>();

        await using var transaction =
            await dbContext.Database.BeginTransactionAsync();

        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            DELETE FROM audit.events
            WHERE action = 'credit.legacy-safeq-transfer'
              AND entity_id = {ownerId.ToString()}
            """);

        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            DELETE FROM credits.legacy_safeq_credit_transfers
            WHERE command_id = {commandId}
            """);

        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            DELETE FROM credits.movements
            WHERE operation_id = {commandId}
            """);

        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            DELETE FROM credits.accounts
            WHERE owner_id = {ownerId}
            """);

        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            DELETE FROM access.role_assignments
            WHERE user_id = {ownerId}
               OR granted_by_user_id = {ownerId}
               OR revoked_by_user_id = {ownerId}
            """);

        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            DELETE FROM access.external_identities
            WHERE user_id = {ownerId}
            """);

        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            DELETE FROM access.users
            WHERE id = {ownerId}
            """);

        await transaction.CommitAsync();
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

    private sealed class ThrowAfterWriteCreditAccountRepository :
        ICreditAccountRepository
    {
        public const string FailureMessage =
            "Injected timeout after SafeQ credit write.";

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
            _inner.FindByOwnerIdAsync(
                ownerId,
                cancellationToken);

        public Task<CreditAccount?> FindByOwnerIdForUpdateAsync(
            Guid ownerId,
            CancellationToken cancellationToken) =>
            _inner.FindByOwnerIdForUpdateAsync(
                ownerId,
                cancellationToken);

        public Task<CreditAccount?> FindByIdForUpdateAsync(
            Guid accountId,
            CancellationToken cancellationToken) =>
            _inner.FindByIdForUpdateAsync(
                accountId,
                cancellationToken);

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
            await _inner.AddAsync(
                account,
                cancellationToken);

            ThrowForTarget(account.OwnerId);
        }

        public async Task SaveAsync(
            CreditAccount account,
            CancellationToken cancellationToken)
        {
            await _inner.SaveAsync(
                account,
                cancellationToken);

            ThrowForTarget(account.OwnerId);
        }

        private void ThrowForTarget(Guid ownerId)
        {
            if (ownerId == _targetOwnerId)
            {
                throw new TimeoutException(
                    FailureMessage);
            }
        }
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
}
