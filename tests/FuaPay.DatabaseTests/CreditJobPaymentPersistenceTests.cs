using FuaPay.Web.BuildingBlocks.Application;
using FuaPay.Web.BuildingBlocks.Auditing;
using FuaPay.Web.BuildingBlocks.Domain;
using FuaPay.Web.BuildingBlocks.Notifications;
using FuaPay.Web.BuildingBlocks.Persistence;
using FuaPay.Web.Modules.Credits.Application;
using FuaPay.Web.Modules.Credits.Domain;
using FuaPay.Web.Modules.Jobs.Application;
using FuaPay.Web.Modules.Jobs.Domain;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FuaPay.DatabaseTests;

public sealed class CreditJobPaymentPersistenceTests :
    IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly DateTimeOffset TestTime =
        new(
            2026,
            7,
            28,
            15,
            0,
            0,
            TimeSpan.Zero);

    private readonly WebApplicationFactory<Program> _factory;

    public CreditJobPaymentPersistenceTests(
        WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task PayAsync_CommitsCreditDebitAndJobSettlementTogether()
    {
        var customerUserId = Guid.NewGuid();
        var jobId = Guid.NewGuid();

        try
        {
            await SeedScenarioAsync(
                customerUserId,
                jobId);

            using (var scope =
                _factory.Services.CreateScope())
            {
                var jobRepository =
                    scope.ServiceProvider
                        .GetRequiredService<IJobRepository>();

                var creditRepository =
                    scope.ServiceProvider
                        .GetRequiredService<
                            ICreditAccountRepository>();

                var transaction =
                    scope.ServiceProvider
                        .GetRequiredService<
                            IApplicationTransaction>();

                var coordination =
                    scope.ServiceProvider
                        .GetRequiredService<
                            IJobPaymentCoordination>();

                var creditService = new CreditService(
                    creditRepository,
                    scope.ServiceProvider
                        .GetRequiredService<IPrintReservationRepository>(),
                    transaction,
                    new FixedTimeProvider(
                        TestTime.AddMinutes(20)));

                var service = new CreditJobPaymentService(
                    jobRepository,
                    coordination,
                    creditService,
                    transaction,
                    NullAuditTrail.Instance,
            NullNotificationOutbox.Instance);

                var wasApplied = await service.PayAsync(
                    customerUserId,
                    jobId);

                Assert.True(wasApplied);
            }

            using var verificationScope =
                _factory.Services.CreateScope();

            var verificationJobRepository =
                verificationScope.ServiceProvider
                    .GetRequiredService<IJobRepository>();

            var verificationCreditRepository =
                verificationScope.ServiceProvider
                    .GetRequiredService<
                        ICreditAccountRepository>();

            var persistedJob =
                Assert.IsType<Job>(
                    await verificationJobRepository
                        .FindByIdAsync(
                            jobId,
                            CancellationToken.None));

            var persistedAccount =
                Assert.IsType<CreditAccount>(
                    await verificationCreditRepository
                        .FindByOwnerIdAsync(
                            customerUserId,
                            CancellationToken.None));

            Assert.Equal(
                JobPaymentStatus.Paid,
                persistedJob.PaymentStatus);

            Assert.Equal(
                JobSettlementType.Credit,
                persistedJob.SettlementType);

            Assert.Equal(
                jobId,
                persistedJob.SettlementReferenceId);

            Assert.Equal(
                TestTime.AddMinutes(20),
                persistedJob.SettledAt);

            Assert.Equal(
                new Money(7_500),
                persistedAccount.Balance);

            var debitMovement = Assert.Single(
                persistedAccount.Movements,
                movement =>
                    movement.OperationId == jobId);

            Assert.Equal(
                CreditMovementType.Debit,
                debitMovement.Type);

            Assert.Equal(
                new Money(12_500),
                debitMovement.Amount);
        }
        finally
        {
            await DeleteScenarioAsync(
                customerUserId,
                jobId);
        }
    }

    [Fact]
    public async Task PayAsync_WhenJobSaveConflicts_RollsBackCreditDebit()
    {
        var customerUserId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var cancellationTime =
            TestTime.AddMinutes(25);

        try
        {
            await SeedScenarioAsync(
                customerUserId,
                jobId);

            using (var scope =
                _factory.Services.CreateScope())
            {
                var innerJobRepository =
                    scope.ServiceProvider
                        .GetRequiredService<IJobRepository>();

                var conflictingJobRepository =
                    new ConflictingJobRepository(
                        innerJobRepository,
                        _factory,
                        cancellationTime);

                var creditRepository =
                    scope.ServiceProvider
                        .GetRequiredService<
                            ICreditAccountRepository>();

                var transaction =
                    scope.ServiceProvider
                        .GetRequiredService<
                            IApplicationTransaction>();

                var creditService = new CreditService(
                    creditRepository,
                    scope.ServiceProvider
                        .GetRequiredService<IPrintReservationRepository>(),
                    transaction,
                    new FixedTimeProvider(
                        TestTime.AddMinutes(20)));

                var service = new CreditJobPaymentService(
                    conflictingJobRepository,
                    new NonLockingJobPaymentCoordination(),
                    creditService,
                    transaction,
                    NullAuditTrail.Instance,
            NullNotificationOutbox.Instance);

                var exception =
                    await Assert.ThrowsAsync<
                        JobConcurrencyException>(
                        () => service.PayAsync(
                            customerUserId,
                            jobId));

                Assert.Equal(jobId, exception.JobId);
            }

            using var verificationScope =
                _factory.Services.CreateScope();

            var verificationJobRepository =
                verificationScope.ServiceProvider
                    .GetRequiredService<IJobRepository>();

            var verificationCreditRepository =
                verificationScope.ServiceProvider
                    .GetRequiredService<
                        ICreditAccountRepository>();

            var persistedJob =
                Assert.IsType<Job>(
                    await verificationJobRepository
                        .FindByIdAsync(
                            jobId,
                            CancellationToken.None));

            var persistedAccount =
                Assert.IsType<CreditAccount>(
                    await verificationCreditRepository
                        .FindByOwnerIdAsync(
                            customerUserId,
                            CancellationToken.None));

            Assert.Equal(
                JobProductionStatus.Cancelled,
                persistedJob.ProductionStatus);

            Assert.Equal(
                JobPaymentStatus.Unpaid,
                persistedJob.PaymentStatus);

            Assert.Equal(
                cancellationTime,
                persistedJob.CancelledAt);

            Assert.Null(persistedJob.SettlementType);
            Assert.Null(persistedJob.SettlementReferenceId);
            Assert.Null(persistedJob.SettledAt);

            Assert.Equal(
                new Money(20_000),
                persistedAccount.Balance);

            Assert.DoesNotContain(
                persistedAccount.Movements,
                movement =>
                    movement.OperationId == jobId);

            Assert.Single(persistedAccount.Movements);
        }
        finally
        {
            await DeleteScenarioAsync(
                customerUserId,
                jobId);
        }
    }

    private async Task SeedScenarioAsync(
        Guid customerUserId,
        Guid jobId)
    {
        using var scope =
            _factory.Services.CreateScope();

        var creditRepository =
            scope.ServiceProvider
                .GetRequiredService<
                    ICreditAccountRepository>();

        var creditService = new CreditService(
            creditRepository,
            scope.ServiceProvider
                .GetRequiredService<IPrintReservationRepository>(),
            scope.ServiceProvider
                .GetRequiredService<IApplicationTransaction>(),
            new FixedTimeProvider(TestTime));

        await creditService.CreditAsync(
            customerUserId,
            Guid.NewGuid(),
            new Money(20_000),
            "Počáteční kredit pro úhradu zakázky");

        var jobRepository =
            scope.ServiceProvider
                .GetRequiredService<IJobRepository>();

        var job = new Job(
            jobId,
            NextJobNumber(),
            Guid.NewGuid(),
            customerUserId,
            Guid.NewGuid(),
            ServiceType.ThreeDPrint,
            "Databázový model",
            "Zakázka pro test kreditní úhrady",
            new Money(12_500),
            TestTime);

        job.Publish(
            TestTime.AddMinutes(10));

        await jobRepository.AddAsync(
            job,
            CancellationToken.None);
    }

    private async Task DeleteScenarioAsync(
        Guid customerUserId,
        Guid jobId)
    {
        using var scope =
            _factory.Services.CreateScope();

        var dbContext =
            scope.ServiceProvider
                .GetRequiredService<FuaPayDbContext>();

        await using var transaction =
            await dbContext.Database
                .BeginTransactionAsync();

        await dbContext.Database
            .ExecuteSqlInterpolatedAsync(
                $"""
                DELETE FROM jobs.jobs
                WHERE id = {jobId}
                """);

        await dbContext.Database
            .ExecuteSqlInterpolatedAsync(
                $"""
                DELETE FROM credits.movements
                WHERE account_id IN
                (
                    SELECT id
                    FROM credits.accounts
                    WHERE owner_id = {customerUserId}
                )
                """);

        await dbContext.Database
            .ExecuteSqlInterpolatedAsync(
                $"""
                DELETE FROM credits.accounts
                WHERE owner_id = {customerUserId}
                """);

        await transaction.CommitAsync();
    }

    private sealed class FixedTimeProvider :
        TimeProvider
    {
        private readonly DateTimeOffset _utcNow;

        internal FixedTimeProvider(
            DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public override DateTimeOffset GetUtcNow()
        {
            return _utcNow;
        }
    }

    private sealed class NonLockingJobPaymentCoordination :
        IJobPaymentCoordination
    {
        public Task<bool> LockJobAsync(
            Guid jobId,
            CancellationToken cancellationToken) =>
            Task.FromResult(true);

        public Task<bool> HasBlockingDirectPaymentAsync(
            Guid jobId,
            CancellationToken cancellationToken) =>
            Task.FromResult(false);
    }

    private sealed class ConflictingJobRepository :
        IJobRepository
    {
        private readonly IJobRepository _inner;
        private readonly WebApplicationFactory<Program> _factory;
        private readonly DateTimeOffset _cancellationTime;

        private bool _conflictCreated;

        internal ConflictingJobRepository(
            IJobRepository inner,
            WebApplicationFactory<Program> factory,
            DateTimeOffset cancellationTime)
        {
            ArgumentNullException.ThrowIfNull(inner);
            ArgumentNullException.ThrowIfNull(factory);

            _inner = inner;
            _factory = factory;
            _cancellationTime = cancellationTime;
        }

        public Task<Job?> FindByIdAsync(
            Guid jobId,
            CancellationToken cancellationToken)
        {
            return _inner.FindByIdAsync(
                jobId,
                cancellationToken);
        }

        public Task AddAsync(
            Job job,
            CancellationToken cancellationToken)
        {
            return _inner.AddAsync(
                job,
                cancellationToken);
        }

        public async Task SaveAsync(
            Job job,
            CancellationToken cancellationToken)
        {
            if (!_conflictCreated)
            {
                _conflictCreated = true;

                using var winningScope =
                    _factory.Services.CreateScope();

                var winningRepository =
                    winningScope.ServiceProvider
                        .GetRequiredService<IJobRepository>();

                var winningJob =
                    Assert.IsType<Job>(
                        await winningRepository
                            .FindByIdAsync(
                                job.Id,
                                cancellationToken));

                winningJob.Cancel(
                    _cancellationTime);

                await winningRepository.SaveAsync(
                    winningJob,
                    cancellationToken);
            }

            await _inner.SaveAsync(
                job,
                cancellationToken);
        }
    }
}
