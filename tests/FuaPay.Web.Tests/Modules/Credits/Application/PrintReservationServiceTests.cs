using FuaPay.Web.BuildingBlocks.Application;
using FuaPay.Web.BuildingBlocks.Auditing;
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
        var audit = new RecordingAuditTrail();
        var service = CreateService(
            accountRepository,
            reservationRepository,
            transaction,
            audit);
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
                "reservation-add",
                "reservation-read"
            ],
            calls);
        Assert.Equal(1, transaction.ExecutionCount);
        var auditEntry = Assert.Single(audit.Entries);
        Assert.Equal("fua-print-payments", auditEntry.ActorProcessName);
        Assert.Equal("print-reservation.reserved", auditEntry.Action);
        Assert.Equal("print-reservation", auditEntry.EntityType);
        Assert.Equal(result.Id.ToString(), auditEntry.EntityId);
        Assert.Contains(result.Id.ToString(), auditEntry.Description);
        Assert.Contains(command.JobUuid, auditEntry.Description);
        Assert.Contains("400", auditEntry.Description);
        Assert.Contains("Reserved", auditEntry.Description);
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
        var audit = new RecordingAuditTrail();
        var service = CreateService(
            new FakeCreditAccountRepository(account, []),
            repository,
            new ImmediateTransaction(),
            audit);

        var result = await service.ReserveAsync(command);

        Assert.Same(existing, result);
        Assert.Equal(PrintReservationStatus.Captured, result.Status);
        Assert.Equal(4, result.Version);
        Assert.Equal(0, repository.AddCount);
        Assert.Empty(audit.Entries);
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
    public async Task FindByPrintJobAsync_IsNormalizedAndSourceScoped()
    {
        var ownerId = Guid.NewGuid();
        var sourceId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var calls = new List<string>();
        var account = CreateAccount(ownerId, balanceMinorUnits: 1_000);
        var repository = new FakePrintReservationRepository(calls);
        var expected = CreateResult(
            account.Id,
            new ReservePrintCreditCommand(
                ownerId,
                sourceId,
                $"urn:uuid:{jobId:D}",
                new Money(400),
                Guid.NewGuid()),
            PrintReservationStatus.Reserved);
        repository.Reservations.Add(expected);
        repository.Reservations.Add(
            expected with
            {
                Id = Guid.NewGuid(),
                PrintSourceId = Guid.NewGuid()
            });
        var service = CreateService(
            new FakeCreditAccountRepository(account, calls),
            repository,
            new ImmediateTransaction());

        var result = await service.FindByPrintJobAsync(
            sourceId,
            $"URN:UUID:{jobId:D}".ToUpperInvariant());

        Assert.Same(expected, result);
        Assert.Equal(["job-read"], calls);
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

    [Theory]
    [InlineData("resolution", "reservation")]
    [InlineData("capture", "source")]
    [InlineData("release", "command")]
    public void LifecycleCommand_RejectsEmptyIdentifiers(
        string operation,
        string emptyIdentifier)
    {
        var reservationId = emptyIdentifier == "reservation"
            ? Guid.Empty
            : Guid.NewGuid();
        var printSourceId = emptyIdentifier == "source"
            ? Guid.Empty
            : Guid.NewGuid();
        var commandId = emptyIdentifier == "command"
            ? Guid.Empty
            : Guid.NewGuid();

        Assert.Throws<ArgumentException>(
            () =>
            {
                object lifecycleCommand = operation switch
                {
                    "resolution" =>
                        new RequirePrintReservationResolutionCommand(
                            reservationId,
                            printSourceId,
                            commandId),
                    "capture" => new CapturePrintReservationCommand(
                        reservationId,
                        printSourceId,
                        commandId),
                    "release" => new ReleasePrintReservationCommand(
                        reservationId,
                        printSourceId,
                        commandId),
                    _ => throw new ArgumentOutOfRangeException(
                        nameof(operation))
                };
                _ = lifecycleCommand;
            });
    }

    [Fact]
    public async Task RequireResolutionAsync_IsBlockingAuditedAndIdempotent()
    {
        var calls = new List<string>();
        var ownerId = Guid.NewGuid();
        var account = CreateAccount(ownerId, balanceMinorUnits: 1_000);
        var repository = new FakePrintReservationRepository(calls);
        var reserved = CreateResult(
            account.Id,
            amountMinorUnits: 400,
            PrintReservationStatus.Reserved);
        repository.Reservations.Add(reserved);
        var audit = new RecordingAuditTrail();
        var service = CreateService(
            new FakeCreditAccountRepository(account, calls),
            repository,
            new ImmediateTransaction(),
            audit);
        var command =
            new RequirePrintReservationResolutionCommand(
                reserved.Id,
                reserved.PrintSourceId,
                Guid.NewGuid());

        var changed = await service.RequireResolutionAsync(command);
        var replayed = await service.RequireResolutionAsync(command);

        Assert.Equal(changed, replayed);
        Assert.Equal(
            PrintReservationStatus.ResolutionRequired,
            changed.Status);
        Assert.Equal(
            command.ResolutionCommandId,
            changed.ResolutionCommandId);
        Assert.Equal(new Money(400), await repository.GetBlockingAmountAsync(
            account.Id,
            CancellationToken.None));
        Assert.Equal(1, repository.SaveCount);
        var auditEntry = Assert.Single(audit.Entries);
        Assert.Equal(
            "fua-print-payments",
            auditEntry.ActorProcessName);
        Assert.Equal(
            "print-reservation.resolution-required",
            auditEntry.Action);
        Assert.True(
            calls.IndexOf("account-lock-by-id") <
            calls.IndexOf("reservation-lock"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CaptureAsync_DebitsOwnReservationExactlyOnce(
        bool resolutionRequired)
    {
        var ownerId = Guid.NewGuid();
        var account = CreateAccount(ownerId, balanceMinorUnits: 1_000);
        var repository = new FakePrintReservationRepository([]);
        var reservation = CreateResult(
            account.Id,
            amountMinorUnits: 700,
            resolutionRequired
                ? PrintReservationStatus.ResolutionRequired
                : PrintReservationStatus.Reserved);
        repository.Reservations.Add(reservation);
        repository.Reservations.Add(
            CreateResult(
                account.Id,
                amountMinorUnits: 300,
                PrintReservationStatus.Reserved));
        var audit = new RecordingAuditTrail();
        var service = CreateService(
            new FakeCreditAccountRepository(account, []),
            repository,
            new ImmediateTransaction(),
            audit);
        var command = new CapturePrintReservationCommand(
            reservation.Id,
            reservation.PrintSourceId,
            Guid.NewGuid());

        var captured = await service.CaptureAsync(command);
        var replayed = await service.CaptureAsync(command);

        Assert.Equal(captured, replayed);
        Assert.Equal(PrintReservationStatus.Captured, captured.Status);
        Assert.NotNull(captured.DebitOperationId);
        Assert.NotEqual(
            command.TerminalCommandId,
            captured.DebitOperationId);
        Assert.Equal(new Money(300), account.Balance);
        var debit = Assert.Single(
            account.Movements,
            movement =>
                movement.Type == CreditMovementType.Debit);
        Assert.Equal(new Money(700), debit.Amount);
        Assert.Equal(captured.DebitOperationId, debit.OperationId);
        Assert.Equal(new Money(300), await repository.GetBlockingAmountAsync(
            account.Id,
            CancellationToken.None));
        Assert.Equal(1, repository.SaveCount);
        Assert.Equal(
            "print-reservation.captured",
            Assert.Single(audit.Entries).Action);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReleaseAsync_RemovesBlockingWithoutBookedMovement(
        bool resolutionRequired)
    {
        var ownerId = Guid.NewGuid();
        var account = CreateAccount(ownerId, balanceMinorUnits: 1_000);
        var repository = new FakePrintReservationRepository([]);
        var reservation = CreateResult(
            account.Id,
            amountMinorUnits: 700,
            resolutionRequired
                ? PrintReservationStatus.ResolutionRequired
                : PrintReservationStatus.Reserved);
        repository.Reservations.Add(reservation);
        var audit = new RecordingAuditTrail();
        var service = CreateService(
            new FakeCreditAccountRepository(account, []),
            repository,
            new ImmediateTransaction(),
            audit);
        var command = new ReleasePrintReservationCommand(
            reservation.Id,
            reservation.PrintSourceId,
            Guid.NewGuid());
        var movementCount = account.Movements.Count;

        var released = await service.ReleaseAsync(command);
        var replayed = await service.ReleaseAsync(command);

        Assert.Equal(released, replayed);
        Assert.Equal(PrintReservationStatus.Released, released.Status);
        Assert.Equal(new Money(1_000), account.Balance);
        Assert.Equal(movementCount, account.Movements.Count);
        Assert.Equal(Money.Zero, await repository.GetBlockingAmountAsync(
            account.Id,
            CancellationToken.None));
        Assert.Equal(1, repository.SaveCount);
        Assert.Equal(
            "print-reservation.released",
            Assert.Single(audit.Entries).Action);
    }

    [Fact]
    public async Task CaptureAndRelease_ConflictsCannotChangeTerminalState()
    {
        var ownerId = Guid.NewGuid();
        var account = CreateAccount(ownerId, balanceMinorUnits: 1_000);
        var repository = new FakePrintReservationRepository([]);
        var reservation = CreateResult(
            account.Id,
            amountMinorUnits: 400,
            PrintReservationStatus.Reserved);
        repository.Reservations.Add(reservation);
        var audit = new RecordingAuditTrail();
        var service = CreateService(
            new FakeCreditAccountRepository(account, []),
            repository,
            new ImmediateTransaction(),
            audit);
        var terminalCommandId = Guid.NewGuid();

        var captured = await service.CaptureAsync(
            new CapturePrintReservationCommand(
                reservation.Id,
                reservation.PrintSourceId,
                terminalCommandId));

        await Assert.ThrowsAsync<PrintReservationTerminalCommandConflictException>(
            () => service.ReleaseAsync(
                new ReleasePrintReservationCommand(
                    reservation.Id,
                    reservation.PrintSourceId,
                    terminalCommandId)));
        await Assert.ThrowsAsync<PrintReservationTerminalCommandConflictException>(
            () => service.ReleaseAsync(
                new ReleasePrintReservationCommand(
                    reservation.Id,
                    reservation.PrintSourceId,
                    Guid.NewGuid())));

        Assert.Equal(
            PrintReservationStatus.Captured,
            repository.Reservations.Single(
                item => item.Id == reservation.Id).Status);
        Assert.Equal(new Money(600), account.Balance);
        Assert.Equal(1, repository.SaveCount);
        Assert.Single(audit.Entries);
        Assert.Equal(captured.DebitOperationId, account.Movements[^1].OperationId);
    }

    private static PrintReservationService CreateService(
        ICreditAccountRepository accountRepository,
        IPrintReservationRepository reservationRepository,
        IApplicationTransaction transaction,
        IAuditTrail? auditTrail = null)
    {
        return new PrintReservationService(
            accountRepository,
            reservationRepository,
            new CreditAvailabilityService(
                new FakeCreditAvailabilityRepository(
                    (FakePrintReservationRepository)reservationRepository)),
            transaction,
            auditTrail ?? NullAuditTrail.Instance,
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

        public Task<CreditAccount?> FindByIdForUpdateAsync(
            Guid accountId,
            CancellationToken cancellationToken)
        {
            _calls.Add("account-lock-by-id");
            return Task.FromResult(
                _account?.Id == accountId ? _account : null);
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
            CancellationToken cancellationToken)
        {
            _calls.Add("account-save");
            return Task.CompletedTask;
        }
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

        public int SaveCount { get; private set; }

        public Action<PrintReservation>? BeforeAdd { get; set; }

        public Exception? AddException { get; set; }

        public Task<PrintReservationResult?> FindByIdAsync(
            Guid reservationId,
            CancellationToken cancellationToken)
        {
            _calls.Add("reservation-read");
            return Task.FromResult(
                Reservations.SingleOrDefault(
                    reservation => reservation.Id == reservationId));
        }

        public Task<PrintReservation?> FindByIdForUpdateAsync(
            Guid reservationId,
            CancellationToken cancellationToken)
        {
            _calls.Add("reservation-lock");
            var result = Reservations.SingleOrDefault(
                reservation => reservation.Id == reservationId);
            return Task.FromResult(
                result is null ? null : Restore(result));
        }

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

        public Task<PrintReservationResult?> FindByResolutionCommandAsync(
            Guid printSourceId,
            Guid resolutionCommandId,
            CancellationToken cancellationToken)
        {
            _calls.Add("resolution-command-read");
            return Task.FromResult(
                Reservations.SingleOrDefault(
                    reservation =>
                        reservation.PrintSourceId == printSourceId &&
                        reservation.ResolutionCommandId ==
                            resolutionCommandId));
        }

        public Task<PrintReservationResult?> FindByTerminalCommandAsync(
            Guid printSourceId,
            Guid terminalCommandId,
            CancellationToken cancellationToken)
        {
            _calls.Add("terminal-command-read");
            return Task.FromResult(
                Reservations.SingleOrDefault(
                    reservation =>
                        reservation.PrintSourceId == printSourceId &&
                        reservation.TerminalCommandId ==
                            terminalCommandId));
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

        public Task SaveAsync(
            PrintReservation reservation,
            CancellationToken cancellationToken)
        {
            _calls.Add("reservation-save");
            SaveCount++;
            var index = Reservations.FindIndex(
                item => item.Id == reservation.Id);
            var current = Reservations[index];
            Reservations[index] = new PrintReservationResult(
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
                current.Version + 1);
            return Task.CompletedTask;
        }

        private static PrintReservation Restore(
            PrintReservationResult result)
        {
            var reservation = new PrintReservation(
                result.Id,
                result.CreditAccountId,
                result.PrintSourceId,
                result.JobUuid,
                result.Amount,
                result.ReserveCommandId,
                result.CreatedAt);

            if (result.ResolutionCommandId.HasValue)
            {
                _ = reservation.RequireResolution(
                    result.ResolutionCommandId.Value,
                    result.StateChangedAt);
            }

            if (result.Status == PrintReservationStatus.Captured)
            {
                _ = reservation.Capture(
                    result.TerminalCommandId!.Value,
                    result.DebitOperationId!.Value,
                    result.StateChangedAt);
            }
            else if (result.Status == PrintReservationStatus.Released)
            {
                _ = reservation.Release(
                    result.TerminalCommandId!.Value,
                    result.StateChangedAt);
            }

            return reservation;
        }
    }

    private sealed class FakeCreditAvailabilityRepository :
        ICreditAvailabilityRepository
    {
        private readonly FakePrintReservationRepository _reservations;

        public FakeCreditAvailabilityRepository(
            FakePrintReservationRepository reservations)
        {
            _reservations = reservations;
        }

        public Task<Money> GetTotalBlockingAmountAsync(
            Guid creditAccountId,
            CancellationToken cancellationToken) =>
            _reservations.GetBlockingAmountAsync(
                creditAccountId,
                cancellationToken);
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

    private sealed class RecordingAuditTrail : IAuditTrail
    {
        public List<AuditEntry> Entries { get; } = [];

        public void Stage(AuditEntry entry) => Entries.Add(entry);

        public Task WriteAsync(
            AuditEntry entry,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
