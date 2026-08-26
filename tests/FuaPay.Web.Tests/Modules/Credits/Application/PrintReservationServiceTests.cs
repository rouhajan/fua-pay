using FuaPay.Web.BuildingBlocks.Application;
using FuaPay.Web.BuildingBlocks.Domain;
using FuaPay.Web.Modules.Credits.Application;
using FuaPay.Web.Modules.Credits.Domain;

namespace FuaPay.Web.Tests.Modules.Credits.Application;

public sealed class PrintReservationServiceTests
{
    private static readonly DateTimeOffset TestTime =
        new(2026, 8, 26, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Command_NormalizesJobUuid()
    {
        var jobId = Guid.NewGuid();

        var command = new ReservePrintCreditCommand(
            Guid.NewGuid(),
            Guid.NewGuid(),
            $"URN:UUID:{jobId:D}".ToUpperInvariant(),
            new Money(100),
            Guid.NewGuid());

        Assert.Equal($"urn:uuid:{jobId:D}", command.JobUuid);
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("source")]
    [InlineData("command")]
    public void Command_RejectsEmptyIdentifiers(string emptyIdentifier)
    {
        Assert.Throws<ArgumentException>(
            () => new ReservePrintCreditCommand(
                emptyIdentifier == "owner"
                    ? Guid.Empty
                    : Guid.NewGuid(),
                emptyIdentifier == "source"
                    ? Guid.Empty
                    : Guid.NewGuid(),
                $"urn:uuid:{Guid.NewGuid():D}",
                new Money(100),
                emptyIdentifier == "command"
                    ? Guid.Empty
                    : Guid.NewGuid()));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Command_RejectsNonPositiveAmount(long amountMinorUnits)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ReservePrintCreditCommand(
                Guid.NewGuid(),
                Guid.NewGuid(),
                $"urn:uuid:{Guid.NewGuid():D}",
                new Money(amountMinorUnits),
                Guid.NewGuid()));
    }

    [Fact]
    public async Task ReserveAsync_CreatesReservedSnapshotWithoutChangingBalance()
    {
        var calls = new List<string>();
        var ownerId = Guid.NewGuid();
        var account = CreateAccount(ownerId, balanceMinorUnits: 1_000);
        var accountRepository = new FakeCreditAccountRepository(
            account,
            calls);
        var reservationRepository =
            new FakePrintReservationRepository(calls);
        var transaction = new ImmediateTransaction();
        var service = CreateService(
            accountRepository,
            reservationRepository,
            transaction);
        var command = CreateCommand(ownerId, amountMinorUnits: 400);
        var balanceBefore = account.Balance;
        var movementCountBefore = account.Movements.Count;

        var result = await service.ReserveAsync(command);

        Assert.Equal(account.Id, result.CreditAccountId);
        Assert.Equal(command.PrintSourceId, result.PrintSourceId);
        Assert.Equal(command.JobUuid, result.JobUuid);
        Assert.Equal(command.Amount, result.Amount);
        Assert.Equal(PrintReservationStatus.Reserved, result.Status);
        Assert.Equal(command.ReserveCommandId, result.ReserveCommandId);
        Assert.Equal(TestTime, result.CreatedAt);
        Assert.Equal(TestTime, result.StateChangedAt);
        Assert.Equal(1, result.Version);
        Assert.Equal(balanceBefore, account.Balance);
        Assert.Equal(movementCountBefore, account.Movements.Count);
        Assert.Equal(
            [
                "account-lock",
                "command-read",
                "job-read",
                "blocking-sum",
                "reservation-add"
            ],
            calls);
        Assert.Equal(1, transaction.ExecutionCount);
    }

    [Fact]
    public async Task ReserveAsync_OnlyReservedAndResolutionRequiredBlockAvailable()
    {
        var ownerId = Guid.NewGuid();
        var account = CreateAccount(ownerId, balanceMinorUnits: 1_000);
        var repository = new FakePrintReservationRepository([]);
        repository.Reservations.Add(
            CreateResult(
                account.Id,
                amountMinorUnits: 200,
                PrintReservationStatus.Reserved));
        repository.Reservations.Add(
            CreateResult(
                account.Id,
                amountMinorUnits: 300,
                PrintReservationStatus.ResolutionRequired));
        repository.Reservations.Add(
            CreateResult(
                account.Id,
                amountMinorUnits: 5_000,
                PrintReservationStatus.Captured));
        repository.Reservations.Add(
            CreateResult(
                account.Id,
                amountMinorUnits: 5_000,
                PrintReservationStatus.Released));
        var service = CreateService(
            new FakeCreditAccountRepository(account, []),
            repository,
            new ImmediateTransaction());

        var result = await service.ReserveAsync(
            CreateCommand(ownerId, amountMinorUnits: 500));

        Assert.Equal(new Money(500), result.Amount);
        Assert.Equal(5, repository.Reservations.Count);
    }

    [Fact]
    public async Task ReserveAsync_RejectsInsufficientAvailableCredit()
    {
        var ownerId = Guid.NewGuid();
        var account = CreateAccount(ownerId, balanceMinorUnits: 1_000);
        var repository = new FakePrintReservationRepository([]);
        repository.Reservations.Add(
            CreateResult(
                account.Id,
                amountMinorUnits: 600,
                PrintReservationStatus.Reserved));
        repository.Reservations.Add(
            CreateResult(
                account.Id,
                amountMinorUnits: 250,
                PrintReservationStatus.ResolutionRequired));
        var service = CreateService(
            new FakeCreditAccountRepository(account, []),
            repository,
            new ImmediateTransaction());

        var exception =
            await Assert.ThrowsAsync<InsufficientAvailablePrintCreditException>(
                () => service.ReserveAsync(
                    CreateCommand(ownerId, amountMinorUnits: 151)));

        Assert.Equal(new Money(151), exception.Requested);
        Assert.Equal(new Money(150), exception.Available);
        Assert.Equal(2, repository.Reservations.Count);
        Assert.Equal(0, repository.AddCount);
    }

    [Fact]
    public async Task ReserveAsync_SameCommandAndPayloadReturnsPersistedSnapshot()
    {
        var ownerId = Guid.NewGuid();
        var account = CreateAccount(ownerId, balanceMinorUnits: 1_000);
        var jobId = Guid.NewGuid();
        var command = new ReservePrintCreditCommand(
            ownerId,
            Guid.NewGuid(),
            $"URN:UUID:{jobId:D}".ToUpperInvariant(),
            new Money(400),
            Guid.NewGuid());
        var repository = new FakePrintReservationRepository([]);
        var existing = CreateResult(
            account.Id,
            command,
            PrintReservationStatus.Captured,
            version: 4);
        repository.Reservations.Add(existing);
        var service = CreateService(
            new FakeCreditAccountRepository(account, []),
            repository,
            new ImmediateTransaction());

        var result = await service.ReserveAsync(command);

        Assert.Same(existing, result);
        Assert.Equal(PrintReservationStatus.Captured, result.Status);
        Assert.Equal(4, result.Version);
        Assert.Equal(0, repository.AddCount);
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("job")]
    [InlineData("amount")]
    public async Task ReserveAsync_SameCommandWithDifferentPayloadIsConflict(
        string payloadDifference)
    {
        var ownerId = Guid.NewGuid();
        var account = CreateAccount(ownerId, balanceMinorUnits: 1_000);
        var command = CreateCommand(ownerId, amountMinorUnits: 400);
        var repository = new FakePrintReservationRepository([]);
        var existing = CreateResult(
            account.Id,
            command,
            PrintReservationStatus.Reserved);
        repository.Reservations.Add(
            payloadDifference switch
            {
                "owner" => existing with
                {
                    CreditAccountId = Guid.NewGuid()
                },
                "job" => existing with
                {
                    JobUuid = $"urn:uuid:{Guid.NewGuid():D}"
                },
                "amount" => existing with
                {
                    Amount = new Money(399)
                },
                _ => throw new ArgumentOutOfRangeException(
                    nameof(payloadDifference))
            });
        var service = CreateService(
            new FakeCreditAccountRepository(account, []),
            repository,
            new ImmediateTransaction());

        await Assert.ThrowsAsync<PrintReservationCommandConflictException>(
            () => service.ReserveAsync(command));

        Assert.Equal(0, repository.AddCount);
    }

    [Fact]
    public async Task ReserveAsync_SamePrintJobWithDifferentCommandIsConflict()
    {
        var ownerId = Guid.NewGuid();
        var account = CreateAccount(ownerId, balanceMinorUnits: 1_000);
        var command = CreateCommand(ownerId, amountMinorUnits: 400);
        var repository = new FakePrintReservationRepository([]);
        repository.Reservations.Add(
            CreateResult(
                account.Id,
                command,
                PrintReservationStatus.Reserved) with
            {
                ReserveCommandId = Guid.NewGuid()
            });
        var service = CreateService(
            new FakeCreditAccountRepository(account, []),
            repository,
            new ImmediateTransaction());

        await Assert.ThrowsAsync<PrintReservationJobConflictException>(
            () => service.ReserveAsync(command));

        Assert.Equal(0, repository.AddCount);
    }

    [Fact]
    public async Task ReserveAsync_UniqueRaceReloadsWinningCommand()
    {
        var ownerId = Guid.NewGuid();
        var account = CreateAccount(ownerId, balanceMinorUnits: 1_000);
        var command = CreateCommand(ownerId, amountMinorUnits: 400);
        var repository = new FakePrintReservationRepository([]);
        var winning = CreateResult(
            account.Id,
            command,
            PrintReservationStatus.Reserved);
        repository.BeforeAdd = _ => repository.Reservations.Add(winning);
        repository.AddException =
            new PrintReservationReserveCommandAlreadyExistsException(
                command.PrintSourceId,
                command.ReserveCommandId,
                new InvalidOperationException("unique violation"));
        var transaction = new ImmediateTransaction();
        var service = CreateService(
            new FakeCreditAccountRepository(account, []),
            repository,
            transaction);

        var result = await service.ReserveAsync(command);

        Assert.Same(winning, result);
        Assert.Equal(1, repository.AddCount);
        Assert.Equal(2, transaction.ExecutionCount);
    }

    [Fact]
    public async Task ReserveAsync_MissingAccountIsRejectedBeforeReservationAccess()
    {
        var calls = new List<string>();
        var ownerId = Guid.NewGuid();
        var repository = new FakePrintReservationRepository(calls);
        var service = CreateService(
            new FakeCreditAccountRepository(account: null, calls),
            repository,
            new ImmediateTransaction());

        await Assert.ThrowsAsync<CreditAccountNotFoundException>(
            () => service.ReserveAsync(CreateCommand(ownerId)));

        Assert.Equal(["account-lock"], calls);
        Assert.Equal(0, repository.AddCount);
    }

    private static PrintReservationService CreateService(
        ICreditAccountRepository accountRepository,
        IPrintReservationRepository reservationRepository,
        IApplicationTransaction transaction)
    {
        return new PrintReservationService(
            accountRepository,
            reservationRepository,
            transaction,
            new FixedTimeProvider(TestTime));
    }

    private static CreditAccount CreateAccount(
        Guid ownerId,
        long balanceMinorUnits)
    {
        var account = new CreditAccount(Guid.NewGuid(), ownerId);
        account.Credit(
            Guid.NewGuid(),
            new Money(balanceMinorUnits),
            TestTime.AddMinutes(-1),
            "Test balance");
        return account;
    }

    private static ReservePrintCreditCommand CreateCommand(
        Guid ownerId,
        long amountMinorUnits = 100)
    {
        return new ReservePrintCreditCommand(
            ownerId,
            Guid.NewGuid(),
            $"urn:uuid:{Guid.NewGuid():D}",
            new Money(amountMinorUnits),
            Guid.NewGuid());
    }

    private static PrintReservationResult CreateResult(
        Guid accountId,
        long amountMinorUnits,
        PrintReservationStatus status)
    {
        return new PrintReservationResult(
            Guid.NewGuid(),
            accountId,
            Guid.NewGuid(),
            $"urn:uuid:{Guid.NewGuid():D}",
            new Money(amountMinorUnits),
            status,
            Guid.NewGuid(),
            status == PrintReservationStatus.ResolutionRequired
                ? Guid.NewGuid()
                : null,
            status is PrintReservationStatus.Captured or
                PrintReservationStatus.Released
                ? Guid.NewGuid()
                : null,
            status == PrintReservationStatus.Captured
                ? Guid.NewGuid()
                : null,
            TestTime,
            TestTime,
            1);
    }

    private static PrintReservationResult CreateResult(
        Guid accountId,
        ReservePrintCreditCommand command,
        PrintReservationStatus status,
        long version = 1)
    {
        return new PrintReservationResult(
            Guid.NewGuid(),
            accountId,
            command.PrintSourceId,
            command.JobUuid,
            command.Amount,
            status,
            command.ReserveCommandId,
            status == PrintReservationStatus.ResolutionRequired
                ? Guid.NewGuid()
                : null,
            status is PrintReservationStatus.Captured or
                PrintReservationStatus.Released
                ? Guid.NewGuid()
                : null,
            status == PrintReservationStatus.Captured
                ? Guid.NewGuid()
                : null,
            TestTime,
            TestTime,
            version);
    }

    private sealed class FakeCreditAccountRepository :
        ICreditAccountRepository
    {
        private readonly CreditAccount? _account;
        private readonly List<string> _calls;

        public FakeCreditAccountRepository(
            CreditAccount? account,
            List<string> calls)
        {
            _account = account;
            _calls = calls;
        }

        public Task<CreditAccount?> FindByOwnerIdForUpdateAsync(
            Guid ownerId,
            CancellationToken cancellationToken)
        {
            _calls.Add("account-lock");
            return Task.FromResult(
                _account?.OwnerId == ownerId ? _account : null);
        }

        public Task<CreditAccount?> FindByOwnerIdAsync(
            Guid ownerId,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                _account?.OwnerId == ownerId ? _account : null);

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

    private sealed class FakePrintReservationRepository :
        IPrintReservationRepository
    {
        private readonly List<string> _calls;

        public FakePrintReservationRepository(List<string> calls)
        {
            _calls = calls;
        }

        public List<PrintReservationResult> Reservations { get; } = [];

        public int AddCount { get; private set; }

        public Action<PrintReservation>? BeforeAdd { get; set; }

        public Exception? AddException { get; set; }

        public Task<PrintReservationResult?> FindByReserveCommandAsync(
            Guid printSourceId,
            Guid reserveCommandId,
            CancellationToken cancellationToken)
        {
            _calls.Add("command-read");
            return Task.FromResult(
                Reservations.SingleOrDefault(
                    reservation =>
                        reservation.PrintSourceId == printSourceId &&
                        reservation.ReserveCommandId == reserveCommandId));
        }

        public Task<PrintReservationResult?> FindByPrintJobAsync(
            Guid printSourceId,
            string jobUuid,
            CancellationToken cancellationToken)
        {
            _calls.Add("job-read");
            return Task.FromResult(
                Reservations.SingleOrDefault(
                    reservation =>
                        reservation.PrintSourceId == printSourceId &&
                        reservation.JobUuid == jobUuid));
        }

        public Task<Money> GetBlockingAmountAsync(
            Guid creditAccountId,
            CancellationToken cancellationToken)
        {
            _calls.Add("blocking-sum");
            return Task.FromResult(
                new Money(
                    Reservations
                        .Where(
                            reservation =>
                                reservation.CreditAccountId ==
                                    creditAccountId &&
                                reservation.Status is
                                    PrintReservationStatus.Reserved or
                                    PrintReservationStatus.ResolutionRequired)
                        .Sum(reservation => reservation.Amount.MinorUnits)));
        }

        public Task AddAsync(
            PrintReservation reservation,
            CancellationToken cancellationToken)
        {
            _calls.Add("reservation-add");
            AddCount++;
            BeforeAdd?.Invoke(reservation);

            if (AddException is not null)
            {
                throw AddException;
            }

            Reservations.Add(
                new PrintReservationResult(
                    reservation.Id,
                    reservation.CreditAccountId,
                    reservation.PrintSourceId,
                    reservation.JobUuid,
                    reservation.Amount,
                    reservation.Status,
                    reservation.ReserveCommandId,
                    reservation.ResolutionCommandId,
                    reservation.TerminalCommandId,
                    reservation.DebitOperationId,
                    reservation.CreatedAt,
                    reservation.StateChangedAt,
                    1));

            return Task.CompletedTask;
        }
    }

    private sealed class ImmediateTransaction : IApplicationTransaction
    {
        public int ExecutionCount { get; private set; }

        public async Task<T> ExecuteAsync<T>(
            Func<CancellationToken, Task<T>> operation,
            CancellationToken cancellationToken = default)
        {
            ExecutionCount++;
            return await operation(cancellationToken);
        }
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
