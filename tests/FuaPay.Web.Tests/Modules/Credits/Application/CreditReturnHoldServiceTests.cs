using FuaPay.Web.BuildingBlocks.Application;
using FuaPay.Web.BuildingBlocks.Domain;
using FuaPay.Web.Modules.Credits.Application;
using FuaPay.Web.Modules.Credits.Domain;

namespace FuaPay.Web.Tests.Modules.Credits.Application;

public sealed class CreditReturnHoldServiceTests
{
    private static readonly DateTimeOffset TestTime =
        new(2026, 8, 28, 14, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task CreateAsync_LocksAccountBeforeReadingAndAddingHold()
    {
        var fixture = new Fixture(balance: 1_000, blocking: 200);
        var returnId = Guid.NewGuid();

        var result = await fixture.Service.CreateAsync(
            new CreateCreditReturnHoldCommand(
                returnId,
                fixture.Account.OwnerId,
                new Money(800)));

        Assert.True(result.Created);
        Assert.Equal(CreditReturnHoldState.Active, result.Hold.State);
        Assert.Equal(
            ["account-lock", "hold-read", "blocking-read", "hold-add"],
            fixture.Calls);
    }

    [Fact]
    public async Task CreateAsync_SameRequest_ReplaysWithoutAvailabilityCheck()
    {
        var fixture = new Fixture(balance: 1_000, blocking: 0);
        var command = new CreateCreditReturnHoldCommand(
            Guid.NewGuid(),
            fixture.Account.OwnerId,
            new Money(600));

        var created = await fixture.Service.CreateAsync(command);
        fixture.Calls.Clear();
        var replayed = await fixture.Service.CreateAsync(command);

        Assert.True(created.Created);
        Assert.False(replayed.Created);
        Assert.Same(created.Hold, replayed.Hold);
        Assert.Equal(["account-lock", "hold-read"], fixture.Calls);
    }

    [Fact]
    public async Task CreateAsync_ConflictingReuse_IsRejected()
    {
        var fixture = new Fixture(balance: 1_000, blocking: 0);
        var returnId = Guid.NewGuid();
        await fixture.Service.CreateAsync(
            new CreateCreditReturnHoldCommand(
                returnId,
                fixture.Account.OwnerId,
                new Money(600)));

        await Assert.ThrowsAsync<CreditReturnHoldConflictException>(
            () => fixture.Service.CreateAsync(
                new CreateCreditReturnHoldCommand(
                    returnId,
                    fixture.Account.OwnerId,
                    new Money(601))));
    }

    [Fact]
    public async Task CreateAsync_WhenFullAmountIsUnavailable_AddsNothing()
    {
        var fixture = new Fixture(balance: 1_000, blocking: 401);

        await Assert.ThrowsAsync<
            InsufficientAvailableCreditForReturnHoldException>(
            () => fixture.Service.CreateAsync(
                new CreateCreditReturnHoldCommand(
                    Guid.NewGuid(),
                    fixture.Account.OwnerId,
                    new Money(600))));

        Assert.Null(fixture.Holds.Hold);
        Assert.DoesNotContain("hold-add", fixture.Calls);
    }

    private sealed class Fixture
    {
        public Fixture(long balance, long blocking)
        {
            Calls = [];
            Account = new CreditAccount(Guid.NewGuid(), Guid.NewGuid());
            Account.Credit(
                Guid.NewGuid(),
                new Money(balance),
                TestTime.AddMinutes(-1),
                "Test credit");
            Holds = new FakeHoldRepository(Calls);
            Service = new CreditReturnHoldService(
                new FakeAccountRepository(Account, Calls),
                Holds,
                new CreditAvailabilityService(
                    new FakeAvailabilityRepository(blocking, Calls)),
                new ImmediateTransaction(),
                new FixedTimeProvider(TestTime));
        }

        public List<string> Calls { get; }

        public CreditAccount Account { get; }

        public FakeHoldRepository Holds { get; }

        public CreditReturnHoldService Service { get; }
    }

    private sealed class FakeAccountRepository : ICreditAccountRepository
    {
        private readonly CreditAccount _account;
        private readonly List<string> _calls;

        public FakeAccountRepository(
            CreditAccount account,
            List<string> calls)
        {
            _account = account;
            _calls = calls;
        }

        public Task<CreditAccount?> FindByOwnerIdAsync(
            Guid ownerId,
            CancellationToken cancellationToken) =>
            Task.FromResult<CreditAccount?>(_account);

        public Task<CreditAccount?> FindByOwnerIdForUpdateAsync(
            Guid ownerId,
            CancellationToken cancellationToken)
        {
            _calls.Add("account-lock");
            return Task.FromResult<CreditAccount?>(_account);
        }

        public Task LockOwnerForAccountCreationAsync(
            Guid ownerId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task AddAsync(
            CreditAccount account,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task SaveAsync(
            CreditAccount account,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class FakeHoldRepository : ICreditReturnHoldRepository
    {
        private readonly List<string> _calls;

        public FakeHoldRepository(List<string> calls)
        {
            _calls = calls;
        }

        public CreditReturnHold? Hold { get; private set; }

        public Task<CreditReturnHold?> FindBySettlementReturnIdAsync(
            Guid settlementReturnId,
            CancellationToken cancellationToken = default)
        {
            _calls.Add("hold-read");
            return Task.FromResult(
                Hold?.SettlementReturnId == settlementReturnId
                    ? Hold
                    : null);
        }

        public Task<CreditReturnHold?> FindBySettlementReturnIdForUpdateAsync(
            Guid settlementReturnId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task AddAsync(
            CreditReturnHold hold,
            CancellationToken cancellationToken = default)
        {
            _calls.Add("hold-add");
            Hold = hold;
            return Task.CompletedTask;
        }

        public Task SaveAsync(
            CreditReturnHold hold,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeAvailabilityRepository :
        ICreditAvailabilityRepository
    {
        private readonly long _blocking;
        private readonly List<string> _calls;

        public FakeAvailabilityRepository(
            long blocking,
            List<string> calls)
        {
            _blocking = blocking;
            _calls = calls;
        }

        public Task<Money> GetTotalBlockingAmountAsync(
            Guid creditAccountId,
            CancellationToken cancellationToken = default)
        {
            _calls.Add("blocking-read");
            return Task.FromResult(new Money(_blocking));
        }
    }

    private sealed class ImmediateTransaction : IApplicationTransaction
    {
        public Task<T> ExecuteAsync<T>(
            Func<CancellationToken, Task<T>> operation,
            CancellationToken cancellationToken = default) =>
            operation(cancellationToken);
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;

        public FixedTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public override DateTimeOffset GetUtcNow() => _utcNow;
    }
}
