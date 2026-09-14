using FuaPay.Web.BuildingBlocks.Persistence;
using FuaPay.Web.Modules.Credits.Application;
using FuaPay.Web.Modules.Credits.Domain;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FuaPay.DatabaseTests;

public sealed class ManualCreditTopUpPersistenceTests :
    IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ManualCreditTopUpPersistenceTests(
        WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task TopUp_RoundTripsPayloadAndReplayConflictThroughPostgreSql()
    {
        var command = CreateCommand("Přijatá hotovost");

        try
        {
            var first = await TopUpAsync(_factory, command);
            var replay = await TopUpAsync(_factory, command);

            Assert.Equal(first, replay);
            Assert.Equal(CreditMovementType.Credit, first.MovementType);
            Assert.Equal(command.Amount, first.Amount);
            Assert.Equal(command.Amount, first.BalanceAfter);
            Assert.Contains("Ruční dobití kreditu", first.Description);
            Assert.DoesNotContain("Administrativní korekce", first.Description);

            using var scope = _factory.Services.CreateScope();
            var commandRepository = scope.ServiceProvider
                .GetRequiredService<IManualCreditTopUpCommandRepository>();
            var persisted = Assert.IsType<PersistedManualCreditTopUpCommand>(
                await commandRepository.FindAsync(command.CommandId));

            Assert.Equal(command.CommandId, persisted.Command.CommandId);
            Assert.Equal(
                command.AdministratorUserId,
                persisted.Command.AdministratorUserId);
            Assert.Equal(command.OwnerId, persisted.Command.OwnerId);
            Assert.Equal(command.Amount, persisted.Command.Amount);
            Assert.Equal(command.Note, persisted.Command.Note);
            Assert.NotEqual(default, persisted.AcceptedAt);

            var dbContext = scope.ServiceProvider
                .GetRequiredService<FuaPayDbContext>();
            var commandCount = await CountCommandsAsync(
                dbContext,
                command.CommandId);
            var movementCount = await CountMovementsAsync(
                dbContext,
                command.CommandId);
            var auditCount = await CountAuditsAsync(
                dbContext,
                command.OwnerId);

            Assert.Equal(1, commandCount);
            Assert.Equal(1, movementCount);
            Assert.Equal(1, auditCount);

            var conflicting = new ManualCreditTopUpCommand(
                command.CommandId,
                command.AdministratorUserId,
                command.OwnerId,
                new Money(command.Amount.MinorUnits + 1),
                command.Note);

            await Assert.ThrowsAsync<ManualCreditTopUpCommandConflictException>(
                () => TopUpAsync(_factory, conflicting));
        }
        finally
        {
            await DeleteAsync(command);
        }
    }

    [Fact]
    public async Task TopUp_ConcurrentDuplicateCreatesOneFinancialEffect()
    {
        var command = CreateCommand("Souběžné ruční dobití");

        try
        {
            var results = await Task.WhenAll(
                TopUpAsync(_factory, command),
                TopUpAsync(_factory, command));

            Assert.Equal(results[0], results[1]);

            using var scope = _factory.Services.CreateScope();
            var accounts = scope.ServiceProvider
                .GetRequiredService<ICreditAccountRepository>();
            var account = Assert.IsType<CreditAccount>(
                await accounts.FindByOwnerIdAsync(
                    command.OwnerId,
                    CancellationToken.None));

            var movement = Assert.Single(account.Movements);
            Assert.Equal(command.CommandId, movement.OperationId);
            Assert.Equal(command.Amount, account.Balance);

            var dbContext = scope.ServiceProvider
                .GetRequiredService<FuaPayDbContext>();
            Assert.Equal(
                1,
                await CountCommandsAsync(dbContext, command.CommandId));
            Assert.Equal(
                1,
                await CountMovementsAsync(dbContext, command.CommandId));
            Assert.Equal(
                1,
                await CountAuditsAsync(dbContext, command.OwnerId));
        }
        finally
        {
            await DeleteAsync(command);
        }
    }

    [Fact]
    public async Task TopUp_FailureInsideTransactionLeavesNoPartialState()
    {
        var command = CreateCommand("Rollback ručního dobití");
        using var failureFactory =
            CreateCreditRepositoryFailureFactory(command.OwnerId);

        try
        {
            var exception = await Assert.ThrowsAsync<TimeoutException>(
                () => TopUpAsync(failureFactory, command));

            Assert.Equal(
                ThrowAfterWriteCreditAccountRepository.FailureMessage,
                exception.Message);

            using var scope = _factory.Services.CreateScope();
            var dbContext = scope.ServiceProvider
                .GetRequiredService<FuaPayDbContext>();

            Assert.Equal(
                0,
                await CountCommandsAsync(dbContext, command.CommandId));
            Assert.Equal(
                0,
                await CountMovementsAsync(dbContext, command.CommandId));
            Assert.Equal(
                0,
                await CountAuditsAsync(dbContext, command.OwnerId));

            var accountCount = await dbContext.Database
                .SqlQuery<int>(
                    $"""
                    SELECT count(*)::int AS "Value"
                    FROM credits.accounts
                    WHERE owner_id = {command.OwnerId}
                    """)
                .SingleAsync();

            Assert.Equal(0, accountCount);
        }
        finally
        {
            await DeleteAsync(command);
        }
    }

    private WebApplicationFactory<Program>
        CreateCreditRepositoryFailureFactory(Guid targetOwnerId)
    {
        return _factory.WithWebHostBuilder(
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

    private static ManualCreditTopUpCommand CreateCommand(string note) =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            new Money(2_500),
            note);

    private static async Task<ManualCreditTopUpResult> TopUpAsync(
        WebApplicationFactory<Program> factory,
        ManualCreditTopUpCommand command)
    {
        using var scope = factory.Services.CreateScope();

        return await scope.ServiceProvider
            .GetRequiredService<ManualCreditTopUpService>()
            .TopUpAsync(command);
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

    private async Task DeleteAsync(ManualCreditTopUpCommand command)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider
            .GetRequiredService<FuaPayDbContext>();
        await using var transaction =
            await dbContext.Database.BeginTransactionAsync();

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

        await transaction.CommitAsync();
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
            _inner.LockOwnerForAccountCreationAsync(ownerId, cancellationToken);

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
}
