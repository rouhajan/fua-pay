using FuaPay.Web.BuildingBlocks.Application;
using FuaPay.Web.BuildingBlocks.Auditing;
using FuaPay.Web.BuildingBlocks.Domain;
using FuaPay.Web.BuildingBlocks.Persistence;
using FuaPay.Web.Modules.Credits.Application;
using FuaPay.Web.Modules.Credits.Domain;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace FuaPay.DatabaseTests;

public sealed class CreditReturnHoldPersistenceTests :
    IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly DateTimeOffset TestTime =
        new(2026, 8, 28, 15, 0, 0, TimeSpan.Zero);

    private readonly WebApplicationFactory<Program> _factory;

    public CreditReturnHoldPersistenceTests(
        WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Migration_CreatesProtectedReturnHoldTable()
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider
            .GetRequiredService<FuaPayDbContext>();

        var tableExists = await dbContext.Database.SqlQuery<bool>(
            $"""
            SELECT to_regclass('credits.return_holds') IS NOT NULL AS "Value"
            """)
            .SingleAsync();
        var constraints = await dbContext.Database.SqlQuery<string>(
            $"""
            SELECT constraint_name AS "Value"
            FROM information_schema.table_constraints
            WHERE table_schema = 'credits'
              AND table_name = 'return_holds'
            """)
            .ToListAsync();
        var indexes = await dbContext.Database.SqlQuery<string>(
            $"""
            SELECT indexname AS "Value"
            FROM pg_indexes
            WHERE schemaname = 'credits'
              AND tablename = 'return_holds'
            """)
            .ToListAsync();

        Assert.True(tableExists);
        string[] expectedConstraints =
        [
            "pk_credits_return_holds",
            "fk_credits_return_holds_account",
            "fk_credits_return_holds_settlement_return",
            "ck_credits_return_holds_return_not_empty",
            "ck_credits_return_holds_account_not_empty",
            "ck_credits_return_holds_amount_positive",
            "ck_credits_return_holds_state_valid",
            "ck_credits_return_holds_timestamps_ordered",
            "ck_credits_return_holds_version_positive"
        ];
        Assert.All(
            expectedConstraints,
            expected => Assert.Contains(expected, constraints));
        Assert.Contains(
            "ix_credits_return_holds_account_state",
            indexes);
    }

    [Fact]
    public async Task Availability_AggregatesActiveHoldsAndExcludesTerminalHolds()
    {
        using var scope = _factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        var dbContext = services.GetRequiredService<FuaPayDbContext>();
        await using var transaction =
            await dbContext.Database.BeginTransactionAsync();
        var ownerId = Guid.NewGuid();
        var firstReturnId = Guid.NewGuid();
        var secondReturnId = Guid.NewGuid();

        await CreateAccountAsync(services, ownerId, balance: 1_000);
        await InsertSettlementReturnAsync(dbContext, firstReturnId);
        await InsertSettlementReturnAsync(dbContext, secondReturnId);
        var service = services.GetRequiredService<CreditReturnHoldService>();
        await service.CreateAsync(
            CreateHoldCommand(firstReturnId, ownerId, amount: 200));
        await service.CreateAsync(
            CreateHoldCommand(secondReturnId, ownerId, amount: 300));

        Assert.Equal(
            new Money(500),
            await ReadAvailableAsync(services, ownerId));

        await TransitionHoldAsync(
            services,
            ownerId,
            firstReturnId,
            consume: true);
        Assert.Equal(
            new Money(700),
            await ReadAvailableAsync(services, ownerId));

        await TransitionHoldAsync(
            services,
            ownerId,
            secondReturnId,
            consume: false);
        Assert.Equal(
            new Money(1_000),
            await ReadAvailableAsync(services, ownerId));

        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task Availability_AggregatesPrintStatesAndActiveReturnHold()
    {
        using var scope = _factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        var dbContext = services.GetRequiredService<FuaPayDbContext>();
        await using var transaction =
            await dbContext.Database.BeginTransactionAsync();
        var ownerId = Guid.NewGuid();
        var returnId = Guid.NewGuid();
        var printService = services
            .GetRequiredService<PrintReservationService>();

        await CreateAccountAsync(services, ownerId, balance: 2_000);
        await InsertSettlementReturnAsync(dbContext, returnId);
        await services.GetRequiredService<CreditReturnHoldService>()
            .CreateAsync(CreateHoldCommand(returnId, ownerId, amount: 300));

        var resolutionReservation = await printService.ReserveAsync(
            CreateReserveCommand(ownerId, amount: 400));
        await printService.RequireResolutionAsync(
            new RequirePrintReservationResolutionCommand(
                resolutionReservation.Id,
                resolutionReservation.PrintSourceId,
                Guid.NewGuid()));
        Assert.Equal(
            new Money(1_300),
            await ReadAvailableAsync(services, ownerId));

        var released = await printService.ReserveAsync(
            CreateReserveCommand(ownerId, amount: 200));
        await printService.ReleaseAsync(
            new ReleasePrintReservationCommand(
                released.Id,
                released.PrintSourceId,
                Guid.NewGuid()));
        Assert.Equal(
            new Money(1_300),
            await ReadAvailableAsync(services, ownerId));

        var captured = await printService.ReserveAsync(
            CreateReserveCommand(ownerId, amount: 100));
        await printService.CaptureAsync(
            new CapturePrintReservationCommand(
                captured.Id,
                captured.PrintSourceId,
                Guid.NewGuid()));
        Assert.Equal(
            new Money(1_200),
            await ReadAvailableAsync(services, ownerId));

        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task DebitAndPrintReserve_RespectActiveReturnHold()
    {
        using var scope = _factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        var dbContext = services.GetRequiredService<FuaPayDbContext>();
        await using var transaction =
            await dbContext.Database.BeginTransactionAsync();
        var ownerId = Guid.NewGuid();
        var returnId = Guid.NewGuid();

        await CreateAccountAsync(services, ownerId, balance: 1_000);
        await InsertSettlementReturnAsync(dbContext, returnId);
        await services.GetRequiredService<CreditReturnHoldService>()
            .CreateAsync(CreateHoldCommand(returnId, ownerId, amount: 700));

        await Assert.ThrowsAsync<InsufficientCreditException>(
            () => services.GetRequiredService<CreditService>().DebitAsync(
                ownerId,
                Guid.NewGuid(),
                new Money(301),
                "Debit blocked by return hold"));
        await Assert.ThrowsAsync<InsufficientAvailablePrintCreditException>(
            () => services.GetRequiredService<PrintReservationService>()
                .ReserveAsync(CreateReserveCommand(ownerId, amount: 301)));

        var state = await ReadStateAsync(services, ownerId);
        Assert.Equal(1_000, state.BalanceMinorUnits);
        Assert.Equal(1, state.MovementCount);
        Assert.Equal(700, state.ActiveHoldMinorUnits);
        Assert.Equal(0, state.ActivePrintMinorUnits);
        Assert.Equal(300, state.AvailableMinorUnits);

        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task PrintCapture_ExcludesOnlyItsOwnReservation()
    {
        using var scope = _factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        var dbContext = services.GetRequiredService<FuaPayDbContext>();
        await using var transaction =
            await dbContext.Database.BeginTransactionAsync();
        var ownerId = Guid.NewGuid();
        var returnId = Guid.NewGuid();
        var printService = services
            .GetRequiredService<PrintReservationService>();

        await CreateAccountAsync(services, ownerId, balance: 1_000);
        await InsertSettlementReturnAsync(dbContext, returnId);
        var ownReservation = await printService.ReserveAsync(
            CreateReserveCommand(ownerId, amount: 600));
        _ = await printService.ReserveAsync(
            CreateReserveCommand(ownerId, amount: 100));
        await services.GetRequiredService<CreditReturnHoldService>()
            .CreateAsync(CreateHoldCommand(returnId, ownerId, amount: 300));

        var captured = await printService.CaptureAsync(
            new CapturePrintReservationCommand(
                ownReservation.Id,
                ownReservation.PrintSourceId,
                Guid.NewGuid()));

        Assert.Equal(PrintReservationStatus.Captured, captured.Status);
        var state = await ReadStateAsync(services, ownerId);
        Assert.Equal(400, state.BalanceMinorUnits);
        Assert.Equal(2, state.MovementCount);
        Assert.Equal(300, state.ActiveHoldMinorUnits);
        Assert.Equal(100, state.ActivePrintMinorUnits);
        Assert.Equal(0, state.AvailableMinorUnits);

        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task HoldCreation_IsFullOnlyIdempotentAndConflictSafe()
    {
        using var scope = _factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        var dbContext = services.GetRequiredService<FuaPayDbContext>();
        await using var transaction =
            await dbContext.Database.BeginTransactionAsync();
        var ownerId = Guid.NewGuid();
        var returnId = Guid.NewGuid();

        await CreateAccountAsync(services, ownerId, balance: 1_000);
        await InsertSettlementReturnAsync(dbContext, returnId);
        _ = await services.GetRequiredService<PrintReservationService>()
            .ReserveAsync(CreateReserveCommand(ownerId, amount: 600));
        var holdService = services
            .GetRequiredService<CreditReturnHoldService>();

        await Assert.ThrowsAsync<
            InsufficientAvailableCreditForReturnHoldException>(
            () => holdService.CreateAsync(
                CreateHoldCommand(returnId, ownerId, amount: 401)));
        Assert.Equal(0, await CountHoldsAsync(dbContext, returnId));

        var command = CreateHoldCommand(returnId, ownerId, amount: 400);
        var created = await holdService.CreateAsync(command);
        var replayed = await holdService.CreateAsync(command);
        Assert.True(created.Created);
        Assert.False(replayed.Created);
        Assert.Equal(1, await CountHoldsAsync(dbContext, returnId));
        Assert.Equal(new Money(400), replayed.Hold.Amount);

        await Assert.ThrowsAsync<CreditReturnHoldConflictException>(
            () => holdService.CreateAsync(
                CreateHoldCommand(returnId, ownerId, amount: 399)));
        Assert.Equal(1, await CountHoldsAsync(dbContext, returnId));

        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task HoldSave_WhenVersionChanged_RejectsOptimisticOverwrite()
    {
        using var scope = _factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        var dbContext = services.GetRequiredService<FuaPayDbContext>();
        await using var transaction =
            await dbContext.Database.BeginTransactionAsync();
        var ownerId = Guid.NewGuid();
        var returnId = Guid.NewGuid();

        await CreateAccountAsync(services, ownerId, balance: 1_000);
        await InsertSettlementReturnAsync(dbContext, returnId);
        await services.GetRequiredService<CreditReturnHoldService>()
            .CreateAsync(CreateHoldCommand(returnId, ownerId, amount: 400));
        var accounts = services
            .GetRequiredService<ICreditAccountRepository>();
        var holds = services.GetRequiredService<ICreditReturnHoldRepository>();
        _ = await accounts.FindByOwnerIdForUpdateAsync(
            ownerId,
            CancellationToken.None);
        var hold = Assert.IsType<CreditReturnHold>(
            await holds.FindBySettlementReturnIdForUpdateAsync(
                returnId,
                CancellationToken.None));
        _ = await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE credits.return_holds
            SET version = version + 1
            WHERE settlement_return_id = {returnId}
            """);
        hold.Release(hold.StateChangedAt.AddMinutes(1));

        await Assert.ThrowsAsync<CreditReturnHoldConcurrencyException>(
            () => holds.SaveAsync(hold));

        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task ConcurrentHoldAndDebit_SerializeWithoutOversubscription()
    {
        var scenario = await CreateCommittedScenarioAsync(balance: 1_000);

        try
        {
            using var holdScope = _factory.Services.CreateScope();
            var holdServices = holdScope.ServiceProvider;
            var holdDb = holdServices.GetRequiredService<FuaPayDbContext>();
            await using var holdTransaction =
                await holdDb.Database.BeginTransactionAsync();
            _ = await holdServices
                .GetRequiredService<ICreditAccountRepository>()
                .FindByOwnerIdForUpdateAsync(
                    scenario.OwnerId,
                    CancellationToken.None);

            using var spendScope = _factory.Services.CreateScope();
            var attempt = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var spendServices = spendScope.ServiceProvider;
            var debitService = new CreditService(
                new SignalingCreditAccountRepository(
                    spendServices.GetRequiredService<
                        ICreditAccountRepository>(),
                    attempt),
                spendServices.GetRequiredService<CreditAvailabilityService>(),
                spendServices.GetRequiredService<IApplicationTransaction>(),
                spendServices.GetRequiredService<TimeProvider>());
            var debitTask = CaptureExceptionAsync(
                debitService.DebitAsync(
                    scenario.OwnerId,
                    Guid.NewGuid(),
                    new Money(400),
                    "Concurrent debit"));
            await attempt.Task;

            await holdServices.GetRequiredService<CreditReturnHoldService>()
                .CreateAsync(
                    CreateHoldCommand(
                        scenario.ReturnId,
                        scenario.OwnerId,
                        amount: 700));
            await holdTransaction.CommitAsync();
            var debitException = await debitTask;

            Assert.IsType<InsufficientCreditException>(debitException);
            await AssertStateAsync(
                scenario.OwnerId,
                balance: 1_000,
                movementCount: 1,
                activeHolds: 700,
                activePrint: 0,
                available: 300);
        }
        finally
        {
            await DeleteScenarioAsync(scenario.OwnerId, scenario.ReturnId);
        }
    }

    [Fact]
    public async Task ConcurrentHoldAndPrintReserve_SerializeWithoutOversubscription()
    {
        var scenario = await CreateCommittedScenarioAsync(balance: 1_000);

        try
        {
            using var holdScope = _factory.Services.CreateScope();
            var holdServices = holdScope.ServiceProvider;
            var holdDb = holdServices.GetRequiredService<FuaPayDbContext>();
            await using var holdTransaction =
                await holdDb.Database.BeginTransactionAsync();
            _ = await holdServices
                .GetRequiredService<ICreditAccountRepository>()
                .FindByOwnerIdForUpdateAsync(
                    scenario.OwnerId,
                    CancellationToken.None);

            using var printScope = _factory.Services.CreateScope();
            var attempt = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var printServices = printScope.ServiceProvider;
            var printService = CreateSignalingPrintService(
                printServices,
                attempt);
            var reserveTask = CaptureExceptionAsync(
                printService.ReserveAsync(
                    CreateReserveCommand(scenario.OwnerId, amount: 400)));
            await attempt.Task;

            await holdServices.GetRequiredService<CreditReturnHoldService>()
                .CreateAsync(
                    CreateHoldCommand(
                        scenario.ReturnId,
                        scenario.OwnerId,
                        amount: 700));
            await holdTransaction.CommitAsync();
            var reserveException = await reserveTask;

            Assert.IsType<InsufficientAvailablePrintCreditException>(
                reserveException);
            await AssertStateAsync(
                scenario.OwnerId,
                balance: 1_000,
                movementCount: 1,
                activeHolds: 700,
                activePrint: 0,
                available: 300);
        }
        finally
        {
            await DeleteScenarioAsync(scenario.OwnerId, scenario.ReturnId);
        }
    }

    [Fact]
    public async Task ConcurrentHoldAndPrintCapture_AreDeadlockFreeAndValid()
    {
        var scenario = await CreateCommittedScenarioAsync(balance: 1_000);

        try
        {
            PrintReservationResult reservation;
            using (var seedScope = _factory.Services.CreateScope())
            {
                reservation = await seedScope.ServiceProvider
                    .GetRequiredService<PrintReservationService>()
                    .ReserveAsync(
                        CreateReserveCommand(
                            scenario.OwnerId,
                            amount: 600));
            }

            using var holdScope = _factory.Services.CreateScope();
            var holdServices = holdScope.ServiceProvider;
            var holdDb = holdServices.GetRequiredService<FuaPayDbContext>();
            await using var holdTransaction =
                await holdDb.Database.BeginTransactionAsync();
            _ = await holdServices
                .GetRequiredService<ICreditAccountRepository>()
                .FindByOwnerIdForUpdateAsync(
                    scenario.OwnerId,
                    CancellationToken.None);

            using var printScope = _factory.Services.CreateScope();
            var attempt = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var printServices = printScope.ServiceProvider;
            var printService = CreateSignalingPrintService(
                printServices,
                attempt);
            var captureTask = printService.CaptureAsync(
                new CapturePrintReservationCommand(
                    reservation.Id,
                    reservation.PrintSourceId,
                    Guid.NewGuid()));
            await attempt.Task;

            await holdServices.GetRequiredService<CreditReturnHoldService>()
                .CreateAsync(
                    CreateHoldCommand(
                        scenario.ReturnId,
                        scenario.OwnerId,
                        amount: 400));
            await holdTransaction.CommitAsync();
            var captured = await captureTask;

            Assert.Equal(PrintReservationStatus.Captured, captured.Status);
            await AssertStateAsync(
                scenario.OwnerId,
                balance: 400,
                movementCount: 2,
                activeHolds: 400,
                activePrint: 0,
                available: 0);
        }
        finally
        {
            await DeleteScenarioAsync(scenario.OwnerId, scenario.ReturnId);
        }
    }

    private static async Task CreateAccountAsync(
        IServiceProvider services,
        Guid ownerId,
        long balance)
    {
        _ = await services.GetRequiredService<CreditService>().CreditAsync(
            ownerId,
            Guid.NewGuid(),
            new Money(balance),
            "Return hold test credit");
    }

    private static async Task<Money> ReadAvailableAsync(
        IServiceProvider services,
        Guid ownerId)
    {
        var account = Assert.IsType<CreditAccount>(
            await services.GetRequiredService<ICreditAccountRepository>()
                .FindByOwnerIdAsync(ownerId, CancellationToken.None));
        return await services.GetRequiredService<CreditAvailabilityService>()
            .GetAvailableAsync(account);
    }

    private static async Task TransitionHoldAsync(
        IServiceProvider services,
        Guid ownerId,
        Guid returnId,
        bool consume)
    {
        _ = await services.GetRequiredService<ICreditAccountRepository>()
            .FindByOwnerIdForUpdateAsync(ownerId, CancellationToken.None);
        var repository = services
            .GetRequiredService<ICreditReturnHoldRepository>();
        var hold = Assert.IsType<CreditReturnHold>(
            await repository.FindBySettlementReturnIdForUpdateAsync(
                returnId,
                CancellationToken.None));

        if (consume)
        {
            hold.Consume(hold.StateChangedAt.AddMinutes(1));
        }
        else
        {
            hold.Release(hold.StateChangedAt.AddMinutes(1));
        }

        await repository.SaveAsync(hold);
    }

    private static CreateCreditReturnHoldCommand CreateHoldCommand(
        Guid returnId,
        Guid ownerId,
        long amount) =>
        new(returnId, ownerId, new Money(amount));

    private static ReservePrintCreditCommand CreateReserveCommand(
        Guid ownerId,
        long amount) =>
        new(
            ownerId,
            Guid.NewGuid(),
            $"urn:uuid:{Guid.NewGuid():D}",
            new Money(amount),
            Guid.NewGuid());

    private static Task<int> CountHoldsAsync(
        FuaPayDbContext dbContext,
        Guid returnId) =>
        dbContext.Database.SqlQuery<int>(
            $"""
            SELECT count(*)::integer AS "Value"
            FROM credits.return_holds
            WHERE settlement_return_id = {returnId}
            """)
            .SingleAsync();

    private static Task<int> InsertSettlementReturnAsync(
        FuaPayDbContext dbContext,
        Guid returnId)
    {
        var requestId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var administratorId = Guid.NewGuid();

        return dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO payments.settlement_returns
            (
                id,
                request_id,
                kind,
                original_payment_id,
                job_id,
                customer_user_id,
                administrator_user_id,
                amount_minor_units,
                currency,
                reason,
                state,
                requested_at,
                started_at,
                updated_at,
                completed_at,
                version
            )
            VALUES
            (
                {returnId},
                {requestId},
                2,
                NULL,
                {jobId},
                {customerId},
                {administratorId},
                1000,
                'CZK',
                'Return hold persistence test',
                1,
                {TestTime},
                NULL,
                {TestTime},
                NULL,
                1
            )
            """);
    }

    private async Task<CommittedScenario> CreateCommittedScenarioAsync(
        long balance)
    {
        using var scope = _factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        var ownerId = Guid.NewGuid();
        var returnId = Guid.NewGuid();
        await CreateAccountAsync(services, ownerId, balance);
        await InsertSettlementReturnAsync(
            services.GetRequiredService<FuaPayDbContext>(),
            returnId);
        return new CommittedScenario(ownerId, returnId);
    }

    private static PrintReservationService CreateSignalingPrintService(
        IServiceProvider services,
        TaskCompletionSource attempt) =>
        new(
            new SignalingCreditAccountRepository(
                services.GetRequiredService<ICreditAccountRepository>(),
                attempt),
            services.GetRequiredService<IPrintReservationRepository>(),
            services.GetRequiredService<CreditAvailabilityService>(),
            services.GetRequiredService<IApplicationTransaction>(),
            services.GetRequiredService<IAuditTrail>(),
            services.GetRequiredService<TimeProvider>());

    private async Task AssertStateAsync(
        Guid ownerId,
        long balance,
        int movementCount,
        long activeHolds,
        long activePrint,
        long available)
    {
        using var scope = _factory.Services.CreateScope();
        var state = await ReadStateAsync(scope.ServiceProvider, ownerId);
        Assert.Equal(balance, state.BalanceMinorUnits);
        Assert.Equal(movementCount, state.MovementCount);
        Assert.Equal(activeHolds, state.ActiveHoldMinorUnits);
        Assert.Equal(activePrint, state.ActivePrintMinorUnits);
        Assert.Equal(available, state.AvailableMinorUnits);
    }

    private static async Task<FinancialState> ReadStateAsync(
        IServiceProvider services,
        Guid ownerId)
    {
        var dbContext = services.GetRequiredService<FuaPayDbContext>();
        return await dbContext.Database.SqlQuery<FinancialState>(
            $"""
            SELECT
                account.balance_minor_units AS "BalanceMinorUnits",
                (
                    SELECT count(*)::integer
                    FROM credits.movements AS movement
                    WHERE movement.account_id = account.id
                ) AS "MovementCount",
                (
                    SELECT COALESCE(sum(hold.amount_minor_units), 0)::bigint
                    FROM credits.return_holds AS hold
                    WHERE hold.credit_account_id = account.id
                      AND hold.state = 1
                ) AS "ActiveHoldMinorUnits",
                (
                    SELECT COALESCE(sum(reservation.amount_minor_units), 0)::bigint
                    FROM credits.print_reservations AS reservation
                    WHERE reservation.credit_account_id = account.id
                      AND reservation.status IN (1, 2)
                ) AS "ActivePrintMinorUnits",
                account.balance_minor_units
                    - (
                        SELECT COALESCE(sum(hold.amount_minor_units), 0)::bigint
                        FROM credits.return_holds AS hold
                        WHERE hold.credit_account_id = account.id
                          AND hold.state = 1
                    )
                    - (
                        SELECT COALESCE(sum(reservation.amount_minor_units), 0)::bigint
                        FROM credits.print_reservations AS reservation
                        WHERE reservation.credit_account_id = account.id
                          AND reservation.status IN (1, 2)
                    ) AS "AvailableMinorUnits"
            FROM credits.accounts AS account
            WHERE account.owner_id = {ownerId}
            """)
            .SingleAsync();
    }

    private async Task DeleteScenarioAsync(
        Guid ownerId,
        Guid returnId)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider
            .GetRequiredService<FuaPayDbContext>();
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
                SELECT id FROM credits.accounts WHERE owner_id = {ownerId}
            )
            """);
        _ = await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            DELETE FROM credits.return_holds
            WHERE settlement_return_id = {returnId}
            """);
        _ = await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            DELETE FROM payments.settlement_returns
            WHERE id = {returnId}
            """);
        _ = await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            DELETE FROM credits.movements
            WHERE account_id IN
            (
                SELECT id FROM credits.accounts WHERE owner_id = {ownerId}
            )
            """);
        _ = await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM credits.accounts WHERE owner_id = {ownerId}");
    }

    private static async Task<Exception?> CaptureExceptionAsync<T>(
        Task<T> operation)
    {
        try
        {
            _ = await operation;
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private sealed class SignalingCreditAccountRepository :
        ICreditAccountRepository
    {
        private readonly ICreditAccountRepository _inner;
        private readonly TaskCompletionSource _attempt;

        public SignalingCreditAccountRepository(
            ICreditAccountRepository inner,
            TaskCompletionSource attempt)
        {
            _inner = inner;
            _attempt = attempt;
        }

        public Task<CreditAccount?> FindByOwnerIdAsync(
            Guid ownerId,
            CancellationToken cancellationToken) =>
            _inner.FindByOwnerIdAsync(ownerId, cancellationToken);

        public Task<CreditAccount?> FindByOwnerIdForUpdateAsync(
            Guid ownerId,
            CancellationToken cancellationToken)
        {
            _attempt.TrySetResult();
            return _inner.FindByOwnerIdForUpdateAsync(
                ownerId,
                cancellationToken);
        }

        public Task<CreditAccount?> FindByIdForUpdateAsync(
            Guid accountId,
            CancellationToken cancellationToken)
        {
            _attempt.TrySetResult();
            return _inner.FindByIdForUpdateAsync(
                accountId,
                cancellationToken);
        }

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
    }

    private sealed record CommittedScenario(
        Guid OwnerId,
        Guid ReturnId);

    private sealed record FinancialState(
        long BalanceMinorUnits,
        int MovementCount,
        long ActiveHoldMinorUnits,
        long ActivePrintMinorUnits,
        long AvailableMinorUnits);
}
