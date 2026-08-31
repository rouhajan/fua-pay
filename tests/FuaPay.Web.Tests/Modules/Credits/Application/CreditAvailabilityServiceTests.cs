using FuaPay.Web.BuildingBlocks.Domain;
using FuaPay.Web.Modules.Credits.Application;
using FuaPay.Web.Modules.Credits.Domain;

namespace FuaPay.Web.Tests.Modules.Credits.Application;

public sealed class CreditAvailabilityServiceTests
{
    [Fact]
    public async Task GetAvailableAsync_SubtractsAllBlockingCredit()
    {
        var account = CreateAccount(balance: 1_000);
        var service = CreateService(blocking: 450);

        var available = await service.GetAvailableAsync(account);

        Assert.Equal(new Money(550), available);
    }

    [Fact]
    public async Task GetAvailableAsync_FromSummary_SubtractsAllBlockingCredit()
    {
        var account = new CreditAccountSummary(
            Guid.NewGuid(),
            Guid.NewGuid(),
            BalanceMinorUnits: 1_000,
            Version: 1);
        var service = CreateService(blocking: 450);

        var available = await service.GetAvailableAsync(account);

        Assert.Equal(new Money(550), available);
    }

    [Fact]
    public async Task GetAvailableExcludingAsync_SubtractsOnlyOtherBlockingCredit()
    {
        var account = CreateAccount(balance: 1_000);
        var service = CreateService(blocking: 700);

        var available = await service.GetAvailableExcludingAsync(
            account,
            new Money(400));

        Assert.Equal(new Money(700), available);
    }

    [Fact]
    public async Task GetAvailableExcludingAsync_WhenOwnBlockExceedsTotal_FailsClosed()
    {
        var account = CreateAccount(balance: 1_000);
        var service = CreateService(blocking: 300);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => service.GetAvailableExcludingAsync(
                account,
                new Money(301)));
    }

    [Fact]
    public async Task GetAvailableAsync_WhenBlockingAdditionOverflowed_FailsClosed()
    {
        var account = CreateAccount(balance: 1_000);
        var service = CreateService(blocking: long.MinValue);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => service.GetAvailableAsync(account));
    }

    private static CreditAvailabilityService CreateService(long blocking) =>
        new(new StubAvailabilityRepository(new Money(blocking)));

    private static CreditAccount CreateAccount(long balance)
    {
        var account = new CreditAccount(Guid.NewGuid(), Guid.NewGuid());
        account.Credit(
            Guid.NewGuid(),
            new Money(balance),
            DateTimeOffset.UtcNow,
            "Test credit");
        return account;
    }

    private sealed class StubAvailabilityRepository :
        ICreditAvailabilityRepository
    {
        private readonly Money _blocking;

        public StubAvailabilityRepository(Money blocking)
        {
            _blocking = blocking;
        }

        public Task<Money> GetTotalBlockingAmountAsync(
            Guid creditAccountId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_blocking);
        }
    }
}
