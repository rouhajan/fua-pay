using FuaPay.Web.BuildingBlocks.Domain;
using FuaPay.Web.BuildingBlocks.Persistence;
using FuaPay.Web.Modules.Credits.Application;
using FuaPay.Web.Modules.Credits.Domain;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FuaPay.DatabaseTests;

public sealed class CreditPersistenceTests :
    IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly DateTimeOffset TestTime =
        new(2026, 7, 26, 10, 0, 0, TimeSpan.Zero);

    private readonly WebApplicationFactory<Program> _factory;

    public CreditPersistenceTests(
        WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Database_IsReachableAndHasNoPendingMigrations()
    {
        using var scope =
            _factory.Services.CreateScope();

        var dbContext =
            scope.ServiceProvider
                .GetRequiredService<FuaPayDbContext>();

        Assert.True(
            await dbContext.Database.CanConnectAsync());

        var pendingMigrations =
            await dbContext.Database
                .GetPendingMigrationsAsync();

        Assert.Empty(pendingMigrations);
    }

    [Fact]
    public async Task CreditAndDebit_RoundTripThroughPostgreSql()
    {
        using var scope =
            _factory.Services.CreateScope();

        var dbContext =
            scope.ServiceProvider
                .GetRequiredService<FuaPayDbContext>();

        var service =
            scope.ServiceProvider
                .GetRequiredService<CreditService>();

        var repository =
            scope.ServiceProvider
                .GetRequiredService<ICreditAccountRepository>();

        await using var transaction =
            await dbContext.Database.BeginTransactionAsync();

        try
        {
            var ownerId = Guid.NewGuid();

            await service.CreditAsync(
                ownerId,
                Guid.NewGuid(),
                new Money(15_000),
                "Integrační test připsání");

            var creditedAccount =
                await repository.FindByOwnerIdAsync(
                    ownerId,
                    CancellationToken.None);

            Assert.NotNull(creditedAccount);
            Assert.Equal(
                new Money(15_000),
                creditedAccount.Balance);
            Assert.Single(creditedAccount.Movements);

            await service.DebitAsync(
                ownerId,
                Guid.NewGuid(),
                new Money(4_000),
                "Integrační test odečtení");

            var debitedAccount =
                await repository.FindByOwnerIdAsync(
                    ownerId,
                    CancellationToken.None);

            Assert.NotNull(debitedAccount);
            Assert.Equal(
                new Money(11_000),
                debitedAccount.Balance);
            Assert.Equal(
                2,
                debitedAccount.Movements.Count);
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    [Fact]
    public async Task CreditAsync_WhenOperationAlreadyExistsOnSameAccount_ThrowsAndKeepsStoredState()
    {
        var ownerId = Guid.NewGuid();
        var operationId = Guid.NewGuid();

        try
        {
            await SeedCreditAsync(
                ownerId,
                operationId,
                new Money(5_000),
                "První zpracování");

            using (var scope = _factory.Services.CreateScope())
            {
                var service =
                    scope.ServiceProvider
                        .GetRequiredService<CreditService>();

                var exception =
                    await Assert.ThrowsAsync<
                        DuplicateCreditOperationException>(
                        async () =>
                        {
                            _ = await service.CreditAsync(
                                ownerId,
                                operationId,
                                new Money(5_000),
                                "Opakované zpracování");
                        });

                Assert.Equal(
                    operationId,
                    exception.OperationId);
            }

            using var verificationScope =
                _factory.Services.CreateScope();

            var repository =
                verificationScope.ServiceProvider
                    .GetRequiredService<
                        ICreditAccountRepository>();

            var persistedAccount =
                Assert.IsType<CreditAccount>(
                    await repository.FindByOwnerIdAsync(
                        ownerId,
                        CancellationToken.None));

            Assert.Equal(
                new Money(5_000),
                persistedAccount.Balance);

            var movement =
                Assert.Single(persistedAccount.Movements);

            Assert.Equal(
                operationId,
                movement.OperationId);
        }
        finally
        {
            await DeleteAccountsAsync(ownerId);
        }
    }

    [Fact]
    public async Task CreditAsync_WhenOperationExistsOnDifferentAccount_ThrowsAndDoesNotCreateSecondAccount()
    {
        var firstOwnerId = Guid.NewGuid();
        var secondOwnerId = Guid.NewGuid();
        var operationId = Guid.NewGuid();

        try
        {
            await SeedCreditAsync(
                firstOwnerId,
                operationId,
                new Money(7_500),
                "Platba prvního vlastníka");

            using (var scope = _factory.Services.CreateScope())
            {
                var service =
                    scope.ServiceProvider
                        .GetRequiredService<CreditService>();

                var exception =
                    await Assert.ThrowsAsync<
                        DuplicateCreditOperationException>(
                        async () =>
                        {
                            _ = await service.CreditAsync(
                                secondOwnerId,
                                operationId,
                                new Money(7_500),
                                "Duplicitní platba jiného vlastníka");
                        });

                Assert.Equal(
                    operationId,
                    exception.OperationId);
            }

            using var verificationScope =
                _factory.Services.CreateScope();

            var repository =
                verificationScope.ServiceProvider
                    .GetRequiredService<
                        ICreditAccountRepository>();

            var firstAccount =
                await repository.FindByOwnerIdAsync(
                    firstOwnerId,
                    CancellationToken.None);

            var secondAccount =
                await repository.FindByOwnerIdAsync(
                    secondOwnerId,
                    CancellationToken.None);

            Assert.NotNull(firstAccount);
            Assert.Null(secondAccount);

            Assert.Equal(
                new Money(7_500),
                firstAccount.Balance);

            Assert.Single(firstAccount.Movements);
        }
        finally
        {
            await DeleteAccountsAsync(
                firstOwnerId,
                secondOwnerId);
        }
    }

    [Fact]
    public async Task SaveAsync_WhenAccountWasChangedAfterLoad_ThrowsAndDoesNotPersistStaleMovement()
    {
        var ownerId = Guid.NewGuid();
        var initialOperationId = Guid.NewGuid();
        var winningOperationId = Guid.NewGuid();
        var staleOperationId = Guid.NewGuid();

        try
        {
            await SeedCreditAsync(
                ownerId,
                initialOperationId,
                new Money(10_000),
                "Počáteční kredit");

            using (
                var winningScope =
                    _factory.Services.CreateScope())
            using (
                var staleScope =
                    _factory.Services.CreateScope())
            {
                var winningRepository =
                    winningScope.ServiceProvider
                        .GetRequiredService<
                            ICreditAccountRepository>();

                var staleRepository =
                    staleScope.ServiceProvider
                        .GetRequiredService<
                            ICreditAccountRepository>();

                var winningAccount =
                    Assert.IsType<CreditAccount>(
                        await winningRepository
                            .FindByOwnerIdAsync(
                                ownerId,
                                CancellationToken.None));

                var staleAccount =
                    Assert.IsType<CreditAccount>(
                        await staleRepository
                            .FindByOwnerIdAsync(
                                ownerId,
                                CancellationToken.None));

                winningAccount.Credit(
                    winningOperationId,
                    new Money(2_000),
                    TestTime.AddMinutes(1),
                    "První souběžná změna");

                staleAccount.Credit(
                    staleOperationId,
                    new Money(3_000),
                    TestTime.AddMinutes(2),
                    "Zastaralá souběžná změna");

                await winningRepository.SaveAsync(
                    winningAccount,
                    CancellationToken.None);

                var exception =
                    await Assert.ThrowsAsync<
                        CreditAccountConcurrencyException>(
                        () => staleRepository.SaveAsync(
                            staleAccount,
                            CancellationToken.None));

                Assert.Equal(
                    ownerId,
                    exception.OwnerId);
            }

            using var verificationScope =
                _factory.Services.CreateScope();

            var verificationRepository =
                verificationScope.ServiceProvider
                    .GetRequiredService<
                        ICreditAccountRepository>();

            var persistedAccount =
                Assert.IsType<CreditAccount>(
                    await verificationRepository
                        .FindByOwnerIdAsync(
                            ownerId,
                            CancellationToken.None));

            Assert.Equal(
                new Money(12_000),
                persistedAccount.Balance);

            Assert.Collection(
                persistedAccount.Movements,
                movement =>
                    Assert.Equal(
                        initialOperationId,
                        movement.OperationId),
                movement =>
                    Assert.Equal(
                        winningOperationId,
                        movement.OperationId));

            Assert.DoesNotContain(
                persistedAccount.Movements,
                movement =>
                    movement.OperationId ==
                    staleOperationId);
        }
        finally
        {
            await DeleteAccountsAsync(ownerId);
        }
    }

    [Fact]
    public async Task AddAsync_WhenAnotherAccountForOwnerAlreadyExists_ThrowsAndKeepsWinningAccount()
    {
        var ownerId = Guid.NewGuid();
        var winningOperationId = Guid.NewGuid();
        var losingOperationId = Guid.NewGuid();

        try
        {
            using (
                var winningScope =
                    _factory.Services.CreateScope())
            using (
                var losingScope =
                    _factory.Services.CreateScope())
            {
                var winningRepository =
                    winningScope.ServiceProvider
                        .GetRequiredService<
                            ICreditAccountRepository>();

                var losingRepository =
                    losingScope.ServiceProvider
                        .GetRequiredService<
                            ICreditAccountRepository>();

                var winningAccount =
                    new CreditAccount(
                        Guid.NewGuid(),
                        ownerId);

                winningAccount.Credit(
                    winningOperationId,
                    new Money(6_000),
                    TestTime.AddMinutes(3),
                    "První vytvoření účtu");

                var losingAccount =
                    new CreditAccount(
                        Guid.NewGuid(),
                        ownerId);

                losingAccount.Credit(
                    losingOperationId,
                    new Money(9_000),
                    TestTime.AddMinutes(4),
                    "Souběžné vytvoření účtu");

                await winningRepository.AddAsync(
                    winningAccount,
                    CancellationToken.None);

                var exception =
                    await Assert.ThrowsAsync<
                        CreditAccountConcurrencyException>(
                        () => losingRepository.AddAsync(
                            losingAccount,
                            CancellationToken.None));

                Assert.Equal(
                    ownerId,
                    exception.OwnerId);
            }

            using var verificationScope =
                _factory.Services.CreateScope();

            var verificationRepository =
                verificationScope.ServiceProvider
                    .GetRequiredService<
                        ICreditAccountRepository>();

            var persistedAccount =
                Assert.IsType<CreditAccount>(
                    await verificationRepository
                        .FindByOwnerIdAsync(
                            ownerId,
                            CancellationToken.None));

            Assert.Equal(
                new Money(6_000),
                persistedAccount.Balance);

            var movement =
                Assert.Single(
                    persistedAccount.Movements);

            Assert.Equal(
                winningOperationId,
                movement.OperationId);

            Assert.DoesNotContain(
                persistedAccount.Movements,
                item =>
                    item.OperationId ==
                    losingOperationId);
        }
        finally
        {
            await DeleteAccountsAsync(ownerId);
        }
    }
    private async Task SeedCreditAsync(
        Guid ownerId,
        Guid operationId,
        Money amount,
        string description)
    {
        using var scope =
            _factory.Services.CreateScope();

        var service =
            scope.ServiceProvider
                .GetRequiredService<CreditService>();

        await service.CreditAsync(
            ownerId,
            operationId,
            amount,
            description);
    }

    private async Task DeleteAccountsAsync(
        params Guid[] ownerIds)
    {
        using var scope =
            _factory.Services.CreateScope();

        var dbContext =
            scope.ServiceProvider
                .GetRequiredService<FuaPayDbContext>();

        await using var transaction =
            await dbContext.Database.BeginTransactionAsync();

        foreach (var ownerId in ownerIds.Distinct())
        {
            await dbContext.Database
                .ExecuteSqlInterpolatedAsync(
                    $"""
                    DELETE FROM credits.movements
                    WHERE account_id IN
                    (
                        SELECT id
                        FROM credits.accounts
                        WHERE owner_id = {ownerId}
                    )
                    """);

            await dbContext.Database
                .ExecuteSqlInterpolatedAsync(
                    $"""
                    DELETE FROM credits.accounts
                    WHERE owner_id = {ownerId}
                    """);
        }

        await transaction.CommitAsync();
    }
}
