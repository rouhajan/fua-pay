using FuaPay.Web.BuildingBlocks.Auditing;
using FuaPay.Web.BuildingBlocks.Domain;
using FuaPay.Web.BuildingBlocks.Persistence;
using FuaPay.Web.Modules.Jobs.Application;
using FuaPay.Web.Modules.Jobs.Domain;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FuaPay.DatabaseTests;

public sealed class JobPersistenceTests :
    IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly DateTimeOffset TestTime =
        new(
            2026,
            7,
            28,
            12,
            0,
            0,
            TimeSpan.Zero);

    private readonly WebApplicationFactory<Program> _factory;

    public JobPersistenceTests(
        WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task FullLifecycle_RoundTripsThroughPostgreSql()
    {
        using var scope =
            _factory.Services.CreateScope();

        var dbContext =
            scope.ServiceProvider
                .GetRequiredService<FuaPayDbContext>();

        var repository =
            scope.ServiceProvider
                .GetRequiredService<IJobRepository>();

        await using var transaction =
            await dbContext.Database
                .BeginTransactionAsync();

        try
        {
            var serviceUnitId = Guid.NewGuid();
            var createdByUserId = Guid.NewGuid();
            var updatedCustomerId = Guid.NewGuid();
            var settlementReferenceId = Guid.NewGuid();
            var publishedAt = TestTime.AddMinutes(10);
            var settledAt = TestTime.AddMinutes(20);
            var productionStartedAt = TestTime.AddMinutes(30);
            var readyForPickupAt = TestTime.AddMinutes(40);
            var completedAt = TestTime.AddMinutes(50);

            var createdJob = new Job(
                Guid.NewGuid(),
                NextJobNumber(),
                serviceUnitId,
                Guid.NewGuid(),
                createdByUserId,
                ServiceType.ThreeDPrint,
                "  Původní model  ",
                "  Původní popis zakázky  ",
                new Money(12_500),
                TestTime);

            await repository.AddAsync(
                createdJob,
                CancellationToken.None);

            createdJob.UpdateDraft(
                updatedCustomerId,
                ServiceType.Other,
                "Administrativní poplatek",
                "Řezání překližky",
                new Money(25_000));

            await repository.SaveAsync(
                createdJob,
                CancellationToken.None);

            createdJob.Publish(publishedAt);

            await repository.SaveAsync(
                createdJob,
                CancellationToken.None);

            var settlementApplied =
                await new JobSettlementService(
                        repository,
                        new FixedTimeProvider(settledAt),
                        NullAuditTrail.Instance)
                    .ConfirmAsync(
                        createdJob.Id,
                        JobSettlementType.Credit,
                        settlementReferenceId);

            Assert.True(settlementApplied);

            var settledJob =
                Assert.IsType<Job>(
                    await repository.FindByIdAsync(
                        createdJob.Id,
                        CancellationToken.None));

            settledJob.StartProduction(productionStartedAt);
            await repository.SaveAsync(
                settledJob,
                CancellationToken.None);

            settledJob.MarkReadyForPickup(readyForPickupAt);
            await repository.SaveAsync(
                settledJob,
                CancellationToken.None);

            settledJob.Complete(completedAt);
            await repository.SaveAsync(
                settledJob,
                CancellationToken.None);

            var persistedJob =
                Assert.IsType<Job>(
                    await repository.FindByIdAsync(
                        settledJob.Id,
                        CancellationToken.None));

            Assert.Equal(settledJob.Number, persistedJob.Number);

            Assert.Equal(
                updatedCustomerId,
                persistedJob.CustomerUserId);

            Assert.Equal(
                createdByUserId,
                persistedJob.CreatedByUserId);

            Assert.Equal(
                serviceUnitId,
                persistedJob.ServiceUnitId);

            Assert.Equal(
                ServiceType.Other,
                persistedJob.ServiceType);

            Assert.Equal(
                "Administrativní poplatek",
                persistedJob.Title);

            Assert.Equal(
                "Řezání překližky",
                persistedJob.Description);

            Assert.Equal(
                new Money(25_000),
                persistedJob.Price);

            Assert.Equal(
                JobProductionStatus.Completed,
                persistedJob.ProductionStatus);

            Assert.Equal(
                JobPaymentStatus.Paid,
                persistedJob.PaymentStatus);

            Assert.Equal<JobSettlementType?>(
                JobSettlementType.Credit,
                persistedJob.SettlementType);

            Assert.Equal(
                settlementReferenceId,
                persistedJob.SettlementReferenceId);

            Assert.Equal(
                TestTime,
                persistedJob.CreatedAt);

            Assert.Equal(
                publishedAt,
                persistedJob.PublishedAt);

            Assert.Equal(
                settledAt,
                persistedJob.SettledAt);

            Assert.Equal(
                productionStartedAt,
                persistedJob.ProductionStartedAt);

            Assert.Equal(
                readyForPickupAt,
                persistedJob.ReadyForPickupAt);

            Assert.Equal(
                completedAt,
                persistedJob.CompletedAt);

            Assert.Null(persistedJob.CancelledAt);
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    [Fact]
    public async Task AddAsync_DuplicateJobNumber_IsRejected()
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider
            .GetRequiredService<FuaPayDbContext>();
        var repository = scope.ServiceProvider
            .GetRequiredService<IJobRepository>();

        await using var transaction =
            await dbContext.Database.BeginTransactionAsync();

        try
        {
            var number = NextJobNumber();

            var first = new Job(
                Guid.NewGuid(),
                number,
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                ServiceType.ThreeDPrint,
                "První zakázka",
                "První popis",
                new Money(10_000),
                TestTime);

            var second = new Job(
                Guid.NewGuid(),
                number,
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                ServiceType.ThreeDPrint,
                "Druhá zakázka",
                "Druhý popis",
                new Money(10_000),
                TestTime.AddMinutes(1));

            await repository.AddAsync(first, CancellationToken.None);

            var exception =
                await Assert.ThrowsAsync<JobNumberAlreadyUsedException>(
                    () => repository.AddAsync(
                        second,
                        CancellationToken.None));

            Assert.Equal(number, exception.JobNumber);
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    [Fact]
    public async Task SaveAsync_WhenJobWasChangedAfterLoad_ThrowsAndKeepsWinningState()
    {
        var jobId =
            Guid.NewGuid();

        try
        {
            await SeedJobAsync(
                CreateDraftJob(jobId));

            using (
                var winningScope =
                    _factory.Services.CreateScope())
            using (
                var staleScope =
                    _factory.Services.CreateScope())
            {
                var winningRepository =
                    winningScope.ServiceProvider
                        .GetRequiredService<IJobRepository>();

                var staleRepository =
                    staleScope.ServiceProvider
                        .GetRequiredService<IJobRepository>();

                var winningJob =
                    Assert.IsType<Job>(
                        await winningRepository
                            .FindByIdAsync(
                                jobId,
                                CancellationToken.None));

                var staleJob =
                    Assert.IsType<Job>(
                        await staleRepository
                            .FindByIdAsync(
                                jobId,
                                CancellationToken.None));

                winningJob.UpdateDraft(
                    Guid.NewGuid(),
                    ServiceType.Workshop,
                    "Vítězná změna",
                    "Platná změna zakázky",
                    new Money(20_000));

                staleJob.UpdateDraft(
                    Guid.NewGuid(),
                    ServiceType.LargeFormatPrint,
                    "Zastaralá změna",
                    "Tato změna se nesmí uložit",
                    new Money(30_000));

                await winningRepository.SaveAsync(
                    winningJob,
                    CancellationToken.None);

                var exception =
                    await Assert.ThrowsAsync<
                        JobConcurrencyException>(
                        () =>
                            staleRepository.SaveAsync(
                                staleJob,
                                CancellationToken.None));

                Assert.Equal(
                    jobId,
                    exception.JobId);
            }

            using var verificationScope =
                _factory.Services.CreateScope();

            var verificationRepository =
                verificationScope.ServiceProvider
                    .GetRequiredService<IJobRepository>();

            var persistedJob =
                Assert.IsType<Job>(
                    await verificationRepository
                        .FindByIdAsync(
                            jobId,
                            CancellationToken.None));

            Assert.Equal(
                "Vítězná změna",
                persistedJob.Title);

            Assert.Equal(
                "Platná změna zakázky",
                persistedJob.Description);

            Assert.Equal(
                ServiceType.Workshop,
                persistedJob.ServiceType);

            Assert.Equal(
                new Money(20_000),
                persistedJob.Price);
        }
        finally
        {
            await DeleteJobsAsync(jobId);
        }
    }

    [Fact]
    public async Task SameSettlementReferenceOnDifferentJobs_IsRejected()
    {
        var firstJobId =
            Guid.NewGuid();

        var secondJobId =
            Guid.NewGuid();

        var settlementReferenceId =
            Guid.NewGuid();

        try
        {
            await SeedJobAsync(
                CreatePublishedJob(firstJobId));

            await SeedJobAsync(
                CreatePublishedJob(secondJobId));

            using (var firstScope =
                _factory.Services.CreateScope())
            {
                var firstRepository =
                    firstScope.ServiceProvider
                        .GetRequiredService<IJobRepository>();

                var wasApplied =
                    await new JobSettlementService(
                            firstRepository,
                            new FixedTimeProvider(
                                TestTime.AddMinutes(20)),
                            NullAuditTrail.Instance)
                        .ConfirmAsync(
                            firstJobId,
                            JobSettlementType.DirectPayment,
                            settlementReferenceId);

                Assert.True(wasApplied);
            }

            using (var secondScope =
                _factory.Services.CreateScope())
            {
                var secondRepository =
                    secondScope.ServiceProvider
                        .GetRequiredService<IJobRepository>();

                var exception =
                    await Assert.ThrowsAsync<
                        JobSettlementReferenceAlreadyUsedException>(
                        () =>
                            new JobSettlementService(
                                    secondRepository,
                                    new FixedTimeProvider(
                                        TestTime.AddMinutes(20)),
                                    NullAuditTrail.Instance)
                                .ConfirmAsync(
                                    secondJobId,
                                    JobSettlementType.DirectPayment,
                                    settlementReferenceId));

                Assert.Equal(
                    JobSettlementType.DirectPayment,
                    exception.SettlementType);

                Assert.Equal(
                    settlementReferenceId,
                    exception.SettlementReferenceId);
            }

            using var verificationScope =
                _factory.Services.CreateScope();

            var verificationRepository =
                verificationScope.ServiceProvider
                    .GetRequiredService<IJobRepository>();

            var firstPersistedJob =
                Assert.IsType<Job>(
                    await verificationRepository
                        .FindByIdAsync(
                            firstJobId,
                            CancellationToken.None));

            var secondPersistedJob =
                Assert.IsType<Job>(
                    await verificationRepository
                        .FindByIdAsync(
                            secondJobId,
                            CancellationToken.None));

            Assert.Equal(
                JobPaymentStatus.Paid,
                firstPersistedJob.PaymentStatus);

            Assert.Equal(
                settlementReferenceId,
                firstPersistedJob.SettlementReferenceId);

            Assert.Equal(
                JobPaymentStatus.Unpaid,
                secondPersistedJob.PaymentStatus);

            Assert.Null(secondPersistedJob.SettlementType);
            Assert.Null(secondPersistedJob.SettlementReferenceId);
            Assert.Null(secondPersistedJob.SettledAt);
        }
        finally
        {
            await DeleteJobsAsync(
                firstJobId,
                secondJobId);
        }
    }

    private async Task SeedJobAsync(Job job)
    {
        using var scope =
            _factory.Services.CreateScope();

        var repository =
            scope.ServiceProvider
                .GetRequiredService<IJobRepository>();

        await repository.AddAsync(
            job,
            CancellationToken.None);
    }

    private async Task DeleteJobsAsync(
        params Guid[] jobIds)
    {
        using var scope =
            _factory.Services.CreateScope();

        var dbContext =
            scope.ServiceProvider
                .GetRequiredService<FuaPayDbContext>();

        await using var transaction =
            await dbContext.Database
                .BeginTransactionAsync();

        foreach (var jobId in jobIds.Distinct())
        {
            await dbContext.Database
                .ExecuteSqlInterpolatedAsync(
                    $"""
                    DELETE FROM jobs.jobs
                    WHERE id = {jobId}
                    """);
        }

        await transaction.CommitAsync();
    }

    private static Job CreateDraftJob(Guid jobId)
    {
        return new Job(
            jobId,
            NextJobNumber(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            ServiceType.ThreeDPrint,
            "Testovací model",
            "Testovací popis",
            new Money(10_000),
            TestTime);
    }

    private static Job CreatePublishedJob(Guid jobId)
    {
        var job =
            CreateDraftJob(jobId);

        job.Publish(
            TestTime.AddMinutes(10));

        return job;
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
}
