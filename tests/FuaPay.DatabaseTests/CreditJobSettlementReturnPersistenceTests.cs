using FuaPay.Web.BuildingBlocks.Application;
using FuaPay.Web.BuildingBlocks.Auditing;
using FuaPay.Web.BuildingBlocks.Domain;
using FuaPay.Web.BuildingBlocks.Notifications;
using FuaPay.Web.BuildingBlocks.Persistence;
using FuaPay.Web.Modules.Credits.Application;
using FuaPay.Web.Modules.Credits.Domain;
using FuaPay.Web.Modules.Jobs.Application;
using FuaPay.Web.Modules.Jobs.Domain;
using FuaPay.Web.Modules.Payments.Application;
using FuaPay.Web.Modules.Payments.Domain;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FuaPay.DatabaseTests;

public sealed class CreditJobSettlementReturnPersistenceTests :
    IClassFixture<WebApplicationFactory<Program>>
{
    private const long InitialBalanceMinorUnits = 20_000;
    private const long JobPriceMinorUnits = 12_500;
    private const string ReturnAction =
        "settlement-return.credit-job.completed";
    private const string ReturnReason =
        "Full return approved by an administrator";

    private static readonly DateTimeOffset TestTime =
        new(2026, 8, 28, 8, 0, 0, TimeSpan.Zero);

    private static int _jobNumberSequence =
        Random.Shared.Next(100_000, 900_000);

    private readonly WebApplicationFactory<Program> _factory;

    public CreditJobSettlementReturnPersistenceTests(
        WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ReturnAsync_CommitsFullReturnAndReplaysExactlyOnce()
    {
        using var scope = _factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        var dbContext = services.GetRequiredService<FuaPayDbContext>();

        await using var transaction =
            await dbContext.Database.BeginTransactionAsync();

        try
        {
            var scenario = await SeedPaidCreditJobAsync(
                services,
                completeProduction: true);
            var administratorUserId = Guid.NewGuid();
            var requestId = Guid.NewGuid();
            var command = new CreditJobSettlementReturnCommand(
                requestId,
                scenario.JobId,
                administratorUserId,
                ReturnReason);
            var creditQueries =
                services.GetRequiredService<ICreditQueries>();
            var jobRepository =
                services.GetRequiredService<IJobRepository>();
            var returnRepository =
                services.GetRequiredService<ISettlementReturnRepository>();
            var jobQueries = services.GetRequiredService<IJobQueries>();
            var service = CreateReturnService(
                services,
                new FixedTimeProvider(TestTime.AddMinutes(7)));

            var accountBefore = Assert.IsType<CreditAccountSummary>(
                await creditQueries.FindAccountForOwnerAsync(
                    scenario.CustomerUserId));
            var originalDebitBefore =
                Assert.IsType<CreditMovementListItem>(
                    await creditQueries.FindMovementForOwnerAsync(
                        scenario.CustomerUserId,
                        scenario.JobId));
            var jobBefore = Assert.IsType<Job>(
                await jobRepository.FindByIdAsync(
                    scenario.JobId,
                    CancellationToken.None));
            var jobSnapshot = JobSnapshot.Create(jobBefore);
            var jobVersionBefore = await FindJobVersionAsync(
                dbContext,
                scenario.JobId);

            var first = await service.ReturnAsync(command);

            Assert.True(first.Created);
            Assert.Equal(
                SettlementReturnState.Completed,
                first.SettlementReturn.State);
            Assert.Equal(
                SettlementReturnKind.CreditJob,
                first.SettlementReturn.Kind);
            Assert.Null(first.SettlementReturn.OriginalPaymentId);
            Assert.Equal(
                scenario.JobId,
                first.SettlementReturn.JobId);
            Assert.Equal(
                scenario.CustomerUserId,
                first.SettlementReturn.CustomerUserId);
            Assert.Equal(
                administratorUserId,
                first.SettlementReturn.AdministratorUserId);
            Assert.Equal(
                new Money(JobPriceMinorUnits),
                first.SettlementReturn.Amount);
            Assert.Equal(ReturnReason, first.SettlementReturn.Reason);
            Assert.NotNull(first.SettlementReturn.StartedAt);
            Assert.NotNull(first.SettlementReturn.CompletedAt);

            var movementsAfterFirst =
                await creditQueries.ListMovementsForOwnerAsync(
                    scenario.CustomerUserId,
                    new CreditMovementPageRequest(limit: 100));
            var accountAfterFirst = Assert.IsType<CreditAccountSummary>(
                await creditQueries.FindAccountForOwnerAsync(
                    scenario.CustomerUserId));
            var auditCountAfterFirst = await CountReturnAuditsAsync(
                dbContext,
                first.SettlementReturn.Id);

            var replay = await service.ReturnAsync(command);

            Assert.False(replay.Created);
            Assert.Equal(
                first.SettlementReturn.Id,
                replay.SettlementReturn.Id);

            var persistedReturn = Assert.IsType<SettlementReturn>(
                await returnRepository.FindByJobIdAsync(
                    scenario.JobId));
            var movementsAfterReplay =
                await creditQueries.ListMovementsForOwnerAsync(
                    scenario.CustomerUserId,
                    new CreditMovementPageRequest(limit: 100));
            var accountAfterReplay = Assert.IsType<CreditAccountSummary>(
                await creditQueries.FindAccountForOwnerAsync(
                    scenario.CustomerUserId));
            var originalDebitAfter =
                Assert.IsType<CreditMovementListItem>(
                    await creditQueries.FindMovementForOwnerAsync(
                        scenario.CustomerUserId,
                        scenario.JobId));
            var compensation = Assert.Single(
                movementsAfterReplay.Items,
                movement =>
                    movement.OperationId == persistedReturn.Id);

            Assert.Equal(originalDebitBefore, originalDebitAfter);
            Assert.Equal(CreditMovementType.Debit, originalDebitAfter.Type);
            Assert.Equal(
                JobPriceMinorUnits,
                originalDebitAfter.AmountMinorUnits);
            Assert.Equal(CreditMovementType.Credit, compensation.Type);
            Assert.Equal(
                JobPriceMinorUnits,
                compensation.AmountMinorUnits);
            Assert.Equal(persistedReturn.Id, compensation.OperationId);
            Assert.Equal(3L, movementsAfterFirst.TotalCount);
            Assert.Equal(3L, movementsAfterReplay.TotalCount);
            Assert.Equal(
                accountBefore.BalanceMinorUnits + JobPriceMinorUnits,
                accountAfterFirst.BalanceMinorUnits);
            Assert.Equal(
                InitialBalanceMinorUnits,
                accountAfterFirst.BalanceMinorUnits);
            Assert.Equal(
                accountAfterFirst.BalanceMinorUnits,
                accountAfterReplay.BalanceMinorUnits);
            Assert.Equal(
                SettlementReturnState.Completed,
                persistedReturn.State);
            Assert.Equal(1L, auditCountAfterFirst);
            Assert.Equal(
                1L,
                await CountReturnAuditsAsync(
                    dbContext,
                    persistedReturn.Id));
            Assert.Equal(
                1L,
                await CountReturnAuditsForActorAsync(
                    dbContext,
                    persistedReturn.Id,
                    administratorUserId));

            var auditDescription = await FindReturnAuditDescriptionAsync(
                dbContext,
                persistedReturn.Id);
            Assert.Contains(
                scenario.JobId.ToString(),
                auditDescription,
                StringComparison.Ordinal);
            Assert.Contains(
                scenario.CustomerUserId.ToString(),
                auditDescription,
                StringComparison.Ordinal);
            Assert.Contains(
                JobPriceMinorUnits.ToString(
                    System.Globalization.CultureInfo.InvariantCulture),
                auditDescription,
                StringComparison.Ordinal);
            Assert.Contains(
                ReturnReason,
                auditDescription,
                StringComparison.Ordinal);
            Assert.Contains(
                "returned to FUA Pay credit",
                auditDescription,
                StringComparison.Ordinal);

            var jobAfter = Assert.IsType<Job>(
                await jobRepository.FindByIdAsync(
                    scenario.JobId,
                    CancellationToken.None));
            Assert.Equal(jobSnapshot, JobSnapshot.Create(jobAfter));
            Assert.Equal(
                jobVersionBefore,
                await FindJobVersionAsync(dbContext, scenario.JobId));

            var projected = Assert.IsType<JobDetail>(
                await jobQueries.FindForCustomerAsync(
                    scenario.CustomerUserId,
                    scenario.JobId));
            Assert.Equal(
                JobPaymentStatus.Paid,
                projected.PaymentStatus);
            Assert.Equal<JobSettlementType?>(
                JobSettlementType.Credit,
                projected.SettlementType);
            Assert.Equal<Guid?>(scenario.JobId, projected.SettlementReferenceId);
            Assert.Equal<DateTimeOffset?>(
                scenario.SettledAt,
                projected.SettledAt);
            Assert.Equal(
                JobProductionStatus.Completed,
                projected.ProductionStatus);
            Assert.Equal<Guid?>(persistedReturn.Id, projected.SettlementReturnId);
            Assert.Equal<DateTimeOffset?>(
                persistedReturn.CompletedAt,
                projected.ReturnedAt);

            var conflict = await Assert.ThrowsAsync<
                SettlementReturnRequestConflictException>(
                () => service.ReturnAsync(
                    command with { Reason = "Different reason" }));
            Assert.Equal(requestId, conflict.RequestId);
            Assert.Equal(
                3L,
                (await creditQueries.ListMovementsForOwnerAsync(
                    scenario.CustomerUserId,
                    new CreditMovementPageRequest(limit: 100))).TotalCount);
            Assert.Equal(
                1L,
                await CountReturnAuditsAsync(
                    dbContext,
                    persistedReturn.Id));
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    [Fact]
    public async Task ReturnAsync_ConcurrentRequestsCreateOneReturnAndCredit()
    {
        CreditJobScenario scenario;
        var firstAdministratorUserId = Guid.NewGuid();
        var secondAdministratorUserId = Guid.NewGuid();

        using (var setupScope = _factory.Services.CreateScope())
        {
            scenario = await SeedPaidCreditJobAsync(
                setupScope.ServiceProvider,
                completeProduction: true);
        }

        try
        {
            var attempts = await Task.WhenAll(
                RunReturnAttemptAsync(
                    new CreditJobSettlementReturnCommand(
                        Guid.NewGuid(),
                        scenario.JobId,
                        firstAdministratorUserId,
                        "First concurrent return")),
                RunReturnAttemptAsync(
                    new CreditJobSettlementReturnCommand(
                        Guid.NewGuid(),
                        scenario.JobId,
                        secondAdministratorUserId,
                        "Second concurrent return")));

            var success = Assert.Single(
                attempts,
                attempt => attempt.Result is not null);
            var failure = Assert.Single(
                attempts,
                attempt => attempt.Exception is not null);
            Assert.True(success.Result!.Created);
            var conflict = Assert.IsType<
                SettlementReturnSourceConflictException>(
                failure.Exception);
            Assert.Equal(
                success.Result.SettlementReturn.Id,
                conflict.ExistingSettlementReturnId);

            using var verificationScope =
                _factory.Services.CreateScope();
            var services = verificationScope.ServiceProvider;
            var dbContext =
                services.GetRequiredService<FuaPayDbContext>();
            var returnRepository =
                services.GetRequiredService<ISettlementReturnRepository>();
            var creditQueries =
                services.GetRequiredService<ICreditQueries>();
            var persistedReturn = Assert.IsType<SettlementReturn>(
                await returnRepository.FindByJobIdAsync(scenario.JobId));
            var movements = await creditQueries.ListMovementsForOwnerAsync(
                scenario.CustomerUserId,
                new CreditMovementPageRequest(limit: 100));

            Assert.Equal(
                success.Result.SettlementReturn.Id,
                persistedReturn.Id);
            Assert.Equal(
                SettlementReturnState.Completed,
                persistedReturn.State);
            Assert.Single(
                movements.Items,
                movement =>
                    movement.OperationId == persistedReturn.Id &&
                    movement.Type == CreditMovementType.Credit &&
                    movement.AmountMinorUnits == JobPriceMinorUnits);
            Assert.Equal(3L, movements.TotalCount);
            Assert.Equal(
                1L,
                await CountReturnsForJobAsync(
                    dbContext,
                    scenario.JobId));
            Assert.Equal(
                1L,
                await CountReturnAuditsAsync(
                    dbContext,
                    persistedReturn.Id));
        }
        finally
        {
            await DeleteScenarioAsync(scenario);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReturnAsync_RejectsUnpaidAndDirectPaidJobs(
        bool directPaid)
    {
        using var scope = _factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        var dbContext = services.GetRequiredService<FuaPayDbContext>();

        await using var transaction =
            await dbContext.Database.BeginTransactionAsync();

        try
        {
            var jobRepository =
                services.GetRequiredService<IJobRepository>();
            var job = CreatePublishedJob();

            await jobRepository.AddAsync(
                job,
                CancellationToken.None);

            if (directPaid)
            {
                job.ConfirmSettlement(
                    JobSettlementType.DirectPayment,
                    Guid.NewGuid(),
                    TestTime.AddMinutes(3));
                await jobRepository.SaveAsync(
                    job,
                    CancellationToken.None);
            }

            var service = CreateReturnService(
                services,
                new FixedTimeProvider(TestTime.AddMinutes(7)));
            var exception = await Assert.ThrowsAsync<
                CreditJobSettlementReturnNotAllowedException>(
                () => service.ReturnAsync(
                    new CreditJobSettlementReturnCommand(
                        Guid.NewGuid(),
                        job.Id,
                        Guid.NewGuid(),
                        ReturnReason)));

            Assert.Equal(job.Id, exception.JobId);
            Assert.Equal(
                directPaid
                    ? JobPaymentStatus.Paid
                    : JobPaymentStatus.Unpaid,
                exception.PaymentStatus);
            Assert.Null(
                await services
                    .GetRequiredService<ISettlementReturnRepository>()
                    .FindByJobIdAsync(job.Id));
            Assert.Equal(
                0L,
                await CountReturnAuditsForJobAsync(
                    dbContext,
                    job.Id));
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    [Theory]
    [InlineData(OriginalHistoryCorruption.MissingMovement)]
    [InlineData(OriginalHistoryCorruption.WrongOwner)]
    [InlineData(OriginalHistoryCorruption.WrongMovementType)]
    [InlineData(OriginalHistoryCorruption.WrongAmount)]
    [InlineData(OriginalHistoryCorruption.WrongSettlementReference)]
    [InlineData(OriginalHistoryCorruption.WrongTimestamp)]
    public async Task ReturnAsync_RejectsInconsistentOriginalHistory(
        OriginalHistoryCorruption corruption)
    {
        using var scope = _factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        var dbContext = services.GetRequiredService<FuaPayDbContext>();

        await using var transaction =
            await dbContext.Database.BeginTransactionAsync();

        try
        {
            var scenario = await SeedPaidCreditJobAsync(
                services,
                completeProduction: false);

            await CorruptOriginalHistoryAsync(
                dbContext,
                scenario,
                corruption);

            var creditQueries =
                services.GetRequiredService<ICreditQueries>();
            var movementCountBefore =
                (await creditQueries.ListMovementsForOwnerAsync(
                    scenario.CustomerUserId,
                    new CreditMovementPageRequest(limit: 100))).TotalCount;
            var accountBefore = Assert.IsType<CreditAccountSummary>(
                await creditQueries.FindAccountForOwnerAsync(
                    scenario.CustomerUserId));
            var service = CreateReturnService(
                services,
                new FixedTimeProvider(TestTime.AddMinutes(7)));

            var exception = await Assert.ThrowsAsync<
                CreditJobSettlementHistoryInconsistentException>(
                () => service.ReturnAsync(
                    new CreditJobSettlementReturnCommand(
                        Guid.NewGuid(),
                        scenario.JobId,
                        Guid.NewGuid(),
                        ReturnReason)));

            Assert.Equal(scenario.JobId, exception.JobId);
            Assert.Null(
                await services
                    .GetRequiredService<ISettlementReturnRepository>()
                    .FindByJobIdAsync(scenario.JobId));
            Assert.Equal(
                movementCountBefore,
                (await creditQueries.ListMovementsForOwnerAsync(
                    scenario.CustomerUserId,
                    new CreditMovementPageRequest(limit: 100))).TotalCount);
            Assert.Equal(
                accountBefore.BalanceMinorUnits,
                Assert.IsType<CreditAccountSummary>(
                    await creditQueries.FindAccountForOwnerAsync(
                        scenario.CustomerUserId)).BalanceMinorUnits);
            Assert.Equal(
                0L,
                await CountReturnAuditsForJobAsync(
                    dbContext,
                    scenario.JobId));
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    [Theory]
    [InlineData(SettlementReturnState.Requested)]
    [InlineData(SettlementReturnState.InProgress)]
    [InlineData(SettlementReturnState.RequiresAttention)]
    [InlineData(SettlementReturnState.Rejected)]
    [InlineData(SettlementReturnState.Completed)]
    public async Task ReturnAsync_PreExistingReturnWithoutCreditFailsClosed(
        SettlementReturnState state)
    {
        using var scope = _factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        var dbContext = services.GetRequiredService<FuaPayDbContext>();

        await using var transaction =
            await dbContext.Database.BeginTransactionAsync();

        try
        {
            var scenario = await SeedPaidCreditJobAsync(
                services,
                completeProduction: false);
            var command = new CreditJobSettlementReturnCommand(
                Guid.NewGuid(),
                scenario.JobId,
                Guid.NewGuid(),
                ReturnReason);
            var existing = CreateExistingReturn(
                scenario,
                command,
                state);
            var returnRepository =
                services.GetRequiredService<ISettlementReturnRepository>();

            await returnRepository.AddAsync(existing);

            var creditQueries =
                services.GetRequiredService<ICreditQueries>();
            var movementCountBefore =
                (await creditQueries.ListMovementsForOwnerAsync(
                    scenario.CustomerUserId,
                    new CreditMovementPageRequest(limit: 100))).TotalCount;
            var service = CreateReturnService(
                services,
                new FixedTimeProvider(TestTime.AddMinutes(7)));

            var exception = await Assert.ThrowsAsync<
                CreditJobSettlementReturnEffectInconsistentException>(
                () => service.ReturnAsync(command));

            Assert.Equal(existing.Id, exception.SettlementReturnId);
            Assert.Equal(
                movementCountBefore,
                (await creditQueries.ListMovementsForOwnerAsync(
                    scenario.CustomerUserId,
                    new CreditMovementPageRequest(limit: 100))).TotalCount);
            Assert.Null(
                await creditQueries.FindMovementForOwnerAsync(
                    scenario.CustomerUserId,
                    existing.Id));
            Assert.Equal(
                0L,
                await CountReturnAuditsAsync(
                    dbContext,
                    existing.Id));
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    [Fact]
    public async Task ReturnAsync_WhenAuditFails_RollsBackReturnAndCredit()
    {
        CreditJobScenario scenario;

        using (var setupScope = _factory.Services.CreateScope())
        {
            scenario = await SeedPaidCreditJobAsync(
                setupScope.ServiceProvider,
                completeProduction: true);
        }

        try
        {
            var requestId = Guid.NewGuid();

            using (var returnScope = _factory.Services.CreateScope())
            {
                var service = CreateReturnService(
                    returnScope.ServiceProvider,
                    new FixedTimeProvider(TestTime.AddMinutes(7)),
                    new ThrowingAuditTrail());

                await Assert.ThrowsAsync<InjectedAuditFailureException>(
                    () => service.ReturnAsync(
                        new CreditJobSettlementReturnCommand(
                            requestId,
                            scenario.JobId,
                            Guid.NewGuid(),
                            ReturnReason)));
            }

            using var verificationScope =
                _factory.Services.CreateScope();
            var services = verificationScope.ServiceProvider;
            var dbContext =
                services.GetRequiredService<FuaPayDbContext>();
            var creditQueries =
                services.GetRequiredService<ICreditQueries>();
            var movements = await creditQueries.ListMovementsForOwnerAsync(
                scenario.CustomerUserId,
                new CreditMovementPageRequest(limit: 100));
            var account = Assert.IsType<CreditAccountSummary>(
                await creditQueries.FindAccountForOwnerAsync(
                    scenario.CustomerUserId));
            var job = Assert.IsType<Job>(
                await services
                    .GetRequiredService<IJobRepository>()
                    .FindByIdAsync(
                        scenario.JobId,
                        CancellationToken.None));

            Assert.Null(
                await services
                    .GetRequiredService<ISettlementReturnRepository>()
                    .FindByRequestIdAsync(requestId));
            Assert.Equal(2L, movements.TotalCount);
            Assert.Equal(
                InitialBalanceMinorUnits - JobPriceMinorUnits,
                account.BalanceMinorUnits);
            Assert.Contains(
                movements.Items,
                movement =>
                    movement.OperationId == scenario.JobId &&
                    movement.Type == CreditMovementType.Debit &&
                    movement.AmountMinorUnits == JobPriceMinorUnits);
            Assert.Equal(
                JobSnapshot.Create(scenario.PersistedJob),
                JobSnapshot.Create(job));
            Assert.Equal(
                0L,
                await CountReturnAuditsForJobAsync(
                    dbContext,
                    scenario.JobId));
        }
        finally
        {
            await DeleteScenarioAsync(scenario);
        }
    }

    private async Task<ReturnAttempt> RunReturnAttemptAsync(
        CreditJobSettlementReturnCommand command)
    {
        try
        {
            using var scope = _factory.Services.CreateScope();
            var service = CreateReturnService(
                scope.ServiceProvider,
                new FixedTimeProvider(TestTime.AddMinutes(7)));
            var result = await service.ReturnAsync(command);
            return new ReturnAttempt(result, Exception: null);
        }
        catch (Exception exception)
        {
            return new ReturnAttempt(Result: null, exception);
        }
    }

    private static CreditJobSettlementReturnService CreateReturnService(
        IServiceProvider services,
        TimeProvider timeProvider,
        IAuditTrail? auditTrail = null)
    {
        var transaction =
            services.GetRequiredService<IApplicationTransaction>();
        var returnRepository =
            services.GetRequiredService<ISettlementReturnRepository>();
        var creditService = new CreditService(
            services.GetRequiredService<ICreditAccountRepository>(),
            services.GetRequiredService<IPrintReservationRepository>(),
            transaction,
            timeProvider);

        return new CreditJobSettlementReturnService(
            services.GetRequiredService<IJobRepository>(),
            services.GetRequiredService<IJobPaymentCoordination>(),
            services.GetRequiredService<ICreditQueries>(),
            creditService,
            new SettlementReturnRegistrationService(returnRepository),
            returnRepository,
            transaction,
            auditTrail ?? services.GetRequiredService<IAuditTrail>(),
            timeProvider);
    }

    private static async Task<CreditJobScenario> SeedPaidCreditJobAsync(
        IServiceProvider services,
        bool completeProduction)
    {
        var customerUserId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var transaction =
            services.GetRequiredService<IApplicationTransaction>();
        var creditRepository =
            services.GetRequiredService<ICreditAccountRepository>();
        var reservationRepository =
            services.GetRequiredService<IPrintReservationRepository>();
        var jobRepository =
            services.GetRequiredService<IJobRepository>();
        var coordination =
            services.GetRequiredService<IJobPaymentCoordination>();

        var fundingService = new CreditService(
            creditRepository,
            reservationRepository,
            transaction,
            new FixedTimeProvider(TestTime));

        await fundingService.CreditAsync(
            customerUserId,
            Guid.NewGuid(),
            new Money(InitialBalanceMinorUnits),
            "Initial credit for settlement return test");

        var job = CreatePublishedJob(customerUserId, jobId);
        await jobRepository.AddAsync(
            job,
            CancellationToken.None);

        var paymentCreditService = new CreditService(
            creditRepository,
            reservationRepository,
            transaction,
            new FixedTimeProvider(TestTime.AddMinutes(3)));
        var paymentService = new CreditJobPaymentService(
            jobRepository,
            coordination,
            paymentCreditService,
            transaction,
            NullAuditTrail.Instance,
            NullNotificationOutbox.Instance);

        Assert.True(
            await paymentService.PayAsync(customerUserId, jobId));

        var persistedJob = Assert.IsType<Job>(
            await jobRepository.FindByIdAsync(
                jobId,
                CancellationToken.None));

        if (completeProduction)
        {
            persistedJob.StartProduction(TestTime.AddMinutes(4));
            persistedJob.MarkReadyForPickup(TestTime.AddMinutes(5));
            persistedJob.Complete(TestTime.AddMinutes(6));
            await jobRepository.SaveAsync(
                persistedJob,
                CancellationToken.None);
        }

        var authoritativeJob = Assert.IsType<Job>(
            await jobRepository.FindByIdAsync(
                jobId,
                CancellationToken.None));

        return new CreditJobScenario(
            jobId,
            customerUserId,
            Assert.IsType<DateTimeOffset>(authoritativeJob.SettledAt),
            authoritativeJob);
    }

    private static Job CreatePublishedJob(
        Guid? customerUserId = null,
        Guid? jobId = null)
    {
        var job = new Job(
            jobId ?? Guid.NewGuid(),
            NextJobNumber(),
            Guid.NewGuid(),
            customerUserId ?? Guid.NewGuid(),
            Guid.NewGuid(),
            ServiceType.ThreeDPrint,
            "Settlement return test job",
            "Job used to verify a full credit settlement return",
            new Money(JobPriceMinorUnits),
            TestTime.AddMinutes(1));

        job.Publish(TestTime.AddMinutes(2));
        return job;
    }

    private static SettlementReturn CreateExistingReturn(
        CreditJobScenario scenario,
        CreditJobSettlementReturnCommand command,
        SettlementReturnState state)
    {
        var settlementReturn = new SettlementReturn(
            Guid.NewGuid(),
            command.RequestId,
            SettlementReturnKind.CreditJob,
            originalPaymentId: null,
            scenario.JobId,
            scenario.CustomerUserId,
            command.AdministratorUserId,
            new Money(JobPriceMinorUnits),
            command.Reason,
            TestTime.AddMinutes(6));

        if (state != SettlementReturnState.Requested)
        {
            settlementReturn.Begin(TestTime.AddMinutes(6));
        }

        switch (state)
        {
            case SettlementReturnState.Requested:
            case SettlementReturnState.InProgress:
                break;
            case SettlementReturnState.RequiresAttention:
                settlementReturn.RequireAttention(
                    TestTime.AddMinutes(6));
                break;
            case SettlementReturnState.Rejected:
                settlementReturn.Reject(TestTime.AddMinutes(6));
                break;
            case SettlementReturnState.Completed:
                settlementReturn.Complete(TestTime.AddMinutes(6));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(state));
        }

        return settlementReturn;
    }

    private static Task CorruptOriginalHistoryAsync(
        FuaPayDbContext dbContext,
        CreditJobScenario scenario,
        OriginalHistoryCorruption corruption)
    {
        return corruption switch
        {
            OriginalHistoryCorruption.MissingMovement =>
                dbContext.Database.ExecuteSqlInterpolatedAsync(
                    $"""
                    DELETE FROM credits.movements
                    WHERE operation_id = {scenario.JobId}
                    """),
            OriginalHistoryCorruption.WrongOwner =>
                dbContext.Database.ExecuteSqlInterpolatedAsync(
                    $"""
                    WITH new_account AS
                    (
                        INSERT INTO credits.accounts
                            (id, owner_id, balance_minor_units, version)
                        VALUES
                            ({Guid.NewGuid()}, {Guid.NewGuid()}, 0, 1)
                        RETURNING id
                    )
                    UPDATE credits.movements
                    SET account_id = (SELECT id FROM new_account)
                    WHERE operation_id = {scenario.JobId}
                    """),
            OriginalHistoryCorruption.WrongMovementType =>
                dbContext.Database.ExecuteSqlInterpolatedAsync(
                    $"""
                    UPDATE credits.movements
                    SET movement_type = {(int)CreditMovementType.Credit}
                    WHERE operation_id = {scenario.JobId}
                    """),
            OriginalHistoryCorruption.WrongAmount =>
                dbContext.Database.ExecuteSqlInterpolatedAsync(
                    $"""
                    UPDATE credits.movements
                    SET amount_minor_units = {JobPriceMinorUnits + 1}
                    WHERE operation_id = {scenario.JobId}
                    """),
            OriginalHistoryCorruption.WrongSettlementReference =>
                dbContext.Database.ExecuteSqlInterpolatedAsync(
                    $"""
                    UPDATE jobs.jobs
                    SET settlement_reference_id = {Guid.NewGuid()}
                    WHERE id = {scenario.JobId}
                    """),
            OriginalHistoryCorruption.WrongTimestamp =>
                dbContext.Database.ExecuteSqlInterpolatedAsync(
                    $"""
                    UPDATE credits.movements
                    SET recorded_at = {scenario.SettledAt.AddSeconds(1)}
                    WHERE operation_id = {scenario.JobId}
                    """),
            _ => throw new ArgumentOutOfRangeException(nameof(corruption))
        };
    }

    private static Task<long> FindJobVersionAsync(
        FuaPayDbContext dbContext,
        Guid jobId)
    {
        return dbContext.Database
            .SqlQuery<long>(
                $"""
                SELECT version AS "Value"
                FROM jobs.jobs
                WHERE id = {jobId}
                """)
            .SingleAsync();
    }

    private static Task<long> CountReturnsForJobAsync(
        FuaPayDbContext dbContext,
        Guid jobId)
    {
        return dbContext.Database
            .SqlQuery<long>(
                $"""
                SELECT COUNT(*)::bigint AS "Value"
                FROM payments.settlement_returns
                WHERE job_id = {jobId}
                """)
            .SingleAsync();
    }

    private static Task<long> CountReturnAuditsAsync(
        FuaPayDbContext dbContext,
        Guid settlementReturnId)
    {
        return dbContext.Database
            .SqlQuery<long>(
                $"""
                SELECT COUNT(*)::bigint AS "Value"
                FROM audit.events
                WHERE action = {ReturnAction}
                  AND entity_type = 'settlement-return'
                  AND entity_id = {settlementReturnId.ToString()}
                """)
            .SingleAsync();
    }

    private static Task<long> CountReturnAuditsForActorAsync(
        FuaPayDbContext dbContext,
        Guid settlementReturnId,
        Guid administratorUserId)
    {
        return dbContext.Database
            .SqlQuery<long>(
                $"""
                SELECT COUNT(*)::bigint AS "Value"
                FROM audit.events
                WHERE action = {ReturnAction}
                  AND entity_type = 'settlement-return'
                  AND entity_id = {settlementReturnId.ToString()}
                  AND actor_user_id = {administratorUserId}
                """)
            .SingleAsync();
    }

    private static Task<long> CountReturnAuditsForJobAsync(
        FuaPayDbContext dbContext,
        Guid jobId)
    {
        return dbContext.Database
            .SqlQuery<long>(
                $"""
                SELECT COUNT(*)::bigint AS "Value"
                FROM audit.events
                WHERE action = {ReturnAction}
                  AND description LIKE
                      '%' || CAST({jobId} AS text) || '%'
                """)
            .SingleAsync();
    }

    private static Task<string> FindReturnAuditDescriptionAsync(
        FuaPayDbContext dbContext,
        Guid settlementReturnId)
    {
        return dbContext.Database
            .SqlQuery<string>(
                $"""
                SELECT description AS "Value"
                FROM audit.events
                WHERE action = {ReturnAction}
                  AND entity_type = 'settlement-return'
                  AND entity_id = {settlementReturnId.ToString()}
                """)
            .SingleAsync();
    }

    private async Task DeleteScenarioAsync(CreditJobScenario scenario)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext =
            scope.ServiceProvider.GetRequiredService<FuaPayDbContext>();

        await using var transaction =
            await dbContext.Database.BeginTransactionAsync();

        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            DELETE FROM audit.events
            WHERE
                (entity_type = 'job'
                 AND entity_id = {scenario.JobId.ToString()})
                OR
                (entity_type = 'settlement-return'
                 AND entity_id IN
                 (
                     SELECT id::text
                     FROM payments.settlement_returns
                     WHERE job_id = {scenario.JobId}
                 ))
            """);

        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            DELETE FROM notifications.outbox
            WHERE recipient_user_id = {scenario.CustomerUserId}
            """);

        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            DELETE FROM payments.settlement_returns
            WHERE job_id = {scenario.JobId}
            """);

        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            DELETE FROM jobs.jobs
            WHERE id = {scenario.JobId}
            """);

        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            DELETE FROM credits.movements
            WHERE account_id IN
            (
                SELECT id
                FROM credits.accounts
                WHERE owner_id = {scenario.CustomerUserId}
            )
            """);

        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            DELETE FROM credits.accounts
            WHERE owner_id = {scenario.CustomerUserId}
            """);

        await transaction.CommitAsync();
    }

    private static string NextJobNumber()
    {
        var sequence = Interlocked.Increment(ref _jobNumberSequence);
        return $"RT-2026-{sequence % 1_000_000:D6}";
    }

    public enum OriginalHistoryCorruption
    {
        MissingMovement,
        WrongOwner,
        WrongMovementType,
        WrongAmount,
        WrongSettlementReference,
        WrongTimestamp
    }

    private sealed record CreditJobScenario(
        Guid JobId,
        Guid CustomerUserId,
        DateTimeOffset SettledAt,
        Job PersistedJob);

    private sealed record ReturnAttempt(
        CreditJobSettlementReturnResult? Result,
        Exception? Exception);

    private sealed record JobSnapshot(
        JobProductionStatus ProductionStatus,
        JobPaymentStatus PaymentStatus,
        JobSettlementType? SettlementType,
        Guid? SettlementReferenceId,
        DateTimeOffset? SettledAt,
        DateTimeOffset? ProductionStartedAt,
        DateTimeOffset? ReadyForPickupAt,
        DateTimeOffset? CompletedAt,
        DateTimeOffset? CancelledAt)
    {
        internal static JobSnapshot Create(Job job)
        {
            return new JobSnapshot(
                job.ProductionStatus,
                job.PaymentStatus,
                job.SettlementType,
                job.SettlementReferenceId,
                job.SettledAt,
                job.ProductionStartedAt,
                job.ReadyForPickupAt,
                job.CompletedAt,
                job.CancelledAt);
        }
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;

        internal FixedTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public override DateTimeOffset GetUtcNow()
        {
            return _utcNow;
        }
    }

    private sealed class ThrowingAuditTrail : IAuditTrail
    {
        public void Stage(AuditEntry entry)
        {
            ArgumentNullException.ThrowIfNull(entry);
            throw new InjectedAuditFailureException();
        }

        public Task WriteAsync(
            AuditEntry entry,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(entry);
            cancellationToken.ThrowIfCancellationRequested();
            throw new InjectedAuditFailureException();
        }
    }

    private sealed class InjectedAuditFailureException : Exception;
}
