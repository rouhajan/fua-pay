using FuaPay.Web.BuildingBlocks.Application;
using FuaPay.Web.BuildingBlocks.Domain;
using FuaPay.Web.Modules.Credits.Application;
using FuaPay.Web.Modules.Credits.Domain;

namespace FuaPay.Web.Tests.Modules.Credits.Application;

public sealed class CreditServiceTests
{
    private static readonly DateTimeOffset CurrentTime =
        new(2026, 7, 25, 14, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task CreditAsync_WhenAccountDoesNotExist_CreatesIt()
    {
        var ownerId = Guid.NewGuid();
        var operationId = Guid.NewGuid();
        var repository = new FakeCreditAccountRepository();
        var service = CreateService(repository);

        var movement = await service.CreditAsync(
            ownerId,
            operationId,
            new Money(15_000),
            "Potvrzená platba");

        Assert.NotNull(repository.Account);
        Assert.NotEqual(Guid.Empty, repository.Account.Id);
        Assert.Equal(ownerId, repository.Account.OwnerId);
        Assert.Equal(new Money(15_000), repository.Account.Balance);
        Assert.Equal(operationId, movement.OperationId);
        Assert.Equal(CurrentTime, movement.RecordedAt);
        Assert.Equal(1, repository.AddCalls);
        Assert.Equal(0, repository.SaveCalls);
        Assert.Equal(2, repository.FindForUpdateCalls);
        Assert.Equal(1, repository.CreationLockCalls);
    }

    [Fact]
    public async Task CreditAsync_WhenAccountExists_SavesIt()
    {
        var account = CreateAccount();
        var repository = new FakeCreditAccountRepository
        {
            Account = account
        };

        var service = CreateService(repository);

        await service.CreditAsync(
            account.OwnerId,
            Guid.NewGuid(),
            new Money(2_500),
            "Ruční korekce");

        Assert.Equal(new Money(2_500), account.Balance);
        Assert.Equal(0, repository.AddCalls);
        Assert.Equal(1, repository.SaveCalls);
        Assert.Equal(1, repository.FindForUpdateCalls);
        Assert.Equal(0, repository.CreationLockCalls);
    }

    [Fact]
    public async Task DebitAsync_DecreasesBalanceAndSavesAccount()
    {
        var account = CreateAccount();

        account.Credit(
            Guid.NewGuid(),
            new Money(10_000),
            CurrentTime.AddMinutes(-1),
            "Počáteční kredit");

        var repository = new FakeCreditAccountRepository
        {
            Account = account
        };

        var service = CreateService(repository);

        var movement = await service.DebitAsync(
            account.OwnerId,
            Guid.NewGuid(),
            new Money(4_000),
            "Úhrada zakázky");

        Assert.Equal(new Money(6_000), account.Balance);
        Assert.Equal(new Money(6_000), movement.BalanceAfter);
        Assert.Equal(CurrentTime, movement.RecordedAt);
        Assert.Equal(1, repository.SaveCalls);
    }

    [Fact]
    public async Task DebitAsync_WhenAccountDoesNotExist_Throws()
    {
        var ownerId = Guid.NewGuid();
        var repository = new FakeCreditAccountRepository();
        var service = CreateService(repository);

        var exception =
            await Assert.ThrowsAsync<CreditAccountNotFoundException>(
                async () =>
                {
                    _ = await service.DebitAsync(
                        ownerId,
                        Guid.NewGuid(),
                        new Money(1_000),
                        "Úhrada zakázky");
                });

        Assert.Equal(ownerId, exception.OwnerId);
        Assert.Equal(0, repository.AddCalls);
        Assert.Equal(0, repository.SaveCalls);
    }

    [Fact]
    public async Task DebitAsync_WhenBalanceIsInsufficient_DoesNotSave()
    {
        var account = CreateAccount();

        account.Credit(
            Guid.NewGuid(),
            new Money(1_000),
            CurrentTime.AddMinutes(-1),
            "Počáteční kredit");

        var repository = new FakeCreditAccountRepository
        {
            Account = account
        };

        var service = CreateService(repository);

        await Assert.ThrowsAsync<InsufficientCreditException>(
            async () =>
            {
                _ = await service.DebitAsync(
                    account.OwnerId,
                    Guid.NewGuid(),
                    new Money(1_001),
                    "Úhrada zakázky");
            });

        Assert.Equal(new Money(1_000), account.Balance);
        Assert.Equal(0, repository.SaveCalls);
    }

    [Fact]
    public async Task CreditAsync_WhenOperationIsDuplicate_DoesNotSave()
    {
        var account = CreateAccount();
        var operationId = Guid.NewGuid();

        account.Credit(
            operationId,
            new Money(3_000),
            CurrentTime.AddMinutes(-1),
            "První zpracování");

        var repository = new FakeCreditAccountRepository
        {
            Account = account
        };

        var service = CreateService(repository);

        await Assert.ThrowsAsync<DuplicateCreditOperationException>(
            async () =>
            {
                _ = await service.CreditAsync(
                    account.OwnerId,
                    operationId,
                    new Money(3_000),
                    "Opakované zpracování");
            });

        Assert.Equal(new Money(3_000), account.Balance);
        Assert.Equal(0, repository.SaveCalls);
    }

    private static CreditService CreateService(
        ICreditAccountRepository repository)
    {
        return new CreditService(
            repository,
            new ImmediateTransaction(),
            new FixedTimeProvider(CurrentTime));
    }

    private static CreditAccount CreateAccount()
    {
        return new CreditAccount(
            Guid.NewGuid(),
            Guid.NewGuid());
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _currentTime;

        public FixedTimeProvider(DateTimeOffset currentTime)
        {
            _currentTime = currentTime;
        }

        public override DateTimeOffset GetUtcNow()
        {
            return _currentTime;
        }
    }

    private sealed class FakeCreditAccountRepository :
        ICreditAccountRepository
    {
        public CreditAccount? Account { get; set; }

        public int AddCalls { get; private set; }

        public int SaveCalls { get; private set; }

        public int FindForUpdateCalls { get; private set; }

        public int CreationLockCalls { get; private set; }

        public Task<CreditAccount?> FindByOwnerIdAsync(
            Guid ownerId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var result =
                Account?.OwnerId == ownerId
                    ? Account
                    : null;

            return Task.FromResult(result);
        }

        public Task<CreditAccount?> FindByOwnerIdForUpdateAsync(
            Guid ownerId,
            CancellationToken cancellationToken)
        {
            FindForUpdateCalls++;

            return FindByOwnerIdAsync(
                ownerId,
                cancellationToken);
        }

        public Task LockOwnerForAccountCreationAsync(
            Guid ownerId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CreationLockCalls++;

            return Task.CompletedTask;
        }

        public Task AddAsync(
            CreditAccount account,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            Account = account;
            AddCalls++;

            return Task.CompletedTask;
        }

        public Task SaveAsync(
            CreditAccount account,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            Assert.Same(Account, account);
            SaveCalls++;

            return Task.CompletedTask;
        }
    }

    private sealed class ImmediateTransaction :
        IApplicationTransaction
    {
        public Task<T> ExecuteAsync<T>(
            Func<CancellationToken, Task<T>> operation,
            CancellationToken cancellationToken = default) =>
            operation(cancellationToken);
    }
}
