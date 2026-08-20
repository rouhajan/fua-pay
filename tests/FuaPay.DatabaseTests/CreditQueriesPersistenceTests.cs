using FuaPay.Web.BuildingBlocks.Domain;
using FuaPay.Web.BuildingBlocks.Persistence;
using FuaPay.Web.Modules.Credits.Application;
using FuaPay.Web.Modules.Credits.Domain;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FuaPay.DatabaseTests;

public sealed class CreditQueriesPersistenceTests :
    IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly DateTimeOffset TestTime =
        new(
            2026,
            7,
            28,
            17,
            0,
            0,
            TimeSpan.Zero);

    private readonly WebApplicationFactory<Program> _factory;

    public CreditQueriesPersistenceTests(
        WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Queries_ReturnOwnerBalanceAndNewestMovementsOnly()
    {
        using var scope =
            _factory.Services.CreateScope();

        var dbContext =
            scope.ServiceProvider
                .GetRequiredService<FuaPayDbContext>();

        var repository =
            scope.ServiceProvider
                .GetRequiredService<ICreditAccountRepository>();

        var queries =
            scope.ServiceProvider
                .GetRequiredService<ICreditQueries>();

        await using var transaction =
            await dbContext.Database
                .BeginTransactionAsync();

        try
        {
            var ownerId = Guid.NewGuid();
            var otherOwnerId = Guid.NewGuid();
            var creditOperationId = Guid.NewGuid();
            var debitOperationId = Guid.NewGuid();

            var account = new CreditAccount(
                Guid.NewGuid(),
                ownerId);

            account.Credit(
                creditOperationId,
                new Money(100_000),
                TestTime,
                "Dobití kreditu");

            account.Debit(
                debitOperationId,
                new Money(25_000),
                TestTime.AddMinutes(1),
                "Úhrada zakázky");

            var otherAccount = new CreditAccount(
                Guid.NewGuid(),
                otherOwnerId);

            otherAccount.Credit(
                Guid.NewGuid(),
                new Money(999_000),
                TestTime,
                "Cizí pohyb");

            await repository.AddAsync(
                account,
                CancellationToken.None);

            await repository.AddAsync(
                otherAccount,
                CancellationToken.None);

            var summary = Assert.IsType<CreditAccountSummary>(
                await queries.FindAccountForOwnerAsync(
                    ownerId));

            Assert.Equal(account.Id, summary.Id);
            Assert.Equal(ownerId, summary.OwnerId);
            Assert.Equal(75_000, summary.BalanceMinorUnits);

            var debit = Assert.IsType<CreditMovementListItem>(
                await queries.FindMovementForOwnerAsync(
                    ownerId,
                    debitOperationId));

            Assert.Equal(CreditMovementType.Debit, debit.Type);
            Assert.Equal(25_000, debit.AmountMinorUnits);
            Assert.Null(
                await queries.FindMovementForOwnerAsync(
                    otherOwnerId,
                    debitOperationId));

            var firstPage =
                await queries.ListMovementsForOwnerAsync(
                    ownerId,
                    new CreditMovementPageRequest(
                        limit: 1));

            Assert.Equal(2L, firstPage.TotalCount);
            Assert.True(firstPage.HasMore);

            var newest = Assert.Single(firstPage.Items);

            Assert.Equal(debitOperationId, newest.OperationId);
            Assert.Equal(CreditMovementType.Debit, newest.Type);
            Assert.Equal(2L, newest.Sequence);

            var secondPage =
                await queries.ListMovementsForOwnerAsync(
                    ownerId,
                    new CreditMovementPageRequest(
                        offset: 1,
                        limit: 1));

            var oldest = Assert.Single(secondPage.Items);

            Assert.Equal(creditOperationId, oldest.OperationId);
            Assert.Equal(CreditMovementType.Credit, oldest.Type);
            Assert.False(secondPage.HasMore);
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }
}
