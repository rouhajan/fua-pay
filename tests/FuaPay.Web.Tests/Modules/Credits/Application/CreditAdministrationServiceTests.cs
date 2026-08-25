using FuaPay.Web.BuildingBlocks.Application;
using FuaPay.Web.BuildingBlocks.Auditing;
using FuaPay.Web.Modules.Credits.Application;
using FuaPay.Web.Modules.Credits.Domain;

namespace FuaPay.Web.Tests.Modules.Credits.Application;

public sealed class CreditAdministrationServiceTests
{
    private static readonly DateTimeOffset CurrentTime =
        new(2026, 8, 12, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task AdjustAsync_SameCommandAndPayloadReturnsOriginalResult()
    {
        var fixture = new Fixture();
        var command = fixture.CreateCommand(new Money(2_500));

        var first = await fixture.Service.AdjustAsync(command);
        var replay = await fixture.Service.AdjustAsync(command);

        Assert.Equal(first, replay);
        Assert.Equal(command.CommandId, first.CommandId);
        Assert.Equal(1, fixture.Accounts.SaveCalls);
        Assert.Single(fixture.Audit.Entries);
        Assert.Single(fixture.Accounts.Account!.Movements);
    }

    [Fact]
    public async Task AdjustAsync_SameCommandWithDifferentPayloadConflicts()
    {
        var fixture = new Fixture();
        var command = fixture.CreateCommand(new Money(2_500));
        await fixture.Service.AdjustAsync(command);

        var conflicting = fixture.CreateCommand(
            new Money(2_501),
            command.CommandId);

        await Assert.ThrowsAsync<CreditAdjustmentCommandConflictException>(
            () => fixture.Service.AdjustAsync(conflicting));
        Assert.Equal(1, fixture.Accounts.SaveCalls);
        Assert.Single(fixture.Audit.Entries);
    }

    [Fact]
    public async Task AdjustAsync_UsesCommandIdAsLedgerOperationId()
    {
        var fixture = new Fixture();
        var command = fixture.CreateCommand(new Money(-1_000));
        fixture.Accounts.Account!.Credit(
            Guid.NewGuid(),
            new Money(2_000),
            CurrentTime.AddMinutes(-1),
            "initial");

        var result = await fixture.Service.AdjustAsync(command);

        Assert.Equal(CreditMovementType.Debit, result.MovementType);
        Assert.Contains(
            fixture.Accounts.Account.Movements,
            movement => movement.OperationId == command.CommandId);
    }

    private sealed class Fixture
    {
        public Fixture()
        {
            OwnerId = Guid.NewGuid();
            AdministratorId = Guid.NewGuid();
            Accounts.Account = new CreditAccount(Guid.NewGuid(), OwnerId);
            Commands = new FakeCommandRepository(Accounts);
            var transaction = new ImmediateTransaction();
            Service = new CreditAdministrationService(
                new CreditService(
                    Accounts,
                    transaction,
                    new FixedTimeProvider(CurrentTime)),
                Commands,
                transaction,
                Audit,
                new FixedTimeProvider(CurrentTime));
        }

        public Guid OwnerId { get; }

        public Guid AdministratorId { get; }

        public FakeCreditAccountRepository Accounts { get; } = new();

        public FakeCommandRepository Commands { get; }

        public RecordingAuditTrail Audit { get; } = new();

        public CreditAdministrationService Service { get; }

        public CreditAdjustmentCommand CreateCommand(
            Money amount,
            Guid? commandId = null) =>
            new(
                commandId ?? Guid.NewGuid(),
                AdministratorId,
                OwnerId,
                amount,
                "M1 test");
    }

    private sealed class FakeCreditAccountRepository : ICreditAccountRepository
    {
        public CreditAccount? Account { get; set; }

        public int SaveCalls { get; private set; }

        public Task<CreditAccount?> FindByOwnerIdAsync(
            Guid ownerId,
            CancellationToken cancellationToken) =>
            Task.FromResult(Account?.OwnerId == ownerId ? Account : null);

        public Task<CreditAccount?> FindByOwnerIdForUpdateAsync(
            Guid ownerId,
            CancellationToken cancellationToken) =>
            FindByOwnerIdAsync(ownerId, cancellationToken);

        public Task LockOwnerForAccountCreationAsync(
            Guid ownerId,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task AddAsync(
            CreditAccount account,
            CancellationToken cancellationToken)
        {
            Account = account;
            SaveCalls++;
            return Task.CompletedTask;
        }

        public Task SaveAsync(
            CreditAccount account,
            CancellationToken cancellationToken)
        {
            Assert.Same(Account, account);
            SaveCalls++;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeCommandRepository : ICreditAdjustmentCommandRepository
    {
        private readonly FakeCreditAccountRepository _accounts;
        private readonly Dictionary<Guid, (CreditAdjustmentCommand Command, DateTimeOffset AcceptedAt)>
            _commands = [];

        public FakeCommandRepository(FakeCreditAccountRepository accounts)
        {
            _accounts = accounts;
        }

        public Task<PersistedCreditAdjustmentCommand?> FindAsync(
            Guid commandId,
            CancellationToken cancellationToken = default)
        {
            if (!_commands.TryGetValue(commandId, out var stored))
            {
                return Task.FromResult<PersistedCreditAdjustmentCommand?>(null);
            }

            var movement = _accounts.Account!.Movements.Single(
                item => item.OperationId == commandId);
            var result = new CreditAdjustmentResult(
                commandId,
                movement.Type,
                movement.Amount,
                movement.BalanceAfter,
                movement.RecordedAt,
                movement.Description);

            return Task.FromResult<PersistedCreditAdjustmentCommand?>(
                new(stored.Command, result, stored.AcceptedAt));
        }

        public void Stage(
            CreditAdjustmentCommand command,
            DateTimeOffset acceptedAt)
        {
            _commands.Add(command.CommandId, (command, acceptedAt));
        }
    }

    private sealed class ImmediateTransaction : IApplicationTransaction
    {
        public Task<T> ExecuteAsync<T>(
            Func<CancellationToken, Task<T>> operation,
            CancellationToken cancellationToken = default) =>
            operation(cancellationToken);
    }

    private sealed class RecordingAuditTrail : IAuditTrail
    {
        public List<AuditEntry> Entries { get; } = [];

        public void Stage(AuditEntry entry) => Entries.Add(entry);

        public Task WriteAsync(
            AuditEntry entry,
            CancellationToken cancellationToken = default)
        {
            Entries.Add(entry);
            return Task.CompletedTask;
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
