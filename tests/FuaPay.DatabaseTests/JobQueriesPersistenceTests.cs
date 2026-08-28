using FuaPay.Web.BuildingBlocks.Domain;
using FuaPay.Web.BuildingBlocks.Persistence;
using FuaPay.Web.Modules.Jobs.Application;
using FuaPay.Web.Modules.Jobs.Domain;
using FuaPay.Web.Modules.Payments.Application;
using FuaPay.Web.Modules.Payments.Domain;

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
    public async Task JobQueries_ProjectOnlyCompletedJobReturns()
    {
        using var scope =
            _factory.Services.CreateScope();

        var dbContext =
            scope.ServiceProvider
                .GetRequiredService<FuaPayDbContext>();

        var jobRepository =
            scope.ServiceProvider
                .GetRequiredService<IJobRepository>();

        var returnRepository =
            scope.ServiceProvider
                .GetRequiredService<ISettlementReturnRepository>();

        var paymentRepository =
            scope.ServiceProvider
                .GetRequiredService<IPaymentRepository>();

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
            var administratorUserId = Guid.NewGuid();
            var serviceUnitId = Guid.NewGuid();

            var withoutReturn = CreatePaidJob(
                customerUserId,
                requesterUserId,
                serviceUnitId,
                "Bez vratky",
                JobSettlementType.Credit,
                Guid.NewGuid(),
                TestTime);

            var completedCredit = CreatePaidJob(
                customerUserId,
                requesterUserId,
                serviceUnitId,
                "Vrácená kreditní",
                JobSettlementType.Credit,
                Guid.NewGuid(),
                TestTime.AddMinutes(20));

            completedCredit.StartProduction(
                TestTime.AddMinutes(23));
            completedCredit.MarkReadyForPickup(
                TestTime.AddMinutes(24));
            completedCredit.Complete(
                TestTime.AddMinutes(25));

            var cardPaymentId = Guid.NewGuid();
            var completedCard = CreatePaidJob(
                customerUserId,
                requesterUserId,
                serviceUnitId,
                "Vrácená kartou",
                JobSettlementType.DirectPayment,
                cardPaymentId,
                TestTime.AddMinutes(40));

            var requested = CreatePaidJob(
                customerUserId,
                requesterUserId,
                serviceUnitId,
                "Požadovaná vratka",
                JobSettlementType.Credit,
                Guid.NewGuid(),
                TestTime.AddMinutes(60));

            var inProgress = CreatePaidJob(
                customerUserId,
                requesterUserId,
                serviceUnitId,
                "Zpracovávaná vratka",
                JobSettlementType.Credit,
                Guid.NewGuid(),
                TestTime.AddMinutes(80));

            var requiresAttention = CreatePaidJob(
                customerUserId,
                requesterUserId,
                serviceUnitId,
                "Vratka vyžaduje kontrolu",
                JobSettlementType.Credit,
                Guid.NewGuid(),
                TestTime.AddMinutes(100));

            var rejected = CreatePaidJob(
                customerUserId,
                requesterUserId,
                serviceUnitId,
                "Zamítnutá vratka",
                JobSettlementType.Credit,
                Guid.NewGuid(),
                TestTime.AddMinutes(120));

            await AddJobsAsync(
                jobRepository,
                withoutReturn,
                completedCredit,
                completedCard,
                requested,
                inProgress,
                requiresAttention,
                rejected);

            var cardPayment = CreateSucceededPayment(
                cardPaymentId,
                customerUserId,
                PaymentPurposeType.Job,
                completedCard.Id,
                TestTime.AddMinutes(40));

            var topUpPayment = CreateSucceededPayment(
                Guid.NewGuid(),
                customerUserId,
                PaymentPurposeType.CreditTopUp,
                jobId: null,
                TestTime.AddMinutes(140));

            await paymentRepository.AddAsync(
                cardPayment,
                CancellationToken.None);
            await paymentRepository.AddAsync(
                topUpPayment,
                CancellationToken.None);

            var completedCreditReturn = CreateSettlementReturn(
                SettlementReturnKind.CreditJob,
                completedCredit.Id,
                originalPaymentId: null,
                customerUserId,
                administratorUserId,
                SettlementReturnState.Completed,
                TestTime.AddMinutes(26));

            var completedCardReturn = CreateSettlementReturn(
                SettlementReturnKind.CardJob,
                completedCard.Id,
                cardPaymentId,
                customerUserId,
                administratorUserId,
                SettlementReturnState.Completed,
                TestTime.AddMinutes(46));

            var requestedReturn = CreateSettlementReturn(
                SettlementReturnKind.CreditJob,
                requested.Id,
                originalPaymentId: null,
                customerUserId,
                administratorUserId,
                SettlementReturnState.Requested,
                TestTime.AddMinutes(66));

            var inProgressReturn = CreateSettlementReturn(
                SettlementReturnKind.CreditJob,
                inProgress.Id,
                originalPaymentId: null,
                customerUserId,
                administratorUserId,
                SettlementReturnState.InProgress,
                TestTime.AddMinutes(86));

            var attentionReturn = CreateSettlementReturn(
                SettlementReturnKind.CreditJob,
                requiresAttention.Id,
                originalPaymentId: null,
                customerUserId,
                administratorUserId,
                SettlementReturnState.RequiresAttention,
                TestTime.AddMinutes(106));

            var rejectedReturn = CreateSettlementReturn(
                SettlementReturnKind.CreditJob,
                rejected.Id,
                originalPaymentId: null,
                customerUserId,
                administratorUserId,
                SettlementReturnState.Rejected,
                TestTime.AddMinutes(126));

            var topUpReturn = CreateSettlementReturn(
                SettlementReturnKind.CardTopUp,
                jobId: null,
                topUpPayment.Id,
                customerUserId,
                administratorUserId,
                SettlementReturnState.Completed,
                TestTime.AddMinutes(146));

            foreach (var settlementReturn in new[]
                     {
                         completedCreditReturn,
                         completedCardReturn,
                         requestedReturn,
                         inProgressReturn,
                         attentionReturn,
                         rejectedReturn,
                         topUpReturn
                     })
            {
                await returnRepository.AddAsync(
                    settlementReturn,
                    CancellationToken.None);
            }

            var customerPage = await queries.ListForCustomerAsync(
                customerUserId,
                new JobListFilter(
                    paymentStatus: JobPaymentStatus.Paid),
                new JobPageRequest(limit: 20));

            Assert.Equal(7L, customerPage.TotalCount);
            Assert.All(
                customerPage.Items,
                item => Assert.Equal(
                    JobPaymentStatus.Paid,
                    item.PaymentStatus));

            var items = customerPage.Items.ToDictionary(item => item.Id);

            Assert.Null(items[withoutReturn.Id].SettlementReturnId);
            Assert.Null(items[withoutReturn.Id].ReturnedAt);
            Assert.Equal(
                completedCreditReturn.Id,
                items[completedCredit.Id].SettlementReturnId);
            Assert.Equal(
                completedCreditReturn.CompletedAt,
                items[completedCredit.Id].ReturnedAt);
            Assert.Equal(
                completedCardReturn.Id,
                items[completedCard.Id].SettlementReturnId);
            Assert.Equal(
                completedCardReturn.CompletedAt,
                items[completedCard.Id].ReturnedAt);
            Assert.Null(items[requested.Id].SettlementReturnId);
            Assert.Null(items[inProgress.Id].SettlementReturnId);
            Assert.Null(items[requiresAttention.Id].SettlementReturnId);
            Assert.Null(items[rejected.Id].SettlementReturnId);

            var creditDetail =
                Assert.IsType<JobDetail>(
                    await queries.FindForCustomerAsync(
                        customerUserId,
                        completedCredit.Id));

            Assert.Equal(
                JobPaymentStatus.Paid,
                creditDetail.PaymentStatus);
            Assert.Equal<JobSettlementType?>(
                JobSettlementType.Credit,
                creditDetail.SettlementType);
            Assert.Equal<Guid?>(
                completedCredit.SettlementReferenceId,
                creditDetail.SettlementReferenceId);
            Assert.Equal<DateTimeOffset?>(
                completedCredit.SettledAt,
                creditDetail.SettledAt);
            Assert.Equal(
                JobProductionStatus.Completed,
                creditDetail.ProductionStatus);
            Assert.Equal<DateTimeOffset?>(
                completedCredit.ProductionStartedAt,
                creditDetail.ProductionStartedAt);
            Assert.Equal<DateTimeOffset?>(
                completedCredit.ReadyForPickupAt,
                creditDetail.ReadyForPickupAt);
            Assert.Equal<DateTimeOffset?>(
                completedCredit.CompletedAt,
                creditDetail.CompletedAt);
            Assert.Equal(
                completedCreditReturn.Id,
                creditDetail.SettlementReturnId);
            Assert.Equal(
                completedCreditReturn.CompletedAt,
                creditDetail.ReturnedAt);

            var cardDetail =
                Assert.IsType<JobDetail>(
                    await queries.FindForManagementAsync(
                        new JobManagementActor(
                            requesterUserId,
                            new[] { serviceUnitId }),
                        completedCard.Id));

            Assert.Equal<JobSettlementType?>(
                JobSettlementType.DirectPayment,
                cardDetail.SettlementType);
            Assert.Equal<Guid?>(
                cardPaymentId,
                cardDetail.SettlementReferenceId);
            Assert.Equal(
                completedCardReturn.Id,
                cardDetail.SettlementReturnId);

            var managementPage =
                await queries.ListForManagementAsync(
                    new JobManagementActor(
                        requesterUserId,
                        new[] { serviceUnitId }),
                    new JobListFilter(
                        paymentStatus: JobPaymentStatus.Paid),
                    new JobPageRequest(limit: 20));

            Assert.Equal(7L, managementPage.TotalCount);

            var outsideScope =
                await queries.ListForManagementAsync(
                    new JobManagementActor(
                        requesterUserId,
                        new[] { Guid.NewGuid() }),
                    new JobListFilter(),
                    new JobPageRequest());

            Assert.Equal(0L, outsideScope.TotalCount);
            Assert.Null(
                await queries.FindForCustomerAsync(
                    Guid.NewGuid(),
                    completedCredit.Id));

            var customerSummary =
                await queries.GetCustomerSummaryAsync(
                    customerUserId);

            Assert.Equal(7L, customerSummary.TotalCount);
            Assert.Equal(0L, customerSummary.AwaitingPaymentCount);
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

    private static Job CreatePaidJob(
        Guid customerUserId,
        Guid requesterUserId,
        Guid serviceUnitId,
        string title,
        JobSettlementType settlementType,
        Guid settlementReferenceId,
        DateTimeOffset createdAt)
    {
        var job = CreateJob(
            customerUserId,
            requesterUserId,
            title,
            createdAt,
            serviceUnitId);

        job.Publish(createdAt.AddMinutes(1));
        job.ConfirmSettlement(
            settlementType,
            settlementReferenceId,
            createdAt.AddMinutes(2));

        return job;
    }

    private static SettlementReturn CreateSettlementReturn(
        SettlementReturnKind kind,
        Guid? jobId,
        Guid? originalPaymentId,
        Guid customerUserId,
        Guid administratorUserId,
        SettlementReturnState state,
        DateTimeOffset requestedAt)
    {
        var settlementReturn = new SettlementReturn(
            Guid.NewGuid(),
            Guid.NewGuid(),
            kind,
            originalPaymentId,
            jobId,
            customerUserId,
            administratorUserId,
            new Money(10_000),
            "Test projekce vratky",
            requestedAt);

        if (state != SettlementReturnState.Requested)
        {
            settlementReturn.Begin(requestedAt.AddMinutes(1));
        }

        switch (state)
        {
            case SettlementReturnState.Requested:
            case SettlementReturnState.InProgress:
                break;
            case SettlementReturnState.Completed:
                settlementReturn.Complete(
                    requestedAt.AddMinutes(2));
                break;
            case SettlementReturnState.Rejected:
                settlementReturn.Reject(
                    requestedAt.AddMinutes(2));
                break;
            case SettlementReturnState.RequiresAttention:
                settlementReturn.RequireAttention(
                    requestedAt.AddMinutes(2));
                break;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(state));
        }

        return settlementReturn;
    }

    private static Payment CreateSucceededPayment(
        Guid paymentId,
        Guid customerUserId,
        PaymentPurposeType purposeType,
        Guid? jobId,
        DateTimeOffset createdAt)
    {
        var payment = new Payment(
            paymentId,
            customerUserId,
            purposeType,
            jobId,
            new Money(10_000),
            PaymentProvider.Development,
            createdAt,
            purposeType == PaymentPurposeType.CreditTopUp
                ? Guid.NewGuid()
                : null);

        payment.MarkPending(
            $"DEV-JOB-RETURN-{Guid.NewGuid():N}",
            createdAt.AddMinutes(1));
        payment.Complete(createdAt.AddMinutes(2));

        return payment;
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
