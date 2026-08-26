using FuaPay.Web.BuildingBlocks.Application;
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
        }
        finally
        {
            await DeleteScenarioAsync([ownerId]);
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
            services.GetRequiredService<TimeProvider>());
    }

    private WebApplicationFactory<Program> CreateCoordinatedFactory(
        Guid ownerId,
        AccountLockGate gate)
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

    private async Task DeleteScenarioAsync(Guid[] ownerIds)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider
            .GetRequiredService<FuaPayDbContext>();

        foreach (var ownerId in ownerIds)
        {
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
        private readonly AccountLockGate _gate;

        public CoordinatingCreditAccountRepository(
            ICreditAccountRepository inner,
            Guid ownerId,
            AccountLockGate gate)
        {
            _inner = inner;
            _ownerId = ownerId;
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
}
