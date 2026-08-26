using FuaPay.Web.BuildingBlocks.Application;
using FuaPay.Web.BuildingBlocks.Auditing;
using FuaPay.Web.BuildingBlocks.Domain;
using FuaPay.Web.BuildingBlocks.Persistence;
using FuaPay.Web.Modules.Credits.Application;
using FuaPay.Web.Modules.Credits.Domain;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FuaPay.DatabaseTests;

public sealed class PrintReservationServicePersistenceTests :
    IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly DateTimeOffset SeedTime =
        new(2026, 8, 26, 12, 0, 0, TimeSpan.Zero);

    private readonly WebApplicationFactory<Program> _factory;

    public PrintReservationServicePersistenceTests(
        WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ReserveAsync_PersistsReplaysAndPreservesBookedBalance()
    {
        var ownerId = Guid.NewGuid();
        var printSourceId = Guid.NewGuid();
        var reserveCommandId = Guid.NewGuid();
        var jobId = Guid.NewGuid();

        try
        {
            await CreateAccountAsync(ownerId, balanceMinorUnits: 1_000);

            using var scope = _factory.Services.CreateScope();
            var service = scope.ServiceProvider
                .GetRequiredService<PrintReservationService>();
            var command = new ReservePrintCreditCommand(
                ownerId,
                printSourceId,
                $"URN:UUID:{jobId:D}".ToUpperInvariant(),
                new Money(400),
                reserveCommandId);

            var created = await service.ReserveAsync(command);
            var replayed = await service.ReserveAsync(command);

            Assert.Equal(created.Id, replayed.Id);
            Assert.Equal(
                created.ReserveCommandId,
                replayed.ReserveCommandId);
            Assert.Equal(created.Version, replayed.Version);
            Assert.Equal($"urn:uuid:{jobId:D}", created.JobUuid);
            Assert.Equal(PrintReservationStatus.Reserved, created.Status);
            Assert.Equal(1, created.Version);

            await Assert.ThrowsAsync<PrintReservationCommandConflictException>(
                () => service.ReserveAsync(
                    new ReservePrintCreditCommand(
                        ownerId,
                        printSourceId,
                        command.JobUuid,
                        new Money(399),
                        reserveCommandId)));
            await Assert.ThrowsAsync<PrintReservationJobConflictException>(
                () => service.ReserveAsync(
                    new ReservePrintCreditCommand(
                        ownerId,
                        printSourceId,
                        command.JobUuid,
                        command.Amount,
                        Guid.NewGuid())));

            var state = await ReadAccountStateAsync(
                ownerId,
                printSourceId);

            Assert.Equal(1_000, state.BalanceMinorUnits);
            Assert.Equal(1, state.MovementCount);
            Assert.Equal(1, state.ReservationCount);
            Assert.Equal(400, state.ReservedMinorUnits);
            Assert.Equal(
                1,
                await CountReservationAuditAsync(
                    created.Id,
                    "print-reservation.reserved"));
        }
        finally
        {
            await DeleteScenarioAsync([ownerId]);
        }
    }

    [Fact]
    public async Task ReserveAsync_AuditPersistenceFailure_RollsBackReservationAndBookedAmount()
    {
        var ownerId = Guid.NewGuid();
        var duplicateAudit = CreateDuplicateAudit(
            "Audit row used to force reserve rollback.");

        try
        {
            await CreateAccountAsync(ownerId, balanceMinorUnits: 1_000);
            await WriteAuditAsync(duplicateAudit);
            var reserveAuditCount = await CountAuditActionAsync(
                "print-reservation.reserved");

            using (var scope = _factory.Services.CreateScope())
            {
                var services = scope.ServiceProvider;
                var service = new PrintReservationService(
                    services.GetRequiredService<ICreditAccountRepository>(),
                    services.GetRequiredService<IPrintReservationRepository>(),
                    services.GetRequiredService<IApplicationTransaction>(),
                    new DuplicateAuditTrail(
                        services.GetRequiredService<IAuditTrail>(),
                        duplicateAudit),
                    services.GetRequiredService<TimeProvider>());

                await Assert.ThrowsAsync<DbUpdateException>(
                    () => service.ReserveAsync(
                        CreateCommand(ownerId, amountMinorUnits: 400)));
            }

            var state = await ReadAccountStateAsync(
                ownerId,
                printSourceId: null);

            Assert.Equal(1_000, state.BalanceMinorUnits);
            Assert.Equal(1, state.MovementCount);
            Assert.Equal(0, state.ReservationCount);
            Assert.Equal(0, state.ReservedMinorUnits);
            Assert.Equal(
                reserveAuditCount,
                await CountAuditActionAsync(
                    "print-reservation.reserved"));
        }
        finally
        {
            await DeleteScenarioAsync([ownerId]);
            await DeleteAuditAsync(duplicateAudit.Id);
        }
    }

    [Fact]
    public async Task CaptureAsync_CreditPersistenceFailure_RollsBackDebitAndKeepsReservationBlocking()
    {
        var ownerId = Guid.NewGuid();

        try
        {
            await CreateAccountAsync(ownerId, balanceMinorUnits: 1_000);
            var reservation = await CreateReservedAsync(
                ownerId,
                amountMinorUnits: 700);

            using (var scope = _factory.Services.CreateScope())
            {
                var services = scope.ServiceProvider;
                var service = new PrintReservationService(
                    new ThrowingCreditSaveRepository(
                        services.GetRequiredService<ICreditAccountRepository>()),
                    services.GetRequiredService<IPrintReservationRepository>(),
                    services.GetRequiredService<IApplicationTransaction>(),
                    services.GetRequiredService<IAuditTrail>(),
                    services.GetRequiredService<TimeProvider>());

                var exception = await Assert.ThrowsAsync<TimeoutException>(
                    () => service.CaptureAsync(
                        new CapturePrintReservationCommand(
                            reservation.Id,
                            reservation.PrintSourceId,
                            Guid.NewGuid())));

                Assert.Equal(
                    ThrowingCreditSaveRepository.FailureMessage,
                    exception.Message);
            }

            await AssertFailedCaptureStateAsync(ownerId, reservation);
        }
        finally
        {
            await DeleteScenarioAsync([ownerId]);
        }
    }

    [Fact]
    public async Task CaptureAsync_ReservationSaveFailure_RollsBackPriorCreditSaveChanges()
    {
        var ownerId = Guid.NewGuid();

        try
        {
            await CreateAccountAsync(ownerId, balanceMinorUnits: 1_000);
            var reservation = await CreateReservedAsync(
                ownerId,
                amountMinorUnits: 700);

            using (var scope = _factory.Services.CreateScope())
            {
                var services = scope.ServiceProvider;
                var service = new PrintReservationService(
                    services.GetRequiredService<ICreditAccountRepository>(),
                    new ThrowingReservationSaveRepository(
                        services.GetRequiredService<IPrintReservationRepository>()),
                    services.GetRequiredService<IApplicationTransaction>(),
                    services.GetRequiredService<IAuditTrail>(),
                    services.GetRequiredService<TimeProvider>());

                var exception = await Assert.ThrowsAsync<TimeoutException>(
                    () => service.CaptureAsync(
                        new CapturePrintReservationCommand(
                            reservation.Id,
                            reservation.PrintSourceId,
                            Guid.NewGuid())));

                Assert.Equal(
                    ThrowingReservationSaveRepository.FailureMessage,
                    exception.Message);
            }

            await AssertFailedCaptureStateAsync(ownerId, reservation);
        }
        finally
        {
            await DeleteScenarioAsync([ownerId]);
        }
    }

    [Fact]
    public async Task CaptureAsync_AuditPersistenceFailure_RollsBackEntireFinancialTransition()
    {
        var ownerId = Guid.NewGuid();
        var duplicateAudit = CreateDuplicateAudit(
            "Audit row used to force capture rollback.");

        try
        {
            await CreateAccountAsync(ownerId, balanceMinorUnits: 1_000);
            var reservation = await CreateReservedAsync(
                ownerId,
                amountMinorUnits: 700);
            await WriteAuditAsync(duplicateAudit);

            using (var scope = _factory.Services.CreateScope())
            {
                var services = scope.ServiceProvider;
                var service = new PrintReservationService(
                    services.GetRequiredService<ICreditAccountRepository>(),
                    services.GetRequiredService<IPrintReservationRepository>(),
                    services.GetRequiredService<IApplicationTransaction>(),
                    new DuplicateAuditTrail(
                        services.GetRequiredService<IAuditTrail>(),
                        duplicateAudit),
                    services.GetRequiredService<TimeProvider>());

                await Assert.ThrowsAsync<DbUpdateException>(
                    () => service.CaptureAsync(
                        new CapturePrintReservationCommand(
                            reservation.Id,
                            reservation.PrintSourceId,
                            Guid.NewGuid())));
            }

            await AssertFailedCaptureStateAsync(ownerId, reservation);
        }
        finally
        {
            await DeleteScenarioAsync([ownerId]);
            await DeleteAuditAsync(duplicateAudit.Id);
        }
    }

    [Fact]
    public async Task ReserveAsync_BlockingSumIgnoresCapturedAndReleased()
    {
        var ownerId = Guid.NewGuid();

        try
        {
            var accountId =
                await CreateAccountAsync(ownerId, balanceMinorUnits: 1_000);
            await InsertReservationAsync(
                accountId,
                amountMinorUnits: 200,
                PrintReservationStatus.Reserved);
            await InsertReservationAsync(
                accountId,
                amountMinorUnits: 300,
                PrintReservationStatus.ResolutionRequired);
            await InsertReservationAsync(
                accountId,
                amountMinorUnits: 5_000,
                PrintReservationStatus.Captured);
            await InsertReservationAsync(
                accountId,
                amountMinorUnits: 5_000,
                PrintReservationStatus.Released);

            using var scope = _factory.Services.CreateScope();
            var service = scope.ServiceProvider
                .GetRequiredService<PrintReservationService>();

            var result = await service.ReserveAsync(
                CreateCommand(ownerId, amountMinorUnits: 500));

            Assert.Equal(new Money(500), result.Amount);

            var exception =
                await Assert.ThrowsAsync<InsufficientAvailablePrintCreditException>(
                    () => service.ReserveAsync(
                        CreateCommand(ownerId, amountMinorUnits: 1)));

            Assert.Equal(Money.Zero, exception.Available);
        }
        finally
        {
            await DeleteScenarioAsync([ownerId]);
        }
    }

    [Theory]
    [InlineData("reserve-command")]
    [InlineData("print-job")]
    public async Task ReserveAsync_UniqueRaceIsTranslatedAndResolved(
        string uniqueScope)
    {
        var firstOwnerId = Guid.NewGuid();
        var secondOwnerId = Guid.NewGuid();
        var printSourceId = Guid.NewGuid();
        var sharedCommandId = Guid.NewGuid();
        var sharedJobUuid = $"urn:uuid:{Guid.NewGuid():D}";

        try
        {
            await CreateAccountAsync(
                firstOwnerId,
                balanceMinorUnits: 1_000);
            await CreateAccountAsync(
                secondOwnerId,
                balanceMinorUnits: 1_000);

            using var firstScope = _factory.Services.CreateScope();
            using var secondScope = _factory.Services.CreateScope();
            var barrier = new AsyncBarrier(participantCount: 2);
            var firstService = CreateServiceWithAddBarrier(
                firstScope.ServiceProvider,
                barrier);
            var secondService = CreateServiceWithAddBarrier(
                secondScope.ServiceProvider,
                barrier);
            var firstCommand = new ReservePrintCreditCommand(
                firstOwnerId,
                printSourceId,
                uniqueScope == "print-job"
                    ? sharedJobUuid
                    : $"urn:uuid:{Guid.NewGuid():D}",
                new Money(400),
                uniqueScope == "reserve-command"
                    ? sharedCommandId
                    : Guid.NewGuid());
            var secondCommand = new ReservePrintCreditCommand(
                secondOwnerId,
                printSourceId,
                uniqueScope == "print-job"
                    ? sharedJobUuid
                    : $"urn:uuid:{Guid.NewGuid():D}",
                new Money(400),
                uniqueScope == "reserve-command"
                    ? sharedCommandId
                    : Guid.NewGuid());

            var attempts = await Task.WhenAll(
                CaptureAsync(firstService.ReserveAsync(firstCommand)),
                CaptureAsync(secondService.ReserveAsync(secondCommand)));

            Assert.Single(attempts, attempt => attempt.Result is not null);
            var failure = Assert.Single(
                attempts,
                attempt => attempt.Exception is not null);

            if (uniqueScope == "reserve-command")
            {
                Assert.IsType<PrintReservationCommandConflictException>(
                    failure.Exception);
            }
            else
            {
                Assert.IsType<PrintReservationJobConflictException>(
                    failure.Exception);
            }

            Assert.Equal(
                1,
                await CountReservationsAsync(printSourceId));
        }
        finally
        {
            await DeleteScenarioAsync([firstOwnerId, secondOwnerId]);
        }
    }

    [Fact]
    public async Task ReserveAsync_ConcurrentSameAccountCannotOverReserve()
    {
        var ownerId = Guid.NewGuid();

        try
        {
            await CreateAccountAsync(ownerId, balanceMinorUnits: 1_000);

            using var firstScope = _factory.Services.CreateScope();
            using var secondScope = _factory.Services.CreateScope();
            var firstService = firstScope.ServiceProvider
                .GetRequiredService<PrintReservationService>();
            var secondService = secondScope.ServiceProvider
                .GetRequiredService<PrintReservationService>();

            var attempts = await Task.WhenAll(
                CaptureAsync(
                    firstService.ReserveAsync(
                        CreateCommand(ownerId, amountMinorUnits: 700))),
                CaptureAsync(
                    secondService.ReserveAsync(
                        CreateCommand(ownerId, amountMinorUnits: 700))));

            Assert.Single(attempts, attempt => attempt.Result is not null);
            var failure = Assert.Single(
                attempts,
                attempt => attempt.Exception is not null);
            Assert.IsType<InsufficientAvailablePrintCreditException>(
                failure.Exception);

            var state = await ReadAccountStateAsync(
                ownerId,
                printSourceId: null);

            Assert.Equal(1_000, state.BalanceMinorUnits);
            Assert.Equal(1, state.MovementCount);
            Assert.Equal(1, state.ReservationCount);
            Assert.Equal(700, state.ReservedMinorUnits);
        }
        finally
        {
            await DeleteScenarioAsync([ownerId]);
        }
    }

    [Fact]
    public async Task DebitAsync_UsesOnlyAvailableCreditAndIgnoresTerminalReservations()
    {
        var ownerId = Guid.NewGuid();

        try
        {
            var accountId =
                await CreateAccountAsync(ownerId, balanceMinorUnits: 1_000);
            await InsertReservationAsync(
                accountId,
                amountMinorUnits: 200,
                PrintReservationStatus.Reserved);
            await InsertReservationAsync(
                accountId,
                amountMinorUnits: 200,
                PrintReservationStatus.ResolutionRequired);
            await InsertReservationAsync(
                accountId,
                amountMinorUnits: 5_000,
                PrintReservationStatus.Captured);
            await InsertReservationAsync(
                accountId,
                amountMinorUnits: 5_000,
                PrintReservationStatus.Released);

            using var scope = _factory.Services.CreateScope();
            var service = scope.ServiceProvider
                .GetRequiredService<CreditService>();

            var movement = await service.DebitAsync(
                ownerId,
                Guid.NewGuid(),
                new Money(600),
                "Ordinary debit at the available limit");

            Assert.Equal(new Money(400), movement.BalanceAfter);

            await Assert.ThrowsAsync<InsufficientCreditException>(
                () => service.DebitAsync(
                    ownerId,
                    Guid.NewGuid(),
                    new Money(1),
                    "Debit of reserved funds"));

            var state = await ReadAccountStateAsync(
                ownerId,
                printSourceId: null);

            Assert.Equal(400, state.BalanceMinorUnits);
            Assert.Equal(2, state.MovementCount);
            Assert.Equal(4, state.ReservationCount);
            Assert.Equal(400, state.ReservedMinorUnits);
        }
        finally
        {
            await DeleteScenarioAsync([ownerId]);
        }
    }

    [Theory]
    [InlineData("reserve")]
    [InlineData("debit")]
    public async Task ReserveAndDebit_ConcurrentSameAccountCannotOvercommit(
        string firstOperation)
    {
        var ownerId = Guid.NewGuid();
        var gate = new AccountLockGate();

        try
        {
            await CreateAccountAsync(ownerId, balanceMinorUnits: 1_000);
            using var factory = CreateCoordinatedFactory(ownerId, gate);
            using var firstScope = factory.Services.CreateScope();
            using var secondScope = factory.Services.CreateScope();

            var secondOperation = firstOperation == "reserve"
                ? "debit"
                : "reserve";
            var firstTask = Record.ExceptionAsync(
                () => ExecuteMutationAsync(
                    firstScope.ServiceProvider,
                    ownerId,
                    firstOperation,
                    amountMinorUnits: 700));

            await gate.FirstLockAcquired.WaitAsync(
                TimeSpan.FromSeconds(30));

            var secondTask = Record.ExceptionAsync(
                () => ExecuteMutationAsync(
                    secondScope.ServiceProvider,
                    ownerId,
                    secondOperation,
                    amountMinorUnits: 700));

            await gate.SecondLockAttempted.WaitAsync(
                TimeSpan.FromSeconds(30));
            gate.ReleaseFirstLock();

            var failures = await Task.WhenAll(firstTask, secondTask);

            Assert.Null(failures[0]);

            if (firstOperation == "reserve")
            {
                Assert.IsType<InsufficientCreditException>(failures[1]);
            }
            else
            {
                Assert.IsType<InsufficientAvailablePrintCreditException>(
                    failures[1]);
            }

            var state = await ReadAccountStateAsync(
                ownerId,
                printSourceId: null);

            if (firstOperation == "reserve")
            {
                Assert.Equal(1_000, state.BalanceMinorUnits);
                Assert.Equal(1, state.MovementCount);
                Assert.Equal(1, state.ReservationCount);
                Assert.Equal(700, state.ReservedMinorUnits);
            }
            else
            {
                Assert.Equal(300, state.BalanceMinorUnits);
                Assert.Equal(2, state.MovementCount);
                Assert.Equal(0, state.ReservationCount);
                Assert.Equal(0, state.ReservedMinorUnits);
            }
        }
        finally
        {
            gate.ReleaseFirstLock();
            await DeleteScenarioAsync([ownerId]);
        }
    }

    [Fact]
    public async Task DebitAsync_ConcurrentDebitsRespectExistingReservation()
    {
        var ownerId = Guid.NewGuid();
        var gate = new AccountLockGate();

        try
        {
            var accountId =
                await CreateAccountAsync(ownerId, balanceMinorUnits: 1_000);
            await InsertReservationAsync(
                accountId,
                amountMinorUnits: 400,
                PrintReservationStatus.Reserved);
            using var factory = CreateCoordinatedFactory(ownerId, gate);
            using var firstScope = factory.Services.CreateScope();
            using var secondScope = factory.Services.CreateScope();
            var firstService = firstScope.ServiceProvider
                .GetRequiredService<CreditService>();
            var secondService = secondScope.ServiceProvider
                .GetRequiredService<CreditService>();

            var firstTask = Record.ExceptionAsync(
                async () =>
                    _ = await firstService.DebitAsync(
                        ownerId,
                        Guid.NewGuid(),
                        new Money(400),
                        "First concurrent debit"));

            await gate.FirstLockAcquired.WaitAsync(
                TimeSpan.FromSeconds(30));

            var secondTask = Record.ExceptionAsync(
                async () =>
                    _ = await secondService.DebitAsync(
                        ownerId,
                        Guid.NewGuid(),
                        new Money(400),
                        "Second concurrent debit"));

            await gate.SecondLockAttempted.WaitAsync(
                TimeSpan.FromSeconds(30));
            gate.ReleaseFirstLock();

            var failures = await Task.WhenAll(firstTask, secondTask);

            Assert.Null(failures[0]);
            Assert.IsType<InsufficientCreditException>(failures[1]);

            var state = await ReadAccountStateAsync(
                ownerId,
                printSourceId: null);

            Assert.Equal(600, state.BalanceMinorUnits);
            Assert.Equal(2, state.MovementCount);
            Assert.Equal(1, state.ReservationCount);
            Assert.Equal(400, state.ReservedMinorUnits);
        }
        finally
        {
            gate.ReleaseFirstLock();
            await DeleteScenarioAsync([ownerId]);
        }
    }

    [Fact]
    public async Task LifecycleAsync_IsAtomicAuditedAndIdempotent()
    {
        var ownerId = Guid.NewGuid();
        var printSourceId = Guid.NewGuid();

        try
        {
            await CreateAccountAsync(ownerId, balanceMinorUnits: 1_000);
            using var scope = _factory.Services.CreateScope();
            var reservations = scope.ServiceProvider
                .GetRequiredService<PrintReservationService>();
            var credit = scope.ServiceProvider
                .GetRequiredService<CreditService>();
            var first = await reservations.ReserveAsync(
                CreateCommand(
                    ownerId,
                    printSourceId,
                    amountMinorUnits: 600));
            var second = await reservations.ReserveAsync(
                CreateCommand(
                    ownerId,
                    printSourceId,
                    amountMinorUnits: 400));
            var resolutionCommand =
                new RequirePrintReservationResolutionCommand(
                    first.Id,
                    printSourceId,
                    Guid.NewGuid());

            var unresolved = await reservations.RequireResolutionAsync(
                resolutionCommand);
            var resolutionReplay =
                await reservations.RequireResolutionAsync(
                    resolutionCommand);

            Assert.Equal(unresolved, resolutionReplay);
            Assert.Equal(
                PrintReservationStatus.ResolutionRequired,
                unresolved.Status);

            var captureCommand = new CapturePrintReservationCommand(
                first.Id,
                printSourceId,
                Guid.NewGuid());
            var captured = await reservations.CaptureAsync(captureCommand);
            var captureReplay =
                await reservations.CaptureAsync(captureCommand);

            Assert.Equal(captured, captureReplay);
            Assert.Equal(PrintReservationStatus.Captured, captured.Status);
            Assert.NotNull(captured.DebitOperationId);
            Assert.NotEqual(
                captureCommand.TerminalCommandId,
                captured.DebitOperationId);
            Assert.Equal(3, captured.Version);

            await Assert.ThrowsAsync<InsufficientCreditException>(
                () => credit.DebitAsync(
                    ownerId,
                    Guid.NewGuid(),
                    new Money(1),
                    "Debit of another reservation"));

            var releaseCommand = new ReleasePrintReservationCommand(
                second.Id,
                printSourceId,
                Guid.NewGuid());
            var released = await reservations.ReleaseAsync(releaseCommand);
            var releaseReplay =
                await reservations.ReleaseAsync(releaseCommand);

            Assert.Equal(released, releaseReplay);
            Assert.Equal(PrintReservationStatus.Released, released.Status);
            Assert.Equal(2, released.Version);

            var state = await ReadLifecycleStateAsync(ownerId);

            Assert.Equal(400, state.BalanceMinorUnits);
            Assert.Equal(2, state.MovementCount);
            Assert.Equal(1, state.CaptureMovementCount);
            Assert.Equal(2, state.ReservationCount);
            Assert.Equal(0, state.BlockingMinorUnits);
            Assert.Equal(5, state.AuditCount);
        }
        finally
        {
            await DeleteScenarioAsync([ownerId]);
        }
    }

    [Fact]
    public async Task LifecycleCommands_ConflictDeterministically()
    {
        var ownerId = Guid.NewGuid();
        var printSourceId = Guid.NewGuid();

        try
        {
            await CreateAccountAsync(ownerId, balanceMinorUnits: 1_000);
            using var scope = _factory.Services.CreateScope();
            var service = scope.ServiceProvider
                .GetRequiredService<PrintReservationService>();
            var first = await service.ReserveAsync(
                CreateCommand(
                    ownerId,
                    printSourceId,
                    amountMinorUnits: 400));
            var second = await service.ReserveAsync(
                CreateCommand(
                    ownerId,
                    printSourceId,
                    amountMinorUnits: 400));
            var resolutionCommandId = Guid.NewGuid();

            _ = await service.RequireResolutionAsync(
                new RequirePrintReservationResolutionCommand(
                    first.Id,
                    printSourceId,
                    resolutionCommandId));
            await Assert.ThrowsAsync<PrintReservationResolutionCommandConflictException>(
                () => service.RequireResolutionAsync(
                    new RequirePrintReservationResolutionCommand(
                        second.Id,
                        printSourceId,
                        resolutionCommandId)));

            var terminalCommandId = Guid.NewGuid();
            _ = await service.CaptureAsync(
                new CapturePrintReservationCommand(
                    first.Id,
                    printSourceId,
                    terminalCommandId));
            await Assert.ThrowsAsync<PrintReservationTerminalCommandConflictException>(
                () => service.ReleaseAsync(
                    new ReleasePrintReservationCommand(
                        first.Id,
                        printSourceId,
                        terminalCommandId)));
            await Assert.ThrowsAsync<PrintReservationTerminalCommandConflictException>(
                () => service.ReleaseAsync(
                    new ReleasePrintReservationCommand(
                        first.Id,
                        printSourceId,
                        Guid.NewGuid())));
            await Assert.ThrowsAsync<PrintReservationTerminalCommandConflictException>(
                () => service.ReleaseAsync(
                    new ReleasePrintReservationCommand(
                        second.Id,
                        printSourceId,
                        terminalCommandId)));

            var state = await ReadLifecycleStateAsync(ownerId);

            Assert.Equal(600, state.BalanceMinorUnits);
            Assert.Equal(2, state.MovementCount);
            Assert.Equal(1, state.CaptureMovementCount);
            Assert.Equal(400, state.BlockingMinorUnits);
            Assert.Equal(4, state.AuditCount);
        }
        finally
        {
            await DeleteScenarioAsync([ownerId]);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CaptureAsync_ConcurrentSameReservationCreatesOneDebit(
        bool sameCommand)
    {
        var ownerId = Guid.NewGuid();
        var gate = new AccountLockGate();

        try
        {
            var accountId =
                await CreateAccountAsync(ownerId, balanceMinorUnits: 1_000);
            PrintReservationResult reservation;

            using (var seedScope = _factory.Services.CreateScope())
            {
                reservation = await seedScope.ServiceProvider
                    .GetRequiredService<PrintReservationService>()
                    .ReserveAsync(CreateCommand(
                        ownerId,
                        amountMinorUnits: 700));
            }

            using var factory = CreateCoordinatedFactory(
                ownerId,
                gate,
                accountId);
            using var firstScope = factory.Services.CreateScope();
            using var secondScope = factory.Services.CreateScope();
            var firstService = firstScope.ServiceProvider
                .GetRequiredService<PrintReservationService>();
            var secondService = secondScope.ServiceProvider
                .GetRequiredService<PrintReservationService>();
            var firstCommandId = Guid.NewGuid();
            var secondCommandId = sameCommand
                ? firstCommandId
                : Guid.NewGuid();
            var firstTask = CaptureAsync(
                firstService.CaptureAsync(
                    new CapturePrintReservationCommand(
                        reservation.Id,
                        reservation.PrintSourceId,
                        firstCommandId)));

            await gate.FirstLockAcquired.WaitAsync(
                TimeSpan.FromSeconds(30));

            var secondTask = CaptureAsync(
                secondService.CaptureAsync(
                    new CapturePrintReservationCommand(
                        reservation.Id,
                        reservation.PrintSourceId,
                        secondCommandId)));

            await gate.SecondLockAttempted.WaitAsync(
                TimeSpan.FromSeconds(30));
            gate.ReleaseFirstLock();

            var attempts = await Task.WhenAll(firstTask, secondTask);

            Assert.NotNull(attempts[0].Result);

            if (sameCommand)
            {
                Assert.NotNull(attempts[1].Result);
                Assert.Equal(
                    attempts[0].Result!.DebitOperationId,
                    attempts[1].Result!.DebitOperationId);
            }
            else
            {
                Assert.IsType<PrintReservationTerminalCommandConflictException>(
                    attempts[1].Exception);
            }

            var state = await ReadLifecycleStateAsync(ownerId);

            Assert.Equal(300, state.BalanceMinorUnits);
            Assert.Equal(2, state.MovementCount);
            Assert.Equal(1, state.CaptureMovementCount);
            Assert.Equal(0, state.BlockingMinorUnits);
            Assert.Equal(2, state.AuditCount);
        }
        finally
        {
            gate.ReleaseFirstLock();
            await DeleteScenarioAsync([ownerId]);
        }
    }

    [Theory]
    [InlineData("resolution")]
    [InlineData("capture")]
    [InlineData("release")]
    public async Task LifecycleCommand_ConcurrentDifferentAccountsHasOneEffect(
        string operation)
    {
        var firstOwnerId = Guid.NewGuid();
        var secondOwnerId = Guid.NewGuid();
        var printSourceId = Guid.NewGuid();

        try
        {
            await CreateAccountAsync(
                firstOwnerId,
                balanceMinorUnits: 1_000);
            await CreateAccountAsync(
                secondOwnerId,
                balanceMinorUnits: 1_000);
            PrintReservationResult firstReservation;
            PrintReservationResult secondReservation;

            using (var seedScope = _factory.Services.CreateScope())
            {
                var service = seedScope.ServiceProvider
                    .GetRequiredService<PrintReservationService>();
                firstReservation = await service.ReserveAsync(
                    CreateCommand(
                        firstOwnerId,
                        printSourceId,
                        amountMinorUnits: 400));
                secondReservation = await service.ReserveAsync(
                    CreateCommand(
                        secondOwnerId,
                        printSourceId,
                        amountMinorUnits: 400));
            }

            using var firstScope = _factory.Services.CreateScope();
            using var secondScope = _factory.Services.CreateScope();
            var barrier = new AsyncBarrier(participantCount: 2);
            var firstService = CreateServiceWithAddBarrier(
                firstScope.ServiceProvider,
                barrier);
            var secondService = CreateServiceWithAddBarrier(
                secondScope.ServiceProvider,
                barrier);
            var sharedCommandId = Guid.NewGuid();

            var attempts = await Task.WhenAll(
                CaptureAsync(ExecuteLifecycleWithCommandAsync(
                    firstService,
                    firstReservation,
                    operation,
                    sharedCommandId)),
                CaptureAsync(ExecuteLifecycleWithCommandAsync(
                    secondService,
                    secondReservation,
                    operation,
                    sharedCommandId)));

            Assert.Single(
                attempts,
                attempt => attempt.Result is not null);
            var failure = Assert.Single(
                attempts,
                attempt => attempt.Exception is not null);

            if (operation == "resolution")
            {
                Assert.IsType<PrintReservationResolutionCommandConflictException>(
                    failure.Exception);
            }
            else
            {
                Assert.IsType<PrintReservationTerminalCommandConflictException>(
                    failure.Exception);
            }

            var firstState = await ReadLifecycleStateAsync(firstOwnerId);
            var secondState = await ReadLifecycleStateAsync(secondOwnerId);

            Assert.Equal(
                operation == "capture" ? 1_600 : 2_000,
                firstState.BalanceMinorUnits +
                secondState.BalanceMinorUnits);
            Assert.Equal(
                operation == "capture" ? 1 : 0,
                firstState.CaptureMovementCount +
                secondState.CaptureMovementCount);
            Assert.Equal(
                operation == "resolution" ? 800 : 400,
                firstState.BlockingMinorUnits +
                secondState.BlockingMinorUnits);
            Assert.Equal(
                3,
                firstState.AuditCount + secondState.AuditCount);
        }
        finally
        {
            await DeleteScenarioAsync([firstOwnerId, secondOwnerId]);
        }
    }

    [Theory]
    [InlineData("capture")]
    [InlineData("release")]
    public async Task CaptureAndRelease_ConcurrentFirstTerminalWins(
        string firstOperation)
    {
        var ownerId = Guid.NewGuid();
        var gate = new AccountLockGate();

        try
        {
            var accountId =
                await CreateAccountAsync(ownerId, balanceMinorUnits: 1_000);
            PrintReservationResult reservation;

            using (var seedScope = _factory.Services.CreateScope())
            {
                reservation = await seedScope.ServiceProvider
                    .GetRequiredService<PrintReservationService>()
                    .ReserveAsync(CreateCommand(
                        ownerId,
                        amountMinorUnits: 700));
            }

            using var factory = CreateCoordinatedFactory(
                ownerId,
                gate,
                accountId);
            using var firstScope = factory.Services.CreateScope();
            using var secondScope = factory.Services.CreateScope();
            var firstTask = CaptureAsync(ExecuteTerminalAsync(
                firstScope.ServiceProvider,
                reservation,
                firstOperation,
                Guid.NewGuid()));

            await gate.FirstLockAcquired.WaitAsync(
                TimeSpan.FromSeconds(30));

            var secondTask = CaptureAsync(ExecuteTerminalAsync(
                secondScope.ServiceProvider,
                reservation,
                firstOperation == "capture" ? "release" : "capture",
                Guid.NewGuid()));

            await gate.SecondLockAttempted.WaitAsync(
                TimeSpan.FromSeconds(30));
            gate.ReleaseFirstLock();

            var attempts = await Task.WhenAll(firstTask, secondTask);

            Assert.NotNull(attempts[0].Result);
            Assert.IsType<PrintReservationTerminalCommandConflictException>(
                attempts[1].Exception);

            var state = await ReadLifecycleStateAsync(ownerId);

            Assert.Equal(
                firstOperation == "capture" ? 300 : 1_000,
                state.BalanceMinorUnits);
            Assert.Equal(
                firstOperation == "capture" ? 2 : 1,
                state.MovementCount);
            Assert.Equal(
                firstOperation == "capture" ? 1 : 0,
                state.CaptureMovementCount);
            Assert.Equal(0, state.BlockingMinorUnits);
            Assert.Equal(2, state.AuditCount);
        }
        finally
        {
            gate.ReleaseFirstLock();
            await DeleteScenarioAsync([ownerId]);
        }
    }

    [Theory]
    [InlineData("resolution", "capture")]
    [InlineData("resolution", "release")]
    [InlineData("capture", "resolution")]
    [InlineData("release", "resolution")]
    public async Task ResolutionAndTerminal_ConcurrentLifecycleIsSerialized(
        string firstOperation,
        string secondOperation)
    {
        var ownerId = Guid.NewGuid();
        var gate = new AccountLockGate();

        try
        {
            var accountId =
                await CreateAccountAsync(ownerId, balanceMinorUnits: 1_000);
            PrintReservationResult reservation;

            using (var seedScope = _factory.Services.CreateScope())
            {
                reservation = await seedScope.ServiceProvider
                    .GetRequiredService<PrintReservationService>()
                    .ReserveAsync(CreateCommand(
                        ownerId,
                        amountMinorUnits: 700));
            }

            using var factory = CreateCoordinatedFactory(
                ownerId,
                gate,
                accountId);
            using var firstScope = factory.Services.CreateScope();
            using var secondScope = factory.Services.CreateScope();
            var firstTask = CaptureAsync(ExecuteLifecycleAsync(
                firstScope.ServiceProvider,
                reservation,
                firstOperation));

            await gate.FirstLockAcquired.WaitAsync(
                TimeSpan.FromSeconds(30));

            var secondTask = CaptureAsync(ExecuteLifecycleAsync(
                secondScope.ServiceProvider,
                reservation,
                secondOperation));

            await gate.SecondLockAttempted.WaitAsync(
                TimeSpan.FromSeconds(30));
            gate.ReleaseFirstLock();

            var attempts = await Task.WhenAll(firstTask, secondTask);

            Assert.NotNull(attempts[0].Result);

            if (firstOperation == "resolution")
            {
                Assert.NotNull(attempts[1].Result);
            }
            else
            {
                Assert.IsType<PrintReservationResolutionCommandConflictException>(
                    attempts[1].Exception);
            }

            var terminalOperation = firstOperation == "resolution"
                ? secondOperation
                : firstOperation;
            var state = await ReadLifecycleStateAsync(ownerId);

            Assert.Equal(
                terminalOperation == "capture" ? 300 : 1_000,
                state.BalanceMinorUnits);
            Assert.Equal(0, state.BlockingMinorUnits);
            Assert.Equal(
                firstOperation == "resolution" ? 3 : 2,
                state.AuditCount);
        }
        finally
        {
            gate.ReleaseFirstLock();
            await DeleteScenarioAsync([ownerId]);
        }
    }

    [Theory]
    [InlineData("debit")]
    [InlineData("capture")]
    public async Task DebitAndCapture_ConcurrentMutationsPreserveOtherReservation(
        string firstOperation)
    {
        var ownerId = Guid.NewGuid();
        var gate = new AccountLockGate();

        try
        {
            var accountId =
                await CreateAccountAsync(ownerId, balanceMinorUnits: 1_000);
            PrintReservationResult capturedReservation;

            using (var seedScope = _factory.Services.CreateScope())
            {
                var service = seedScope.ServiceProvider
                    .GetRequiredService<PrintReservationService>();
                capturedReservation = await service.ReserveAsync(
                    CreateCommand(ownerId, amountMinorUnits: 600));
                _ = await service.ReserveAsync(
                    CreateCommand(ownerId, amountMinorUnits: 300));
            }

            using var factory = CreateCoordinatedFactory(
                ownerId,
                gate,
                accountId);
            using var firstScope = factory.Services.CreateScope();
            using var secondScope = factory.Services.CreateScope();
            var secondOperation = firstOperation == "debit"
                ? "capture"
                : "debit";
            var firstTask = Record.ExceptionAsync(
                () => ExecuteDebitOrCaptureAsync(
                    firstScope.ServiceProvider,
                    ownerId,
                    capturedReservation,
                    firstOperation));

            await gate.FirstLockAcquired.WaitAsync(
                TimeSpan.FromSeconds(30));

            var secondTask = Record.ExceptionAsync(
                () => ExecuteDebitOrCaptureAsync(
                    secondScope.ServiceProvider,
                    ownerId,
                    capturedReservation,
                    secondOperation));

            await gate.SecondLockAttempted.WaitAsync(
                TimeSpan.FromSeconds(30));
            gate.ReleaseFirstLock();

            var failures = await Task.WhenAll(firstTask, secondTask);

            Assert.All(failures, Assert.Null);

            var state = await ReadLifecycleStateAsync(ownerId);

            Assert.Equal(300, state.BalanceMinorUnits);
            Assert.Equal(3, state.MovementCount);
            Assert.Equal(1, state.CaptureMovementCount);
            Assert.Equal(300, state.BlockingMinorUnits);
            Assert.Equal(3, state.AuditCount);
        }
        finally
        {
            gate.ReleaseFirstLock();
            await DeleteScenarioAsync([ownerId]);
        }
    }

    [Theory]
    [InlineData("debit")]
    [InlineData("release")]
    public async Task DebitAndRelease_ConcurrentOutcomeFollowsAccountLockOrder(
        string firstOperation)
    {
        var ownerId = Guid.NewGuid();
        var gate = new AccountLockGate();

        try
        {
            var accountId =
                await CreateAccountAsync(ownerId, balanceMinorUnits: 1_000);
            PrintReservationResult reservation;

            using (var seedScope = _factory.Services.CreateScope())
            {
                reservation = await seedScope.ServiceProvider
                    .GetRequiredService<PrintReservationService>()
                    .ReserveAsync(CreateCommand(
                        ownerId,
                        amountMinorUnits: 700));
            }

            using var factory = CreateCoordinatedFactory(
                ownerId,
                gate,
                accountId);
            using var firstScope = factory.Services.CreateScope();
            using var secondScope = factory.Services.CreateScope();
            var secondOperation = firstOperation == "debit"
                ? "release"
                : "debit";
            var firstTask = Record.ExceptionAsync(
                () => ExecuteDebitOrReleaseAsync(
                    firstScope.ServiceProvider,
                    ownerId,
                    reservation,
                    firstOperation));

            await gate.FirstLockAcquired.WaitAsync(
                TimeSpan.FromSeconds(30));

            var secondTask = Record.ExceptionAsync(
                () => ExecuteDebitOrReleaseAsync(
                    secondScope.ServiceProvider,
                    ownerId,
                    reservation,
                    secondOperation));

            await gate.SecondLockAttempted.WaitAsync(
                TimeSpan.FromSeconds(30));
            gate.ReleaseFirstLock();

            var failures = await Task.WhenAll(firstTask, secondTask);

            if (firstOperation == "debit")
            {
                Assert.IsType<InsufficientCreditException>(failures[0]);
                Assert.Null(failures[1]);
            }
            else
            {
                Assert.All(failures, Assert.Null);
            }

            var state = await ReadLifecycleStateAsync(ownerId);

            Assert.Equal(
                firstOperation == "debit" ? 1_000 : 0,
                state.BalanceMinorUnits);
            Assert.Equal(
                firstOperation == "debit" ? 1 : 2,
                state.MovementCount);
            Assert.Equal(0, state.BlockingMinorUnits);
            Assert.Equal(2, state.AuditCount);
        }
        finally
        {
            gate.ReleaseFirstLock();
            await DeleteScenarioAsync([ownerId]);
        }
    }

    private async Task<Guid> CreateAccountAsync(
        Guid ownerId,
        long balanceMinorUnits)
    {
        using var scope = _factory.Services.CreateScope();
        var creditService = scope.ServiceProvider
            .GetRequiredService<CreditService>();
        await creditService.CreditAsync(
            ownerId,
            Guid.NewGuid(),
            new Money(balanceMinorUnits),
            "Print reservation test balance");

        var repository = scope.ServiceProvider
            .GetRequiredService<ICreditAccountRepository>();
        var account = await repository.FindByOwnerIdAsync(
            ownerId,
            CancellationToken.None);

        return Assert.IsType<CreditAccount>(account).Id;
    }

    private PrintReservationService CreateServiceWithAddBarrier(
        IServiceProvider services,
        AsyncBarrier barrier)
    {
        return new PrintReservationService(
            services.GetRequiredService<ICreditAccountRepository>(),
            new AddBarrierRepository(
                services.GetRequiredService<IPrintReservationRepository>(),
                barrier),
            services.GetRequiredService<IApplicationTransaction>(),
            services.GetRequiredService<IAuditTrail>(),
            services.GetRequiredService<TimeProvider>());
    }

    private WebApplicationFactory<Program> CreateCoordinatedFactory(
        Guid ownerId,
        AccountLockGate gate,
        Guid? accountId = null)
    {
        return _factory.WithWebHostBuilder(
            builder => builder.ConfigureTestServices(
                services =>
                {
                    var descriptor = Assert.Single(
                        services,
                        item => item.ServiceType ==
                            typeof(ICreditAccountRepository));

                    services.Remove(descriptor);
                    services.AddScoped<ICreditAccountRepository>(
                        provider =>
                            new CoordinatingCreditAccountRepository(
                                CreateOriginalCreditRepository(
                                    provider,
                                    descriptor),
                                ownerId,
                                accountId,
                                gate));
                }));
    }

    private static ICreditAccountRepository CreateOriginalCreditRepository(
        IServiceProvider provider,
        ServiceDescriptor descriptor)
    {
        if (descriptor.ImplementationInstance is
            ICreditAccountRepository instance)
        {
            return instance;
        }

        if (descriptor.ImplementationFactory is not null)
        {
            return (ICreditAccountRepository)
                descriptor.ImplementationFactory(provider);
        }

        return (ICreditAccountRepository)
            ActivatorUtilities.CreateInstance(
                provider,
                descriptor.ImplementationType
                    ?? throw new InvalidOperationException(
                        "The original credit repository has no implementation."));
    }

    private static async Task ExecuteMutationAsync(
        IServiceProvider services,
        Guid ownerId,
        string operation,
        long amountMinorUnits)
    {
        if (operation == "reserve")
        {
            _ = await services
                .GetRequiredService<PrintReservationService>()
                .ReserveAsync(
                    CreateCommand(ownerId, amountMinorUnits));
            return;
        }

        if (operation == "debit")
        {
            _ = await services
                .GetRequiredService<CreditService>()
                .DebitAsync(
                    ownerId,
                    Guid.NewGuid(),
                    new Money(amountMinorUnits),
                    "Concurrent ordinary debit");
            return;
        }

        throw new ArgumentOutOfRangeException(nameof(operation));
    }

    private static Task<PrintReservationResult> ExecuteTerminalAsync(
        IServiceProvider services,
        PrintReservationResult reservation,
        string operation,
        Guid commandId)
    {
        var service = services
            .GetRequiredService<PrintReservationService>();

        return operation switch
        {
            "capture" => service.CaptureAsync(
                new CapturePrintReservationCommand(
                    reservation.Id,
                    reservation.PrintSourceId,
                    commandId)),
            "release" => service.ReleaseAsync(
                new ReleasePrintReservationCommand(
                    reservation.Id,
                    reservation.PrintSourceId,
                    commandId)),
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        };
    }

    private static Task<PrintReservationResult> ExecuteLifecycleAsync(
        IServiceProvider services,
        PrintReservationResult reservation,
        string operation)
    {
        var service = services
            .GetRequiredService<PrintReservationService>();

        return operation switch
        {
            "resolution" => service.RequireResolutionAsync(
                new RequirePrintReservationResolutionCommand(
                    reservation.Id,
                    reservation.PrintSourceId,
                    Guid.NewGuid())),
            "capture" => service.CaptureAsync(
                new CapturePrintReservationCommand(
                    reservation.Id,
                    reservation.PrintSourceId,
                    Guid.NewGuid())),
            "release" => service.ReleaseAsync(
                new ReleasePrintReservationCommand(
                    reservation.Id,
                    reservation.PrintSourceId,
                    Guid.NewGuid())),
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        };
    }

    private static Task<PrintReservationResult>
        ExecuteLifecycleWithCommandAsync(
            PrintReservationService service,
            PrintReservationResult reservation,
            string operation,
            Guid commandId)
    {
        return operation switch
        {
            "resolution" => service.RequireResolutionAsync(
                new RequirePrintReservationResolutionCommand(
                    reservation.Id,
                    reservation.PrintSourceId,
                    commandId)),
            "capture" => service.CaptureAsync(
                new CapturePrintReservationCommand(
                    reservation.Id,
                    reservation.PrintSourceId,
                    commandId)),
            "release" => service.ReleaseAsync(
                new ReleasePrintReservationCommand(
                    reservation.Id,
                    reservation.PrintSourceId,
                    commandId)),
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        };
    }

    private static async Task ExecuteDebitOrCaptureAsync(
        IServiceProvider services,
        Guid ownerId,
        PrintReservationResult reservation,
        string operation)
    {
        if (operation == "debit")
        {
            _ = await services.GetRequiredService<CreditService>()
                .DebitAsync(
                    ownerId,
                    Guid.NewGuid(),
                    new Money(100),
                    "Concurrent ordinary debit");
            return;
        }

        if (operation == "capture")
        {
            _ = await services
                .GetRequiredService<PrintReservationService>()
                .CaptureAsync(
                    new CapturePrintReservationCommand(
                        reservation.Id,
                        reservation.PrintSourceId,
                        Guid.NewGuid()));
            return;
        }

        throw new ArgumentOutOfRangeException(nameof(operation));
    }

    private static async Task ExecuteDebitOrReleaseAsync(
        IServiceProvider services,
        Guid ownerId,
        PrintReservationResult reservation,
        string operation)
    {
        if (operation == "debit")
        {
            _ = await services.GetRequiredService<CreditService>()
                .DebitAsync(
                    ownerId,
                    Guid.NewGuid(),
                    new Money(1_000),
                    "Concurrent debit around release");
            return;
        }

        if (operation == "release")
        {
            _ = await services
                .GetRequiredService<PrintReservationService>()
                .ReleaseAsync(
                    new ReleasePrintReservationCommand(
                        reservation.Id,
                        reservation.PrintSourceId,
                        Guid.NewGuid()));
            return;
        }

        throw new ArgumentOutOfRangeException(nameof(operation));
    }

    private static ReservePrintCreditCommand CreateCommand(
        Guid ownerId,
        long amountMinorUnits)
    {
        return new ReservePrintCreditCommand(
            ownerId,
            Guid.NewGuid(),
            $"urn:uuid:{Guid.NewGuid():D}",
            new Money(amountMinorUnits),
            Guid.NewGuid());
    }

    private async Task<PrintReservationResult> CreateReservedAsync(
        Guid ownerId,
        long amountMinorUnits)
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider
            .GetRequiredService<PrintReservationService>()
            .ReserveAsync(
                CreateCommand(ownerId, amountMinorUnits));
    }

    private async Task AssertFailedCaptureStateAsync(
        Guid ownerId,
        PrintReservationResult reservation)
    {
        var state = await ReadLifecycleStateAsync(ownerId);

        Assert.Equal(1_000, state.BalanceMinorUnits);
        Assert.Equal(1, state.MovementCount);
        Assert.Equal(0, state.CaptureMovementCount);
        Assert.Equal(1, state.ReservationCount);
        Assert.Equal(
            reservation.Amount.MinorUnits,
            state.BlockingMinorUnits);
        Assert.Equal(1, state.AuditCount);

        using var scope = _factory.Services.CreateScope();
        var persisted = await scope.ServiceProvider
            .GetRequiredService<IPrintReservationRepository>()
            .FindByIdAsync(
                reservation.Id,
                CancellationToken.None);

        Assert.NotNull(persisted);
        Assert.Equal(PrintReservationStatus.Reserved, persisted.Status);
        Assert.Null(persisted.DebitOperationId);
        Assert.Equal(
            1,
            await CountReservationAuditAsync(
                reservation.Id,
                "print-reservation.reserved"));
        Assert.Equal(
            0,
            await CountReservationAuditAsync(
                reservation.Id,
                "print-reservation.captured"));
    }

    private static AuditEntry CreateDuplicateAudit(string description)
    {
        return AuditEntry.ForProcess(
            "database-test",
            "test.audit-duplicate",
            "test",
            Guid.NewGuid().ToString(),
            description,
            SeedTime);
    }

    private async Task WriteAuditAsync(AuditEntry audit)
    {
        using var scope = _factory.Services.CreateScope();
        await scope.ServiceProvider
            .GetRequiredService<IAuditTrail>()
            .WriteAsync(audit);
    }

    private async Task DeleteAuditAsync(Guid auditId)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider
            .GetRequiredService<FuaPayDbContext>();

        _ = await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM audit.events WHERE id = {auditId}");
    }

    private static ReservePrintCreditCommand CreateCommand(
        Guid ownerId,
        Guid printSourceId,
        long amountMinorUnits)
    {
        return new ReservePrintCreditCommand(
            ownerId,
            printSourceId,
            $"urn:uuid:{Guid.NewGuid():D}",
            new Money(amountMinorUnits),
            Guid.NewGuid());
    }

    private async Task InsertReservationAsync(
        Guid accountId,
        long amountMinorUnits,
        PrintReservationStatus status)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider
            .GetRequiredService<FuaPayDbContext>();
        var id = Guid.NewGuid();
        var printSourceId = Guid.NewGuid();
        var jobUuid = $"urn:uuid:{Guid.NewGuid():D}";
        var reserveCommandId = Guid.NewGuid();
        Guid? resolutionCommandId =
            status == PrintReservationStatus.ResolutionRequired
                ? Guid.NewGuid()
                : null;
        Guid? terminalCommandId =
            status is PrintReservationStatus.Captured or
                PrintReservationStatus.Released
                ? Guid.NewGuid()
                : null;
        Guid? debitOperationId =
            status == PrintReservationStatus.Captured
                ? Guid.NewGuid()
                : null;

        _ = await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO credits.print_reservations
            (
                id,
                credit_account_id,
                print_source_id,
                job_uuid,
                amount_minor_units,
                status,
                reserve_command_id,
                resolution_command_id,
                terminal_command_id,
                debit_operation_id,
                created_at,
                state_changed_at,
                version
            )
            VALUES
            (
                {id},
                {accountId},
                {printSourceId},
                {jobUuid},
                {amountMinorUnits},
                {(int)status},
                {reserveCommandId},
                {resolutionCommandId},
                {terminalCommandId},
                {debitOperationId},
                {SeedTime},
                {SeedTime},
                1
            )
            """);
    }

    private async Task<AccountState> ReadAccountStateAsync(
        Guid ownerId,
        Guid? printSourceId)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider
            .GetRequiredService<FuaPayDbContext>();

        return await dbContext.Database
            .SqlQuery<AccountState>(
                $"""
                SELECT
                    account.balance_minor_units AS "BalanceMinorUnits",
                    (
                        SELECT count(*)::integer
                        FROM credits.movements AS movement
                        WHERE movement.account_id = account.id
                    ) AS "MovementCount",
                    (
                        SELECT count(*)::integer
                        FROM credits.print_reservations AS reservation
                        WHERE reservation.credit_account_id = account.id
                          AND (
                              {printSourceId}::uuid IS NULL
                              OR reservation.print_source_id = {printSourceId}
                          )
                    ) AS "ReservationCount",
                    (
                        SELECT COALESCE(sum(reservation.amount_minor_units), 0)::bigint
                        FROM credits.print_reservations AS reservation
                        WHERE reservation.credit_account_id = account.id
                          AND reservation.status IN (1, 2)
                          AND (
                              {printSourceId}::uuid IS NULL
                              OR reservation.print_source_id = {printSourceId}
                          )
                    ) AS "ReservedMinorUnits"
                FROM credits.accounts AS account
                WHERE account.owner_id = {ownerId}
                """)
            .SingleAsync();
    }

    private async Task<LifecycleState> ReadLifecycleStateAsync(
        Guid ownerId)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider
            .GetRequiredService<FuaPayDbContext>();

        return await dbContext.Database
            .SqlQuery<LifecycleState>(
                $"""
                SELECT
                    account.balance_minor_units AS "BalanceMinorUnits",
                    (
                        SELECT count(*)::integer
                        FROM credits.movements AS movement
                        WHERE movement.account_id = account.id
                    ) AS "MovementCount",
                    (
                        SELECT count(*)::integer
                        FROM credits.movements AS movement
                        WHERE movement.account_id = account.id
                          AND movement.operation_id IN
                          (
                              SELECT reservation.debit_operation_id
                              FROM credits.print_reservations AS reservation
                              WHERE reservation.credit_account_id = account.id
                                AND reservation.debit_operation_id IS NOT NULL
                          )
                    ) AS "CaptureMovementCount",
                    (
                        SELECT count(*)::integer
                        FROM credits.print_reservations AS reservation
                        WHERE reservation.credit_account_id = account.id
                    ) AS "ReservationCount",
                    (
                        SELECT COALESCE(sum(reservation.amount_minor_units), 0)::bigint
                        FROM credits.print_reservations AS reservation
                        WHERE reservation.credit_account_id = account.id
                          AND reservation.status IN (1, 2)
                    ) AS "BlockingMinorUnits",
                    (
                        SELECT count(*)::integer
                        FROM audit.events AS audit
                        WHERE audit.entity_type = 'print-reservation'
                          AND audit.entity_id IN
                          (
                              SELECT reservation.id::text
                              FROM credits.print_reservations AS reservation
                              WHERE reservation.credit_account_id = account.id
                          )
                    ) AS "AuditCount"
                FROM credits.accounts AS account
                WHERE account.owner_id = {ownerId}
                """)
            .SingleAsync();
    }

    private async Task<int> CountReservationsAsync(Guid printSourceId)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider
            .GetRequiredService<FuaPayDbContext>();

        return await dbContext.Database.SqlQuery<int>(
            $"""
            SELECT count(*)::integer AS "Value"
            FROM credits.print_reservations
            WHERE print_source_id = {printSourceId}
            """)
            .SingleAsync();
    }

    private async Task<int> CountAuditActionAsync(string action)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider
            .GetRequiredService<FuaPayDbContext>();

        return await dbContext.Database.SqlQuery<int>(
            $"""
            SELECT count(*)::integer AS "Value"
            FROM audit.events
            WHERE action = {action}
            """)
            .SingleAsync();
    }

    private async Task<int> CountReservationAuditAsync(
        Guid reservationId,
        string action)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider
            .GetRequiredService<FuaPayDbContext>();

        return await dbContext.Database.SqlQuery<int>(
            $"""
            SELECT count(*)::integer AS "Value"
            FROM audit.events
            WHERE entity_type = 'print-reservation'
              AND entity_id = {reservationId.ToString()}
              AND action = {action}
            """)
            .SingleAsync();
    }

    private async Task DeleteScenarioAsync(Guid[] ownerIds)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider
            .GetRequiredService<FuaPayDbContext>();

        foreach (var ownerId in ownerIds)
        {
            _ = await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"""
                DELETE FROM audit.events
                WHERE entity_type = 'print-reservation'
                  AND entity_id IN
                  (
                      SELECT reservation.id::text
                      FROM credits.print_reservations AS reservation
                      JOIN credits.accounts AS account
                        ON account.id = reservation.credit_account_id
                      WHERE account.owner_id = {ownerId}
                  )
                """);
            _ = await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"""
                DELETE FROM credits.print_reservations
                WHERE credit_account_id IN
                (
                    SELECT id
                    FROM credits.accounts
                    WHERE owner_id = {ownerId}
                )
                """);
            _ = await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"""
                DELETE FROM credits.movements
                WHERE account_id IN
                (
                    SELECT id
                    FROM credits.accounts
                    WHERE owner_id = {ownerId}
                )
                """);
            _ = await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"""
                DELETE FROM credits.accounts
                WHERE owner_id = {ownerId}
                """);
        }
    }

    private static async Task<ReservationAttempt> CaptureAsync(
        Task<PrintReservationResult> operation)
    {
        try
        {
            return new ReservationAttempt(
                await operation,
                Exception: null);
        }
        catch (Exception exception)
        {
            return new ReservationAttempt(
                Result: null,
                exception);
        }
    }

    private sealed class ThrowingCreditSaveRepository :
        ICreditAccountRepository
    {
        public const string FailureMessage =
            "Injected failure during credit persistence.";

        private readonly ICreditAccountRepository _inner;

        public ThrowingCreditSaveRepository(
            ICreditAccountRepository inner)
        {
            _inner = inner;
        }

        public Task<CreditAccount?> FindByOwnerIdAsync(
            Guid ownerId,
            CancellationToken cancellationToken) =>
            _inner.FindByOwnerIdAsync(ownerId, cancellationToken);

        public Task<CreditAccount?> FindByOwnerIdForUpdateAsync(
            Guid ownerId,
            CancellationToken cancellationToken) =>
            _inner.FindByOwnerIdForUpdateAsync(ownerId, cancellationToken);

        public Task<CreditAccount?> FindByIdForUpdateAsync(
            Guid accountId,
            CancellationToken cancellationToken) =>
            _inner.FindByIdForUpdateAsync(accountId, cancellationToken);

        public Task LockOwnerForAccountCreationAsync(
            Guid ownerId,
            CancellationToken cancellationToken) =>
            _inner.LockOwnerForAccountCreationAsync(
                ownerId,
                cancellationToken);

        public Task AddAsync(
            CreditAccount account,
            CancellationToken cancellationToken) =>
            _inner.AddAsync(account, cancellationToken);

        public Task SaveAsync(
            CreditAccount account,
            CancellationToken cancellationToken) =>
            throw new TimeoutException(FailureMessage);
    }

    private sealed class ThrowingReservationSaveRepository :
        IPrintReservationRepository
    {
        public const string FailureMessage =
            "Injected failure during reservation persistence.";

        private readonly IPrintReservationRepository _inner;

        public ThrowingReservationSaveRepository(
            IPrintReservationRepository inner)
        {
            _inner = inner;
        }

        public Task<PrintReservationResult?> FindByIdAsync(
            Guid reservationId,
            CancellationToken cancellationToken) =>
            _inner.FindByIdAsync(reservationId, cancellationToken);

        public Task<PrintReservation?> FindByIdForUpdateAsync(
            Guid reservationId,
            CancellationToken cancellationToken) =>
            _inner.FindByIdForUpdateAsync(
                reservationId,
                cancellationToken);

        public Task<PrintReservationResult?> FindByReserveCommandAsync(
            Guid printSourceId,
            Guid reserveCommandId,
            CancellationToken cancellationToken) =>
            _inner.FindByReserveCommandAsync(
                printSourceId,
                reserveCommandId,
                cancellationToken);

        public Task<PrintReservationResult?> FindByPrintJobAsync(
            Guid printSourceId,
            string jobUuid,
            CancellationToken cancellationToken) =>
            _inner.FindByPrintJobAsync(
                printSourceId,
                jobUuid,
                cancellationToken);

        public Task<PrintReservationResult?> FindByResolutionCommandAsync(
            Guid printSourceId,
            Guid resolutionCommandId,
            CancellationToken cancellationToken) =>
            _inner.FindByResolutionCommandAsync(
                printSourceId,
                resolutionCommandId,
                cancellationToken);

        public Task<PrintReservationResult?> FindByTerminalCommandAsync(
            Guid printSourceId,
            Guid terminalCommandId,
            CancellationToken cancellationToken) =>
            _inner.FindByTerminalCommandAsync(
                printSourceId,
                terminalCommandId,
                cancellationToken);

        public Task<Money> GetBlockingAmountAsync(
            Guid creditAccountId,
            CancellationToken cancellationToken) =>
            _inner.GetBlockingAmountAsync(
                creditAccountId,
                cancellationToken);

        public Task AddAsync(
            PrintReservation reservation,
            CancellationToken cancellationToken) =>
            _inner.AddAsync(reservation, cancellationToken);

        public Task SaveAsync(
            PrintReservation reservation,
            CancellationToken cancellationToken) =>
            throw new TimeoutException(FailureMessage);
    }

    private sealed class DuplicateAuditTrail : IAuditTrail
    {
        private readonly IAuditTrail _inner;
        private readonly AuditEntry _duplicate;

        public DuplicateAuditTrail(
            IAuditTrail inner,
            AuditEntry duplicate)
        {
            _inner = inner;
            _duplicate = duplicate;
        }

        public void Stage(AuditEntry entry) =>
            _inner.Stage(_duplicate);

        public Task WriteAsync(
            AuditEntry entry,
            CancellationToken cancellationToken = default) =>
            _inner.WriteAsync(_duplicate, cancellationToken);
    }

    private sealed class AddBarrierRepository :
        IPrintReservationRepository
    {
        private readonly IPrintReservationRepository _inner;
        private readonly AsyncBarrier _barrier;

        public AddBarrierRepository(
            IPrintReservationRepository inner,
            AsyncBarrier barrier)
        {
            _inner = inner;
            _barrier = barrier;
        }

        public Task<PrintReservationResult?> FindByIdAsync(
            Guid reservationId,
            CancellationToken cancellationToken) =>
            _inner.FindByIdAsync(
                reservationId,
                cancellationToken);

        public Task<PrintReservation?> FindByIdForUpdateAsync(
            Guid reservationId,
            CancellationToken cancellationToken) =>
            _inner.FindByIdForUpdateAsync(
                reservationId,
                cancellationToken);

        public Task<PrintReservationResult?> FindByReserveCommandAsync(
            Guid printSourceId,
            Guid reserveCommandId,
            CancellationToken cancellationToken) =>
            _inner.FindByReserveCommandAsync(
                printSourceId,
                reserveCommandId,
                cancellationToken);

        public Task<PrintReservationResult?> FindByPrintJobAsync(
            Guid printSourceId,
            string jobUuid,
            CancellationToken cancellationToken) =>
            _inner.FindByPrintJobAsync(
                printSourceId,
                jobUuid,
                cancellationToken);

        public Task<PrintReservationResult?> FindByResolutionCommandAsync(
            Guid printSourceId,
            Guid resolutionCommandId,
            CancellationToken cancellationToken) =>
            _inner.FindByResolutionCommandAsync(
                printSourceId,
                resolutionCommandId,
                cancellationToken);

        public Task<PrintReservationResult?> FindByTerminalCommandAsync(
            Guid printSourceId,
            Guid terminalCommandId,
            CancellationToken cancellationToken) =>
            _inner.FindByTerminalCommandAsync(
                printSourceId,
                terminalCommandId,
                cancellationToken);

        public Task<Money> GetBlockingAmountAsync(
            Guid creditAccountId,
            CancellationToken cancellationToken) =>
            _inner.GetBlockingAmountAsync(
                creditAccountId,
                cancellationToken);

        public async Task AddAsync(
            PrintReservation reservation,
            CancellationToken cancellationToken)
        {
            await _barrier.SignalAndWaitAsync(cancellationToken);
            await _inner.AddAsync(reservation, cancellationToken);
        }

        public async Task SaveAsync(
            PrintReservation reservation,
            CancellationToken cancellationToken)
        {
            await _barrier.SignalAndWaitAsync(cancellationToken);
            await _inner.SaveAsync(reservation, cancellationToken);
        }
    }

    private sealed class AsyncBarrier
    {
        private readonly int _participantCount;
        private readonly TaskCompletionSource _release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _arrivals;

        public AsyncBarrier(int participantCount)
        {
            _participantCount = participantCount;
        }

        public async Task SignalAndWaitAsync(
            CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _arrivals) == _participantCount)
            {
                _release.SetResult();
            }

            await _release.Task.WaitAsync(cancellationToken);
        }
    }

    private sealed class AccountLockGate
    {
        private readonly TaskCompletionSource _firstLockAcquired =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _secondLockAttempted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _releaseFirstLock =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _attemptCount;

        public Task FirstLockAcquired => _firstLockAcquired.Task;

        public Task SecondLockAttempted => _secondLockAttempted.Task;

        public int NextAttempt() =>
            Interlocked.Increment(ref _attemptCount);

        public void FirstAcquired() =>
            _firstLockAcquired.TrySetResult();

        public void SecondAttempted() =>
            _secondLockAttempted.TrySetResult();

        public Task WaitForFirstReleaseAsync() =>
            _releaseFirstLock.Task;

        public void ReleaseFirstLock() =>
            _releaseFirstLock.TrySetResult();
    }

    private sealed class CoordinatingCreditAccountRepository :
        ICreditAccountRepository
    {
        private readonly ICreditAccountRepository _inner;
        private readonly Guid _ownerId;
        private readonly Guid? _accountId;
        private readonly AccountLockGate _gate;

        public CoordinatingCreditAccountRepository(
            ICreditAccountRepository inner,
            Guid ownerId,
            Guid? accountId,
            AccountLockGate gate)
        {
            _inner = inner;
            _ownerId = ownerId;
            _accountId = accountId;
            _gate = gate;
        }

        public Task<CreditAccount?> FindByOwnerIdAsync(
            Guid ownerId,
            CancellationToken cancellationToken) =>
            _inner.FindByOwnerIdAsync(ownerId, cancellationToken);

        public Task<CreditAccount?> FindByOwnerIdForUpdateAsync(
            Guid ownerId,
            CancellationToken cancellationToken) =>
            ownerId == _ownerId
                ? CoordinateAsync(
                    () => _inner.FindByOwnerIdForUpdateAsync(
                        ownerId,
                        cancellationToken))
                : _inner.FindByOwnerIdForUpdateAsync(
                    ownerId,
                    cancellationToken);

        public Task<CreditAccount?> FindByIdForUpdateAsync(
            Guid accountId,
            CancellationToken cancellationToken) =>
            accountId == _accountId
                ? CoordinateAsync(
                    () => _inner.FindByIdForUpdateAsync(
                        accountId,
                        cancellationToken))
                : _inner.FindByIdForUpdateAsync(
                    accountId,
                    cancellationToken);

        public Task LockOwnerForAccountCreationAsync(
            Guid ownerId,
            CancellationToken cancellationToken) =>
            _inner.LockOwnerForAccountCreationAsync(
                ownerId,
                cancellationToken);

        public Task AddAsync(
            CreditAccount account,
            CancellationToken cancellationToken) =>
            _inner.AddAsync(account, cancellationToken);

        public Task SaveAsync(
            CreditAccount account,
            CancellationToken cancellationToken) =>
            _inner.SaveAsync(account, cancellationToken);

        private async Task<CreditAccount?> CoordinateAsync(
            Func<Task<CreditAccount?>> acquireLock)
        {
            var attempt = _gate.NextAttempt();

            if (attempt == 2)
            {
                _gate.SecondAttempted();
            }

            var account = await acquireLock();

            if (attempt == 1)
            {
                _gate.FirstAcquired();
                await _gate.WaitForFirstReleaseAsync();
            }

            return account;
        }
    }

    private sealed record ReservationAttempt(
        PrintReservationResult? Result,
        Exception? Exception);

    private sealed record AccountState(
        long BalanceMinorUnits,
        int MovementCount,
        int ReservationCount,
        long ReservedMinorUnits);

    private sealed record LifecycleState(
        long BalanceMinorUnits,
        int MovementCount,
        int CaptureMovementCount,
        int ReservationCount,
        long BlockingMinorUnits,
        int AuditCount);
}
