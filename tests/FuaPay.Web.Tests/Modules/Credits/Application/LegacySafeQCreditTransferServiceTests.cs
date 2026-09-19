using FuaPay.Web.BuildingBlocks.Application;
using FuaPay.Web.BuildingBlocks.Auditing;
using FuaPay.Web.BuildingBlocks.Domain;
using FuaPay.Web.Modules.Access.Application;
using FuaPay.Web.Modules.Access.Domain;
using FuaPay.Web.Modules.Credits.Application;
using FuaPay.Web.Modules.Credits.Domain;

namespace FuaPay.Web.Tests.Modules.Credits.Application;

public sealed class LegacySafeQCreditTransferServiceTests
{
    private const string SnapshotHash =
        "5305EEFCFE4B2D6DAF86DF11897626B5F2819FE422F33463D9AA53DD5072F6FA";

    private static readonly DateTimeOffset CurrentTime =
        new(2026, 9, 19, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task TransferAsync_PositiveAmountCreatesCanonicalCreditMovementAndAudit()
    {
        var fixture = new Fixture();
        var command = fixture.CreateCommand(new Money(2_500));

        var result = await fixture.Service.TransferAsync(command);

        var movement = Assert.Single(fixture.Accounts.Account!.Movements);
        Assert.Equal(command.CommandId, movement.OperationId);
        Assert.Equal(CreditMovementType.Credit, movement.Type);
        Assert.Equal(new Money(2_500), movement.Amount);
        Assert.Equal(new Money(2_500), movement.BalanceAfter);
        Assert.Equal(
            LegacySafeQCreditTransferService.CustomerDescription,
            movement.Description);
        Assert.Equal("Převod kreditu ze SafeQ", movement.Description);

        Assert.Equal(command.CommandId, result.CommandId);
        Assert.Equal(CreditMovementType.Credit, result.MovementType);
        Assert.Equal(command.Amount, result.Amount);
        Assert.Equal(movement.BalanceAfter, result.BalanceAfter);
        Assert.Equal(movement.RecordedAt, result.RecordedAt);
        Assert.Equal(
            LegacySafeQCreditTransferService.CustomerDescription,
            result.Description);

        Assert.DoesNotContain(command.SafeQUserId, result.Description);
        Assert.DoesNotContain(command.SnapshotSha256, result.Description);
        Assert.DoesNotContain(
            command.AdministratorUserId.ToString(),
            result.Description);
        Assert.DoesNotContain(
            command.CommandId.ToString(),
            result.Description);

        var persisted = Assert.IsType<PersistedLegacySafeQCreditTransfer>(
            await fixture.Commands.FindByCommandIdAsync(command.CommandId));
        Assert.Equal(command, persisted.Command);
        Assert.Equal(CurrentTime, persisted.AcceptedAt);

        var audit = Assert.Single(fixture.Audit.Entries);
        Assert.Equal("credit.legacy-safeq-transfer", audit.Action);
        Assert.Equal(fixture.AdministratorId, audit.ActorUserId);
        Assert.Equal(fixture.OwnerId.ToString(), audit.EntityId);
        Assert.Contains(command.SafeQUserId, audit.Description);
        Assert.Contains(command.SnapshotSha256, audit.Description);
        Assert.Contains(
            command.Amount.MinorUnits.ToString(),
            audit.Description);
    }

    [Fact]
    public async Task TransferAsync_SameCommandAndPayloadReturnsOriginalResult()
    {
        var fixture = new Fixture();
        var command = fixture.CreateCommand(new Money(2_500));

        var first = await fixture.Service.TransferAsync(command);
        var replay = await fixture.Service.TransferAsync(command);

        Assert.Equal(first, replay);
        Assert.Equal(1, fixture.Accounts.SaveCalls);
        Assert.Single(fixture.Audit.Entries);
        Assert.Single(fixture.Accounts.Account!.Movements);
    }

    [Fact]
    public async Task TransferAsync_SameCommandWithDifferentPayloadConflicts()
    {
        var fixture = new Fixture();
        var command = fixture.CreateCommand(new Money(2_500));
        await fixture.Service.TransferAsync(command);

        var conflicting = fixture.CreateCommand(
            new Money(2_501),
            commandId: command.CommandId);

        await Assert.ThrowsAsync<
            LegacySafeQCreditTransferCommandConflictException>(
            () => fixture.Service.TransferAsync(conflicting));

        Assert.Equal(1, fixture.Accounts.SaveCalls);
        Assert.Single(fixture.Audit.Entries);
        Assert.Single(fixture.Accounts.Account!.Movements);
    }

    [Fact]
    public async Task TransferAsync_DifferentCommandForSameSafeQUserIdIsRejected()
    {
        var fixture = new Fixture();
        var first = fixture.CreateCommand(
            new Money(2_500),
            safeQUserId: "1000000000100123");

        await fixture.Service.TransferAsync(first);

        var duplicateSource = fixture.CreateCommand(
            new Money(2_500),
            safeQUserId: first.SafeQUserId,
            snapshotSha256:
                "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA");

        var exception = await Assert.ThrowsAsync<
            LegacySafeQCreditAlreadyTransferredException>(
            () => fixture.Service.TransferAsync(duplicateSource));

        Assert.Equal(first.SafeQUserId, exception.SafeQUserId);
        Assert.Equal(1, fixture.Accounts.SaveCalls);
        Assert.Single(fixture.Audit.Entries);
        Assert.Single(fixture.Accounts.Account!.Movements);
    }

    [Fact]
    public async Task TransferAsync_InactiveCustomerIsRejectedWithoutFinancialEffect()
    {
        var fixture = new Fixture
        {
            CustomerIsEligible = false
        };
        var command = fixture.CreateCommand(new Money(2_500));

        await Assert.ThrowsAsync<
            LegacySafeQCreditTransferOwnerNotEligibleException>(
            () => fixture.Service.TransferAsync(command));

        Assert.Equal(0, fixture.Accounts.SaveCalls);
        Assert.Empty(fixture.Audit.Entries);
        Assert.Empty(fixture.Accounts.Account!.Movements);
        Assert.Null(
            await fixture.Commands.FindByCommandIdAsync(
                command.CommandId));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Command_ZeroOrNegativeAmountIsRejected(long minorUnits)
    {
        var fixture = new Fixture();

        Assert.Throws<
            LegacySafeQCreditTransferAmountNotAllowedException>(
            () => fixture.CreateCommand(new Money(minorUnits)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-sha256")]
    [InlineData("GGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGG")]
    public void Command_InvalidSnapshotHashIsRejected(string snapshotSha256)
    {
        var fixture = new Fixture();

        Assert.Throws<LegacySafeQSnapshotHashNotAllowedException>(
            () => fixture.CreateCommand(
                new Money(2_500),
                snapshotSha256: snapshotSha256));
    }

    [Fact]
    public void Command_NormalizesSafeQUserIdAndSnapshotHash()
    {
        var fixture = new Fixture();

        var command = fixture.CreateCommand(
            new Money(2_500),
            safeQUserId: " 1000000000100123 ",
            snapshotSha256: SnapshotHash.ToLowerInvariant());

        Assert.Equal("1000000000100123", command.SafeQUserId);
        Assert.Equal(SnapshotHash, command.SnapshotSha256);
    }

    private sealed class Fixture
    {
        private readonly FakeAccessUserQueries _accessUsers = new();

        public Fixture()
        {
            OwnerId = Guid.NewGuid();
            AdministratorId = Guid.NewGuid();
            Accounts.Account = new CreditAccount(Guid.NewGuid(), OwnerId);
            Commands = new FakeTransferRepository(Accounts);
            var transaction = new ImmediateTransaction();

            Service = new LegacySafeQCreditTransferService(
                new CreditService(
                    Accounts,
                    new CreditAvailabilityService(
                        new NoBlockingPrintReservationRepository()),
                    transaction,
                    new FixedTimeProvider(CurrentTime)),
                Commands,
                transaction,
                Audit,
                _accessUsers,
                new FixedTimeProvider(CurrentTime));
        }

        public Guid OwnerId { get; }

        public Guid AdministratorId { get; }

        public FakeCreditAccountRepository Accounts { get; } = new();

        public FakeTransferRepository Commands { get; }

        public RecordingAuditTrail Audit { get; } = new();

        public LegacySafeQCreditTransferService Service { get; }

        public bool CustomerIsEligible
        {
            get => _accessUsers.CustomerIsEligible;
            init => _accessUsers.CustomerIsEligible = value;
        }

        public LegacySafeQCreditTransferCommand CreateCommand(
            Money amount,
            Guid? commandId = null,
            string safeQUserId = "1000000000100123",
            string snapshotSha256 = SnapshotHash) =>
            new(
                commandId ?? Guid.NewGuid(),
                AdministratorId,
                OwnerId,
                safeQUserId,
                snapshotSha256,
                amount);
    }

    private sealed class FakeTransferRepository :
        ILegacySafeQCreditTransferRepository
    {
        private readonly FakeCreditAccountRepository _accounts;
        private readonly Dictionary<
            Guid,
            (LegacySafeQCreditTransferCommand Command, DateTimeOffset AcceptedAt)>
            _commands = [];

        public FakeTransferRepository(
            FakeCreditAccountRepository accounts)
        {
            _accounts = accounts;
        }

        public Task<PersistedLegacySafeQCreditTransfer?>
            FindByCommandIdAsync(
                Guid commandId,
                CancellationToken cancellationToken = default)
        {
            if (!_commands.TryGetValue(commandId, out var stored))
            {
                return Task.FromResult<
                    PersistedLegacySafeQCreditTransfer?>(null);
            }

            return Task.FromResult<
                PersistedLegacySafeQCreditTransfer?>(
                    Restore(stored));
        }

        public Task<PersistedLegacySafeQCreditTransfer?>
            FindBySafeQUserIdAsync(
                string safeQUserId,
                CancellationToken cancellationToken = default)
        {
            var stored = _commands.Values.SingleOrDefault(
                item =>
                    string.Equals(
                        item.Command.SafeQUserId,
                        safeQUserId,
                        StringComparison.Ordinal));

            return Task.FromResult<
                PersistedLegacySafeQCreditTransfer?>(
                    stored.Command is null
                        ? null
                        : Restore(stored));
        }

        public void Stage(
            LegacySafeQCreditTransferCommand command,
            DateTimeOffset acceptedAt)
        {
            _commands.Add(
                command.CommandId,
                (command, acceptedAt));
        }

        private PersistedLegacySafeQCreditTransfer Restore(
            (
                LegacySafeQCreditTransferCommand Command,
                DateTimeOffset AcceptedAt
            ) stored)
        {
            var movement = _accounts.Account!.Movements.Single(
                item =>
                    item.OperationId ==
                    stored.Command.CommandId);

            var result = new LegacySafeQCreditTransferResult(
                stored.Command.CommandId,
                movement.Type,
                movement.Amount,
                movement.BalanceAfter,
                movement.RecordedAt,
                movement.Description);

            return new PersistedLegacySafeQCreditTransfer(
                stored.Command,
                result,
                stored.AcceptedAt);
        }
    }

    private sealed class FakeCreditAccountRepository :
        ICreditAccountRepository
    {
        public CreditAccount? Account { get; set; }

        public int SaveCalls { get; private set; }

        public Task<CreditAccount?> FindByOwnerIdAsync(
            Guid ownerId,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                Account?.OwnerId == ownerId
                    ? Account
                    : null);

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

    private sealed class FakeAccessUserQueries : IAccessUserQueries
    {
        public bool CustomerIsEligible { get; set; } = true;

        public Task<bool> IsActiveCustomerAsync(
            Guid userId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(CustomerIsEligible);

        public Task<AccessUserPage> ListAsync(
            AccessUserListRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AccessUserDetail?> FindDetailAsync(
            Guid userId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<AccessUserOption>>
            ListActiveCustomersAsync(
                CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyDictionary<Guid, AccessUserOption>>
            FindOptionsAsync(
                IEnumerable<Guid> userIds,
                CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> IsActiveAsync(
            Guid userId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<long> CountActiveUsersWithRoleAsync(
            AccessRole role,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
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

    private sealed class NoBlockingPrintReservationRepository :
        ICreditAvailabilityRepository
    {
        public Task<PrintReservationResult?> FindByReserveCommandAsync(
            Guid printSourceId,
            Guid reserveCommandId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<PrintReservationResult?> FindByPrintJobAsync(
            Guid printSourceId,
            string jobUuid,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Money> GetTotalBlockingAmountAsync(
            Guid creditAccountId,
            CancellationToken cancellationToken) =>
            Task.FromResult(Money.Zero);

        public Task AddAsync(
            PrintReservation reservation,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
