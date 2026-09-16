using FuaPay.Web.BuildingBlocks.Application;
using FuaPay.Web.BuildingBlocks.Auditing;
using FuaPay.Web.Modules.Credits.Application;
using FuaPay.Web.Modules.Credits.Domain;
using FuaPay.Web.Modules.FinancialDocuments.Application;
using FuaPay.Web.Modules.FinancialDocuments.Domain;

namespace FuaPay.Web.Tests.Modules.Credits.Application;

public sealed class ManualCreditTopUpServiceTests
{
    private static readonly DateTimeOffset CurrentTime =
        new(2026, 9, 13, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task TopUpAsync_PositiveAmountCreatesOneCanonicalCreditMovementAndAudit()
    {
        var fixture = new Fixture();
        var command = fixture.CreateCommand(
            new Money(2_500),
            note: "  Přijatá hotovost  ");

        var result = await fixture.Service.TopUpAsync(
            command,
            fixture.Customer);

        var movement = Assert.Single(fixture.Accounts.Account!.Movements);
        Assert.Equal(command.CommandId, movement.OperationId);
        Assert.Equal(CreditMovementType.Credit, movement.Type);
        Assert.Equal(new Money(2_500), movement.Amount);
        Assert.Equal(new Money(2_500), movement.BalanceAfter);
        Assert.Equal(movement.BalanceAfter, fixture.Accounts.Account.Balance);
        Assert.Equal(CreditMovementType.Credit, result.MovementType);
        Assert.Equal("Ruční dobití kreditu", result.Description);
        Assert.DoesNotContain(command.Note, result.Description);
        Assert.DoesNotContain(
            command.AdministratorUserId.ToString(),
            result.Description);
        Assert.DoesNotContain(command.CommandId.ToString(), result.Description);
        Assert.DoesNotContain("Administrativní korekce", result.Description);

        var persisted = Assert.IsType<PersistedManualCreditTopUpCommand>(
            await fixture.Commands.FindAsync(command.CommandId));
        Assert.Equal(command.CommandId, persisted.Command.CommandId);
        Assert.Equal(fixture.AdministratorId, persisted.Command.AdministratorUserId);
        Assert.Equal(fixture.OwnerId, persisted.Command.OwnerId);
        Assert.Equal(new Money(2_500), persisted.Command.Amount);
        Assert.Equal("Přijatá hotovost", persisted.Command.Note);
        Assert.Equal(CurrentTime, persisted.AcceptedAt);
        Assert.True(persisted.FinancialDocumentRequired);

        var audit = Assert.Single(fixture.Audit.Entries);
        Assert.Equal("credit.manual-topup", audit.Action);
        Assert.Equal(fixture.AdministratorId, audit.ActorUserId);
        Assert.Equal(fixture.OwnerId.ToString(), audit.EntityId);
        Assert.Contains(command.Note, audit.Description);

        var document = Assert.IsType<FinancialDocument>(
            await fixture.Documents.FindBySourceAsync(
                FinancialDocumentSourceType.ManualCreditTopUp,
                command.CommandId));
        Assert.Equal(fixture.Customer, document.Customer);
        Assert.Equal(command.Amount.MinorUnits, document.AmountMinorUnits);
        Assert.Equal(CurrentTime, document.FinancialEventAt);
        Assert.Equal(CurrentTime, document.IssuedAt);
        Assert.Null(document.Issuer);
        Assert.Null(document.Provider);
        Assert.Null(document.Job);
    }

    [Fact]
    public async Task TopUpAsync_SameCommandAndPayloadReturnsOriginalResult()
    {
        var fixture = new Fixture();
        var command = fixture.CreateCommand(new Money(2_500));

        var first = await fixture.Service.TopUpAsync(
            command,
            fixture.Customer);
        var replay = await fixture.Service.TopUpAsync(
            command,
            new FinancialDocumentCustomerSnapshot(
                fixture.OwnerId,
                "Changed customer name",
                "changed@example.test"));

        Assert.Equal(first, replay);
        Assert.Equal(1, fixture.Accounts.SaveCalls);
        Assert.Single(fixture.Audit.Entries);
        Assert.Single(fixture.Accounts.Account!.Movements);
        Assert.Equal(1, fixture.DocumentNumbers.AllocationCount);
        var document = Assert.IsType<FinancialDocument>(
            await fixture.Documents.FindBySourceAsync(
                FinancialDocumentSourceType.ManualCreditTopUp,
                command.CommandId));
        Assert.Equal(fixture.Customer, document.Customer);
    }

    [Fact]
    public async Task TopUpAsync_SameCommandWithDifferentPayloadConflicts()
    {
        var fixture = new Fixture();
        var command = fixture.CreateCommand(new Money(2_500));
        await fixture.Service.TopUpAsync(command, fixture.Customer);

        var conflicting = fixture.CreateCommand(
            new Money(2_501),
            command.CommandId);

        await Assert.ThrowsAsync<ManualCreditTopUpCommandConflictException>(
            () => fixture.Service.TopUpAsync(
                conflicting,
                fixture.Customer));
        Assert.Equal(1, fixture.Accounts.SaveCalls);
        Assert.Single(fixture.Audit.Entries);
        Assert.Single(fixture.Accounts.Account!.Movements);
    }

    [Fact]
    public async Task TopUpAsync_CustomerSnapshotForDifferentOwnerIsRejected()
    {
        var fixture = new Fixture();
        var command = fixture.CreateCommand(new Money(2_500));

        await Assert.ThrowsAsync<ArgumentException>(
            () => fixture.Service.TopUpAsync(
                command,
                new FinancialDocumentCustomerSnapshot(
                    Guid.NewGuid(),
                    "Wrong customer",
                    null)));

        Assert.Equal(0, fixture.Accounts.SaveCalls);
        Assert.Empty(fixture.Audit.Entries);
        Assert.Equal(0, fixture.DocumentNumbers.AllocationCount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Command_ZeroOrNegativeAmountIsRejected(long minorUnits)
    {
        var fixture = new Fixture();

        Assert.Throws<ManualCreditTopUpAmountNotAllowedException>(
            () => fixture.CreateCommand(new Money(minorUnits)));
    }

    private sealed class Fixture
    {
        public Fixture()
        {
            OwnerId = Guid.NewGuid();
            AdministratorId = Guid.NewGuid();
            Customer = new FinancialDocumentCustomerSnapshot(
                OwnerId,
                "Test customer",
                "customer@example.test");
            Accounts.Account = new CreditAccount(Guid.NewGuid(), OwnerId);
            Commands = new FakeCommandRepository(Accounts);
            var transaction = new ImmediateTransaction();
            Service = new ManualCreditTopUpService(
                new CreditService(
                    Accounts,
                    new CreditAvailabilityService(
                        new NoBlockingPrintReservationRepository()),
                    transaction,
                    new FixedTimeProvider(CurrentTime)),
                Commands,
                transaction,
                Audit,
                Documents,
                DocumentNumbers,
                new FixedTimeProvider(CurrentTime));
        }

        public Guid OwnerId { get; }

        public Guid AdministratorId { get; }

        public FinancialDocumentCustomerSnapshot Customer { get; }

        public FakeCreditAccountRepository Accounts { get; } = new();

        public FakeCommandRepository Commands { get; }

        public RecordingAuditTrail Audit { get; } = new();

        public FakeFinancialDocumentRepository Documents { get; } = new();

        public FakeFinancialDocumentNumberAllocator DocumentNumbers { get; } = new();

        public ManualCreditTopUpService Service { get; }

        public ManualCreditTopUpCommand CreateCommand(
            Money amount,
            Guid? commandId = null,
            string note = "Ruční vklad") =>
            new(
                commandId ?? Guid.NewGuid(),
                AdministratorId,
                OwnerId,
                amount,
                note);
    }

    private sealed class FakeFinancialDocumentRepository :
        IFinancialDocumentRepository
    {
        private readonly Dictionary<
            (FinancialDocumentSourceType SourceType, Guid SourceId),
            FinancialDocument> _documents = [];
        private FinancialDocument? _staged;

        public Task<FinancialDocument?> FindBySourceAsync(
            FinancialDocumentSourceType sourceType,
            Guid sourceId,
            CancellationToken cancellationToken = default)
        {
            _documents.TryGetValue((sourceType, sourceId), out var document);
            return Task.FromResult(document);
        }

        public void Stage(FinancialDocument document)
        {
            Assert.Null(_staged);
            _staged = document;
        }

        public Task PersistStagedAsync(
            FinancialDocument document,
            CancellationToken cancellationToken = default)
        {
            Assert.Same(document, _staged);
            _documents.Add(
                (document.SourceType, document.SourceId),
                document);
            _staged = null;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeFinancialDocumentNumberAllocator :
        IFinancialDocumentNumberAllocator
    {
        public int AllocationCount { get; private set; }

        public Task<FinancialDocumentNumberAllocation> AllocateAsync(
            DateTimeOffset issuedAt,
            CancellationToken cancellationToken = default)
        {
            AllocationCount++;
            return Task.FromResult(
                new FinancialDocumentNumberAllocation(
                    $"FUA-{issuedAt.Year:D4}-{AllocationCount:D6}",
                    issuedAt.Year,
                    AllocationCount));
        }
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

    private sealed class FakeCommandRepository :
        IManualCreditTopUpCommandRepository
    {
        private readonly FakeCreditAccountRepository _accounts;
        private readonly Dictionary<Guid, (ManualCreditTopUpCommand Command, DateTimeOffset AcceptedAt)>
            _commands = [];

        public FakeCommandRepository(FakeCreditAccountRepository accounts)
        {
            _accounts = accounts;
        }

        public Task<PersistedManualCreditTopUpCommand?> FindAsync(
            Guid commandId,
            CancellationToken cancellationToken = default)
        {
            if (!_commands.TryGetValue(commandId, out var stored))
            {
                return Task.FromResult<PersistedManualCreditTopUpCommand?>(null);
            }

            var movement = _accounts.Account!.Movements.Single(
                item => item.OperationId == commandId);
            var result = new ManualCreditTopUpResult(
                commandId,
                movement.Type,
                movement.Amount,
                movement.BalanceAfter,
                movement.RecordedAt,
                movement.Description);

            return Task.FromResult<PersistedManualCreditTopUpCommand?>(
                new(
                    stored.Command,
                    result,
                    stored.AcceptedAt,
                    FinancialDocumentRequired: true));
        }

        public void Stage(
            ManualCreditTopUpCommand command,
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

        public Task<T> ExecuteTopLevelAsync<T>(
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
