using FuaPay.Web.BuildingBlocks.Domain;
using FuaPay.Web.BuildingBlocks.Persistence;
using FuaPay.Web.Modules.Jobs.Application;
using FuaPay.Web.Modules.Jobs.Domain;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FuaPay.DatabaseTests;

public sealed class JobQueriesPersistenceTests :
    IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly DateTimeOffset TestTime =
        new(
            2026,
            7,
            28,
            16,
            0,
            0,
            TimeSpan.Zero);

    private readonly WebApplicationFactory<Program> _factory;

    public JobQueriesPersistenceTests(
        WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ListForCustomerAsync_ReturnsOnlyOwnPublishedHistory()
    {
        using var scope =
            _factory.Services.CreateScope();

        var dbContext =
            scope.ServiceProvider
                .GetRequiredService<FuaPayDbContext>();

        var repository =
            scope.ServiceProvider
                .GetRequiredService<IJobRepository>();

        var queries =
            scope.ServiceProvider
                .GetRequiredService<IJobQueries>();

        await using var transaction =
            await dbContext.Database
                .BeginTransactionAsync();

        try
        {
            var customerUserId = Guid.NewGuid();
            var otherCustomerUserId = Guid.NewGuid();
            var requesterUserId = Guid.NewGuid();

            var hiddenDraft = CreateJob(
                customerUserId,
                requesterUserId,
                "Skrytý návrh",
                TestTime);

            var oldestVisible = CreateJob(
                customerUserId,
                requesterUserId,
                "Nejstarší zveřejněná",
                TestTime.AddMinutes(10));

            oldestVisible.Publish(
                TestTime.AddMinutes(11));

            var newerVisible = CreateJob(
                customerUserId,
                requesterUserId,
                "Novější zveřejněná",
                TestTime.AddMinutes(20));

            newerVisible.Publish(
                TestTime.AddMinutes(21));

            var cancelledVisible = CreateJob(
                customerUserId,
                requesterUserId,
                "Zrušená po zveřejnění",
                TestTime.AddMinutes(30));

            cancelledVisible.Publish(
                TestTime.AddMinutes(31));

            cancelledVisible.Cancel(
                TestTime.AddMinutes(32));

            var otherCustomerJob = CreateJob(
                otherCustomerUserId,
                requesterUserId,
                "Cizí zakázka",
                TestTime.AddMinutes(40));

            otherCustomerJob.Publish(
                TestTime.AddMinutes(41));

            await AddJobsAsync(
                repository,
                hiddenDraft,
                oldestVisible,
                newerVisible,
                cancelledVisible,
                otherCustomerJob);

            var firstPage =
                await queries.ListForCustomerAsync(
                    customerUserId,
                    new JobListFilter(),
                    new JobPageRequest(
                        offset: 0,
                        limit: 2));

            Assert.Equal(3L, firstPage.TotalCount);
            Assert.Equal(2, firstPage.Items.Count);
            Assert.True(firstPage.HasMore);

            Assert.Collection(
                firstPage.Items,
                item =>
                {
                    Assert.Equal(
                        cancelledVisible.Id,
                        item.Id);

                    Assert.Equal(
                        JobProductionStatus.Cancelled,
                        item.ProductionStatus);
                },
                item =>
                {
                    Assert.Equal(
                        newerVisible.Id,
                        item.Id);
                });

            var secondPage =
                await queries.ListForCustomerAsync(
                    customerUserId,
                    new JobListFilter(),
                    new JobPageRequest(
                        offset: 2,
                        limit: 2));

            var remainingItem =
                Assert.Single(secondPage.Items);

            Assert.Equal(
                oldestVisible.Id,
                remainingItem.Id);

            Assert.False(secondPage.HasMore);
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    [Fact]
    public async Task GetCustomerSummaryAsync_CountsVisibleAndAwaitingPaymentJobs()
    {
        using var scope =
            _factory.Services.CreateScope();

        var dbContext =
            scope.ServiceProvider
                .GetRequiredService<FuaPayDbContext>();

        var repository =
            scope.ServiceProvider
                .GetRequiredService<IJobRepository>();

        var queries =
            scope.ServiceProvider
                .GetRequiredService<IJobQueries>();

        await using var transaction =
            await dbContext.Database
                .BeginTransactionAsync();

        try
        {
            var customerUserId = Guid.NewGuid();
            var requesterUserId = Guid.NewGuid();

            var awaitingPayment = CreateJob(
                customerUserId,
                requesterUserId,
                "Čekající na úhradu",
                TestTime);

            awaitingPayment.Publish(
                TestTime.AddMinutes(1));

            var paid = CreateJob(
                customerUserId,
                requesterUserId,
                "Uhrazená",
                TestTime.AddMinutes(10));

            paid.Publish(
                TestTime.AddMinutes(11));

            paid.ConfirmSettlement(
                JobSettlementType.Credit,
                Guid.NewGuid(),
                TestTime.AddMinutes(12));

            var cancelled = CreateJob(
                customerUserId,
                requesterUserId,
                "Zrušená",
                TestTime.AddMinutes(20));

            cancelled.Publish(
                TestTime.AddMinutes(21));

            cancelled.Cancel(
                TestTime.AddMinutes(22));

            var hiddenDraft = CreateJob(
                customerUserId,
                requesterUserId,
                "Skrytý návrh",
                TestTime.AddMinutes(30));

            var otherCustomerJob = CreateJob(
                Guid.NewGuid(),
                requesterUserId,
                "Cizí zakázka",
                TestTime.AddMinutes(40));

            otherCustomerJob.Publish(
                TestTime.AddMinutes(41));

            await AddJobsAsync(
                repository,
                awaitingPayment,
                paid,
                cancelled,
                hiddenDraft,
                otherCustomerJob);

            var summary =
                await queries.GetCustomerSummaryAsync(
                    customerUserId);

            Assert.Equal(3L, summary.TotalCount);
            Assert.Equal(1L, summary.AwaitingPaymentCount);
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    [Fact]
    public async Task GetManagementSummaryAsync_CountsOwnWorkflowStates()
    {
        using var scope =
            _factory.Services.CreateScope();

        var dbContext =
            scope.ServiceProvider
                .GetRequiredService<FuaPayDbContext>();

        var repository =
            scope.ServiceProvider
                .GetRequiredService<IJobRepository>();

        var queries =
            scope.ServiceProvider
                .GetRequiredService<IJobQueries>();

        await using var transaction =
            await dbContext.Database
                .BeginTransactionAsync();

        try
        {
            var requesterUserId = Guid.NewGuid();
            var customerUserId = Guid.NewGuid();
            var managedServiceUnitId = Guid.NewGuid();

            var draft = CreateJob(
                customerUserId,
                requesterUserId,
                "Návrh",
                TestTime,
                managedServiceUnitId);

            var awaitingPayment = CreateJob(
                customerUserId,
                requesterUserId,
                "Čeká na úhradu",
                TestTime.AddMinutes(10),
                managedServiceUnitId);

            awaitingPayment.Publish(
                TestTime.AddMinutes(11));

            var inProduction = CreateJob(
                customerUserId,
                requesterUserId,
                "Ve výrobě",
                TestTime.AddMinutes(20),
                managedServiceUnitId);

            inProduction.Publish(
                TestTime.AddMinutes(21));

            inProduction.ConfirmSettlement(
                JobSettlementType.Credit,
                Guid.NewGuid(),
                TestTime.AddMinutes(22));

            inProduction.StartProduction(
                TestTime.AddMinutes(23));

            var completed = CreateJob(
                customerUserId,
                requesterUserId,
                "Dokončená",
                TestTime.AddMinutes(30),
                managedServiceUnitId);

            completed.Publish(
                TestTime.AddMinutes(31));

            completed.ConfirmSettlement(
                JobSettlementType.Credit,
                Guid.NewGuid(),
                TestTime.AddMinutes(32));

            completed.StartProduction(
                TestTime.AddMinutes(33));

            completed.MarkReadyForPickup(
                TestTime.AddMinutes(34));

            completed.Complete(
                TestTime.AddMinutes(35));

            var cancelled = CreateJob(
                customerUserId,
                requesterUserId,
                "Zrušená",
                TestTime.AddMinutes(40),
                managedServiceUnitId);

            cancelled.Publish(
                TestTime.AddMinutes(41));

            cancelled.Cancel(
                TestTime.AddMinutes(42));

            var otherRequesterJob = CreateJob(
                customerUserId,
                Guid.NewGuid(),
                "Cizí návrh",
                TestTime.AddMinutes(50));

            await AddJobsAsync(
                repository,
                draft,
                awaitingPayment,
                inProduction,
                completed,
                cancelled,
                otherRequesterJob);

            var summary =
                await queries.GetManagementSummaryAsync(
                    new JobManagementActor(
                        requesterUserId,
                        new[] { managedServiceUnitId }));

            Assert.Equal(5L, summary.TotalCount);
            Assert.Equal(3L, summary.ActiveCount);
            Assert.Equal(1L, summary.AwaitingPaymentCount);
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    [Fact]
    public async Task ListForManagementAsync_RespectsScopeAndFilters()
    {
        using var scope =
            _factory.Services.CreateScope();

        var dbContext =
            scope.ServiceProvider
                .GetRequiredService<FuaPayDbContext>();

        var repository =
            scope.ServiceProvider
                .GetRequiredService<IJobRepository>();

        var queries =
            scope.ServiceProvider
                .GetRequiredService<IJobQueries>();

        await using var transaction =
            await dbContext.Database
                .BeginTransactionAsync();

        try
        {
            var requesterUserId = Guid.NewGuid();
            var otherRequesterUserId = Guid.NewGuid();
            var ownServiceUnitId = Guid.NewGuid();

            var ownDraft = CreateJob(
                Guid.NewGuid(),
                requesterUserId,
                "Vlastní návrh",
                TestTime,
                ownServiceUnitId);

            var ownPaid = CreateJob(
                Guid.NewGuid(),
                requesterUserId,
                "Vlastní uhrazená",
                TestTime.AddMinutes(10),
                ownServiceUnitId);

            ownPaid.Publish(
                TestTime.AddMinutes(11));

            ownPaid.ConfirmSettlement(
                JobSettlementType.Credit,
                Guid.NewGuid(),
                TestTime.AddMinutes(12));

            var otherPublished = CreateJob(
                Guid.NewGuid(),
                otherRequesterUserId,
                "Cizí zveřejněná",
                TestTime.AddMinutes(20));

            otherPublished.Publish(
                TestTime.AddMinutes(21));

            var publishedFilter =
                new JobListFilter(
                    productionStatus:
                        JobProductionStatus.Published);

            var publishedBefore =
                await queries.ListForManagementAsync(
                    new JobManagementActor(
                        Guid.NewGuid(),
                        JobManagementScope.All),
                    publishedFilter,
                    new JobPageRequest());

            await AddJobsAsync(
                repository,
                ownDraft,
                ownPaid,
                otherPublished);

            var ownPaidPage =
                await queries.ListForManagementAsync(
                    new JobManagementActor(
                        requesterUserId,
                        new[] { ownServiceUnitId }),
                    new JobListFilter(
                        paymentStatus:
                            JobPaymentStatus.Paid),
                    new JobPageRequest());

            var ownPaidItem =
                Assert.Single(ownPaidPage.Items);

            Assert.Equal(ownPaid.Id, ownPaidItem.Id);
            Assert.Equal(1L, ownPaidPage.TotalCount);

            var allPublishedPage =
                await queries.ListForManagementAsync(
                    new JobManagementActor(
                        Guid.NewGuid(),
                        JobManagementScope.All),
                    publishedFilter,
                    new JobPageRequest());

            Assert.Equal(
                publishedBefore.TotalCount + 2,
                allPublishedPage.TotalCount);
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    [Fact]
    public async Task ListForManagementAsync_SharedUnitIncludesOtherCreator()
    {
        using var scope =
            _factory.Services.CreateScope();

        var dbContext =
            scope.ServiceProvider
                .GetRequiredService<FuaPayDbContext>();

        var repository =
            scope.ServiceProvider
                .GetRequiredService<IJobRepository>();

        var queries =
            scope.ServiceProvider
                .GetRequiredService<IJobQueries>();

        await using var transaction =
            await dbContext.Database
                .BeginTransactionAsync();

        try
        {
            var serviceUnitId = Guid.NewGuid();
            var firstCreatorId = Guid.NewGuid();
            var secondCreatorId = Guid.NewGuid();

            var firstJob = CreateJob(
                Guid.NewGuid(),
                firstCreatorId,
                "Zakázka první obsluhy",
                TestTime,
                serviceUnitId);

            var secondJob = CreateJob(
                Guid.NewGuid(),
                secondCreatorId,
                "Zakázka druhé obsluhy",
                TestTime.AddMinutes(1),
                serviceUnitId);

            await AddJobsAsync(
                repository,
                firstJob,
                secondJob);

            var page = await queries.ListForManagementAsync(
                new JobManagementActor(
                    firstCreatorId,
                    new[] { serviceUnitId }),
                new JobListFilter(),
                new JobPageRequest());

            Assert.Equal(2L, page.TotalCount);
            Assert.Equal(
                new[] { secondJob.Id, firstJob.Id },
                page.Items.Select(item => item.Id));
            Assert.Contains(
                page.Items,
                item => item.CreatedByUserId == secondCreatorId);
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    [Fact]
    public async Task FindForCustomerAsync_ProtectsVisibilityAndProjectsDetail()
    {
        using var scope =
            _factory.Services.CreateScope();

        var dbContext =
            scope.ServiceProvider
                .GetRequiredService<FuaPayDbContext>();

        var repository =
            scope.ServiceProvider
                .GetRequiredService<IJobRepository>();

        var queries =
            scope.ServiceProvider
                .GetRequiredService<IJobQueries>();

        await using var transaction =
            await dbContext.Database
                .BeginTransactionAsync();

        try
        {
            var customerUserId = Guid.NewGuid();
            var requesterUserId = Guid.NewGuid();
            var settlementReferenceId = Guid.NewGuid();

            var completedJob = new Job(
                Guid.NewGuid(),
                NextJobNumber(),
                Guid.NewGuid(),
                customerUserId,
                requesterUserId,
                ServiceType.Workshop,
                "Dokončený laser",
                "Kompletní detail zakázky",
                new Money(24_500),
                TestTime);

            completedJob.Publish(
                TestTime.AddMinutes(10));

            completedJob.ConfirmSettlement(
                JobSettlementType.Credit,
                settlementReferenceId,
                TestTime.AddMinutes(20));

            completedJob.StartProduction(
                TestTime.AddMinutes(30));

            completedJob.MarkReadyForPickup(
                TestTime.AddMinutes(40));

            completedJob.Complete(
                TestTime.AddMinutes(50));

            var hiddenDraft = CreateJob(
                customerUserId,
                requesterUserId,
                "Skrytý detail návrhu",
                TestTime.AddMinutes(60));

            await AddJobsAsync(
                repository,
                completedJob,
                hiddenDraft);

            var detail =
                Assert.IsType<JobDetail>(
                    await queries.FindForCustomerAsync(
                        customerUserId,
                        completedJob.Id));

            Assert.Equal(completedJob.Id, detail.Id);
            Assert.Equal(customerUserId, detail.CustomerUserId);
            Assert.Equal(
                requesterUserId,
                detail.CreatedByUserId);
            Assert.Equal(
                completedJob.ServiceUnitId,
                detail.ServiceUnitId);
            Assert.Equal(completedJob.Number, detail.Number);
            Assert.Equal(
                ServiceType.Workshop,
                detail.ServiceType);
            Assert.Equal("Dokončený laser", detail.Title);
            Assert.Equal(
                "Kompletní detail zakázky",
                detail.Description);
            Assert.Equal(24_500, detail.PriceMinorUnits);
            Assert.Equal(
                JobProductionStatus.Completed,
                detail.ProductionStatus);
            Assert.Equal(
                JobPaymentStatus.Paid,
                detail.PaymentStatus);
            Assert.Equal<JobSettlementType?>(
                JobSettlementType.Credit,
                detail.SettlementType);
            Assert.Equal<Guid?>(
                settlementReferenceId,
                detail.SettlementReferenceId);
            Assert.Equal<DateTimeOffset?>(
                TestTime.AddMinutes(50),
                detail.CompletedAt);
            Assert.True(detail.Version > 0);

            Assert.Null(
                await queries.FindForCustomerAsync(
                    customerUserId,
                    hiddenDraft.Id));

            Assert.Null(
                await queries.FindForCustomerAsync(
                    Guid.NewGuid(),
                    completedJob.Id));
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    [Fact]
    public async Task FindForManagementAsync_RespectsOwnAndAllScopes()
    {
        using var scope =
            _factory.Services.CreateScope();

        var dbContext =
            scope.ServiceProvider
                .GetRequiredService<FuaPayDbContext>();

        var repository =
            scope.ServiceProvider
                .GetRequiredService<IJobRepository>();

        var queries =
            scope.ServiceProvider
                .GetRequiredService<IJobQueries>();

        await using var transaction =
            await dbContext.Database
                .BeginTransactionAsync();

        try
        {
            var requesterUserId = Guid.NewGuid();

            var job = CreateJob(
                Guid.NewGuid(),
                requesterUserId,
                "Spravovaný návrh",
                TestTime);

            await repository.AddAsync(
                job,
                CancellationToken.None);

            var unrelatedRequester =
                new JobManagementActor(
                    Guid.NewGuid(),
                    new[] { Guid.NewGuid() });

            Assert.Null(
                await queries.FindForManagementAsync(
                    unrelatedRequester,
                    job.Id));

            var adminDetail =
                Assert.IsType<JobDetail>(
                    await queries.FindForManagementAsync(
                        new JobManagementActor(
                            Guid.NewGuid(),
                            JobManagementScope.All),
                        job.Id));

            Assert.Equal(job.Id, adminDetail.Id);
            Assert.Equal(
                JobProductionStatus.Draft,
                adminDetail.ProductionStatus);
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    private static Job CreateJob(
        Guid customerUserId,
        Guid requesterUserId,
        string title,
        DateTimeOffset createdAt,
        Guid? serviceUnitId = null)
    {
        return new Job(
            Guid.NewGuid(),
            NextJobNumber(),
            serviceUnitId ?? Guid.NewGuid(),
            customerUserId,
            requesterUserId,
            ServiceType.ThreeDPrint,
            title,
            $"Popis: {title}",
            new Money(10_000),
            createdAt);
    }

    private static async Task AddJobsAsync(
        IJobRepository repository,
        params Job[] jobs)
    {
        foreach (var job in jobs)
        {
            await repository.AddAsync(
                job,
                CancellationToken.None);
        }
    }
}
