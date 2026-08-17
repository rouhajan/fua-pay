using FuaPay.Web.BuildingBlocks.Application;
using FuaPay.Web.BuildingBlocks.Auditing;
using FuaPay.Web.BuildingBlocks.Domain;
using FuaPay.Web.BuildingBlocks.Notifications;
using FuaPay.Web.BuildingBlocks.Persistence;
using FuaPay.Web.Modules.Audit.Application;
using FuaPay.Web.Modules.Credits.Application;
using FuaPay.Web.Modules.Credits.Domain;
using FuaPay.Web.Modules.Jobs.Application;
using FuaPay.Web.Modules.Jobs.Domain;
using FuaPay.Web.Modules.Notifications.Application;
using FuaPay.Web.Modules.Payments.Application;
using FuaPay.Web.Modules.Payments.Domain;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FuaPay.DatabaseTests;

public sealed class PaymentSettlementPersistenceTests :
    IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly DateTimeOffset TestTime =
        new(2026, 8, 3, 12, 0, 0, TimeSpan.Zero);

    private readonly WebApplicationFactory<Program> _factory;

    public PaymentSettlementPersistenceTests(
        WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task CompleteAsync_TopUpCommitsExactlyOnce()
    {
        var customerUserId = Guid.NewGuid();
        var payment = CreatePendingTopUp(
            customerUserId,
            $"DEV-ATOMIC-TOP-UP-{Guid.NewGuid():N}");

        try
        {
            await AddPaymentAsync(payment);

            using (var scope = _factory.Services.CreateScope())
            {
                var service = scope.ServiceProvider
                    .GetRequiredService<IPaymentSettlementService>();

                var confirmation = CreateConfirmation(payment);

                Assert.True(
                    await service.CompleteAsync(confirmation));
                Assert.False(
                    await service.CompleteAsync(confirmation));
            }

            using var verificationScope =
                _factory.Services.CreateScope();

            var persistedPayment = Assert.IsType<Payment>(
                await verificationScope.ServiceProvider
                    .GetRequiredService<IPaymentRepository>()
                    .FindByIdAsync(payment.Id));

            var account = Assert.IsType<CreditAccount>(
                await verificationScope.ServiceProvider
                    .GetRequiredService<ICreditAccountRepository>()
                    .FindByOwnerIdAsync(
                        customerUserId,
                        CancellationToken.None));

            Assert.Equal(
                PaymentStatus.Succeeded,
                persistedPayment.Status);
            Assert.Equal(payment.Amount, account.Balance);

            var movement = Assert.Single(account.Movements);
            Assert.Equal(payment.Id, movement.OperationId);
            Assert.Equal(CreditMovementType.Credit, movement.Type);
            Assert.Equal(payment.Amount, movement.Amount);

            var audit = await FindAuditAsync(
                verificationScope.ServiceProvider,
                payment.Id);
            var auditItem = Assert.Single(
                audit.Items,
                item => item.Action == "payment.succeeded");
            Assert.Equal(
                "payment-provider",
                auditItem.ActorProcessName);

            var notifications =
                await verificationScope.ServiceProvider
                    .GetRequiredService<INotificationQueries>()
                    .ListRecentAsync();

            var notification = Assert.Single(
                notifications,
                item =>
                    item.RecipientUserId == customerUserId &&
                    item.Type == "payment.succeeded");

            Assert.Contains(
                payment.ProviderReference!,
                notification.Body,
                StringComparison.Ordinal);
        }
        finally
        {
            await DeleteScenarioAsync(
                customerUserId,
                payment.Id,
                jobId: null);
        }
    }

    [Fact]
    public async Task CompleteAsync_ConcurrentDuplicateTopUpSettlesExactlyOnce()
    {
        var customerUserId = Guid.NewGuid();
        var payment = CreatePendingTopUp(
            customerUserId,
            $"DEV-CONCURRENT-TOP-UP-{Guid.NewGuid():N}");

        try
        {
            await AddPaymentAsync(payment);

            using var firstScope = _factory.Services.CreateScope();
            using var secondScope = _factory.Services.CreateScope();

            var confirmation = CreateConfirmation(payment);
            var firstTask = firstScope.ServiceProvider
                .GetRequiredService<IPaymentSettlementService>()
                .CompleteAsync(confirmation);
            var secondTask = secondScope.ServiceProvider
                .GetRequiredService<IPaymentSettlementService>()
                .CompleteAsync(confirmation);

            var results = await Task.WhenAll(firstTask, secondTask);

            Assert.Single(results, result => result);
            Assert.Single(results, result => !result);

            using var verificationScope =
                _factory.Services.CreateScope();

            var persistedPayment = Assert.IsType<Payment>(
                await verificationScope.ServiceProvider
                    .GetRequiredService<IPaymentRepository>()
                    .FindByIdAsync(payment.Id));

            var account = Assert.IsType<CreditAccount>(
                await verificationScope.ServiceProvider
                    .GetRequiredService<ICreditAccountRepository>()
                    .FindByOwnerIdAsync(
                        customerUserId,
                        CancellationToken.None));

            Assert.Equal(
                PaymentStatus.Succeeded,
                persistedPayment.Status);
            Assert.Equal(payment.Amount, account.Balance);
            Assert.Single(account.Movements);
        }
        finally
        {
            await DeleteScenarioAsync(
                customerUserId,
                payment.Id,
                jobId: null);
        }
    }

    [Fact]
    public async Task CompleteAsync_RejectsMismatchedAmountWithoutEffects()
    {
        var customerUserId = Guid.NewGuid();
        var payment = CreatePendingTopUp(
            customerUserId,
            $"DEV-MISMATCH-TOP-UP-{Guid.NewGuid():N}");

        try
        {
            await AddPaymentAsync(payment);

            using (var scope = _factory.Services.CreateScope())
            {
                var service = scope.ServiceProvider
                    .GetRequiredService<IPaymentSettlementService>();
                var mismatched = new VerifiedPaymentConfirmation(
                    payment.Provider,
                    payment.ProviderReference!,
                    new Money(payment.Amount.MinorUnits + 1));

                await Assert.ThrowsAsync<
                    PaymentConfirmationMismatchException>(
                    () => service.CompleteAsync(mismatched));
            }

            using var verificationScope =
                _factory.Services.CreateScope();

            var persistedPayment = Assert.IsType<Payment>(
                await verificationScope.ServiceProvider
                    .GetRequiredService<IPaymentRepository>()
                    .FindByIdAsync(payment.Id));

            var account = await verificationScope.ServiceProvider
                .GetRequiredService<ICreditAccountRepository>()
                .FindByOwnerIdAsync(
                    customerUserId,
                    CancellationToken.None);

            Assert.Equal(
                PaymentStatus.Pending,
                persistedPayment.Status);
            Assert.Null(account);

            var audit = await FindAuditAsync(
                verificationScope.ServiceProvider,
                payment.Id);
            Assert.Empty(audit.Items);

            var notifications =
                await verificationScope.ServiceProvider
                    .GetRequiredService<INotificationQueries>()
                    .ListRecentAsync();

            Assert.DoesNotContain(
                notifications,
                item => item.RecipientUserId == customerUserId);
        }
        finally
        {
            await DeleteScenarioAsync(
                customerUserId,
                payment.Id,
                jobId: null);
        }
    }

    [Fact]
    public async Task CompleteAsync_WhenFinalSaveFails_RollsBackTopUp()
    {
        var customerUserId = Guid.NewGuid();
        var payment = CreatePendingTopUp(
            customerUserId,
            $"DEV-ROLLBACK-TOP-UP-{Guid.NewGuid():N}");

        try
        {
            await AddPaymentAsync(payment);

            using (var scope = _factory.Services.CreateScope())
            {
                var services = scope.ServiceProvider;
                var service = CreateFailingSettlementService(
                    services,
                    TestTime.AddMinutes(30));

                await Assert.ThrowsAsync<TestPaymentSaveException>(
                    () => service.CompleteAsync(
                        CreateConfirmation(payment)));
            }

            using var verificationScope =
                _factory.Services.CreateScope();

            var persistedPayment = Assert.IsType<Payment>(
                await verificationScope.ServiceProvider
                    .GetRequiredService<IPaymentRepository>()
                    .FindByIdAsync(payment.Id));

            var account = await verificationScope.ServiceProvider
                .GetRequiredService<ICreditAccountRepository>()
                .FindByOwnerIdAsync(
                    customerUserId,
                    CancellationToken.None);

            Assert.Equal(
                PaymentStatus.Pending,
                persistedPayment.Status);
            Assert.Null(account);

            var audit = await FindAuditAsync(
                verificationScope.ServiceProvider,
                payment.Id);
            Assert.Empty(audit.Items);

            var notifications =
                await verificationScope.ServiceProvider
                    .GetRequiredService<INotificationQueries>()
                    .ListRecentAsync();

            Assert.DoesNotContain(
                notifications,
                item => item.RecipientUserId == customerUserId);
        }
        finally
        {
            await DeleteScenarioAsync(
                customerUserId,
                payment.Id,
                jobId: null);
        }
    }

    [Fact]
    public async Task CompleteAsync_WhenFinalSaveFails_RollsBackJob()
    {
        var customerUserId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var payment = CreatePendingJobPayment(
            customerUserId,
            jobId,
            $"DEV-ROLLBACK-JOB-{Guid.NewGuid():N}");

        try
        {
            await AddPublishedJobAsync(
                customerUserId,
                jobId,
                payment.Amount);
            await AddPaymentAsync(payment);

            using (var scope = _factory.Services.CreateScope())
            {
                var service = CreateFailingSettlementService(
                    scope.ServiceProvider,
                    TestTime.AddMinutes(30));

                await Assert.ThrowsAsync<TestPaymentSaveException>(
                    () => service.CompleteAsync(
                        CreateConfirmation(payment)));
            }

            using var verificationScope =
                _factory.Services.CreateScope();

            var persistedPayment = Assert.IsType<Payment>(
                await verificationScope.ServiceProvider
                    .GetRequiredService<IPaymentRepository>()
                    .FindByIdAsync(payment.Id));

            var persistedJob = Assert.IsType<Job>(
                await verificationScope.ServiceProvider
                    .GetRequiredService<IJobRepository>()
                    .FindByIdAsync(
                        jobId,
                        CancellationToken.None));

            Assert.Equal(
                PaymentStatus.Pending,
                persistedPayment.Status);
            Assert.Equal(
                JobPaymentStatus.Unpaid,
                persistedJob.PaymentStatus);
            Assert.Null(persistedJob.SettlementType);
            Assert.Null(persistedJob.SettlementReferenceId);
            Assert.Null(persistedJob.SettledAt);

            var paymentAudit = await FindAuditAsync(
                verificationScope.ServiceProvider,
                payment.Id);
            Assert.Empty(paymentAudit.Items);

            var jobAudit = await FindAuditAsync(
                verificationScope.ServiceProvider,
                jobId);
            Assert.Empty(jobAudit.Items);

            var notifications =
                await verificationScope.ServiceProvider
                    .GetRequiredService<INotificationQueries>()
                    .ListRecentAsync();

            Assert.DoesNotContain(
                notifications,
                item => item.RecipientUserId == customerUserId);
        }
        finally
        {
            await DeleteScenarioAsync(
                customerUserId,
                payment.Id,
                jobId);
        }
    }

    [Fact]
    public async Task CompleteAsync_JobPaymentCommitsExactlyOnce()
    {
        var customerUserId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var payment = CreatePendingJobPayment(
            customerUserId,
            jobId,
            $"DEV-ATOMIC-JOB-{Guid.NewGuid():N}");

        try
        {
            await AddPublishedJobAsync(
                customerUserId,
                jobId,
                payment.Amount);
            await AddPaymentAsync(payment);

            using (var scope = _factory.Services.CreateScope())
            {
                var service = scope.ServiceProvider
                    .GetRequiredService<IPaymentSettlementService>();
                var confirmation = CreateConfirmation(payment);

                Assert.True(
                    await service.CompleteAsync(confirmation));
                Assert.False(
                    await service.CompleteAsync(confirmation));
            }

            using var verificationScope =
                _factory.Services.CreateScope();

            var persistedPayment = Assert.IsType<Payment>(
                await verificationScope.ServiceProvider
                    .GetRequiredService<IPaymentRepository>()
                    .FindByIdAsync(payment.Id));

            var persistedJob = Assert.IsType<Job>(
                await verificationScope.ServiceProvider
                    .GetRequiredService<IJobRepository>()
                    .FindByIdAsync(
                        jobId,
                        CancellationToken.None));

            Assert.Equal(
                PaymentStatus.Succeeded,
                persistedPayment.Status);
            Assert.Equal(
                JobPaymentStatus.Paid,
                persistedJob.PaymentStatus);
            Assert.Equal(
                JobSettlementType.DirectPayment,
                persistedJob.SettlementType);
            Assert.Equal(
                payment.Id,
                persistedJob.SettlementReferenceId);

            var paymentAudit = await FindAuditAsync(
                verificationScope.ServiceProvider,
                payment.Id);
            Assert.Single(
                paymentAudit.Items,
                item => item.Action == "payment.succeeded");

            var jobAudit = await FindAuditAsync(
                verificationScope.ServiceProvider,
                jobId);
            Assert.Single(
                jobAudit.Items,
                item => item.Action == "job.settled");

            var notifications =
                await verificationScope.ServiceProvider
                    .GetRequiredService<INotificationQueries>()
                    .ListRecentAsync();

            Assert.Single(
                notifications,
                item =>
                    item.RecipientUserId == customerUserId &&
                    item.Type == "payment.succeeded");
        }
        finally
        {
            await DeleteScenarioAsync(
                customerUserId,
                payment.Id,
                jobId);
        }
    }

    private static PaymentSettlementService
        CreateFailingSettlementService(
            IServiceProvider services,
            DateTimeOffset currentTime)
    {
        return new PaymentSettlementService(
            new ThrowingSavePaymentRepository(
                services.GetRequiredService<IPaymentRepository>()),
            services.GetRequiredService<CreditService>(),
            services.GetRequiredService<JobSettlementService>(),
            services.GetRequiredService<IApplicationTransaction>(),
            new FixedTimeProvider(currentTime),
            services.GetRequiredService<IAuditTrail>(),
            services.GetRequiredService<INotificationOutbox>());
    }

    private async Task AddPaymentAsync(Payment payment)
    {
        using var scope = _factory.Services.CreateScope();

        await scope.ServiceProvider
            .GetRequiredService<IPaymentRepository>()
            .AddAsync(payment);
    }

    private async Task AddPublishedJobAsync(
        Guid customerUserId,
        Guid jobId,
        Money price)
    {
        using var scope = _factory.Services.CreateScope();

        var job = new Job(
            jobId,
            TestJobData.NextJobNumber(),
            Guid.NewGuid(),
            customerUserId,
            Guid.NewGuid(),
            ServiceType.ThreeDPrint,
            "Provider settlement test",
            "Zakázka pro databázový test vypořádání platby.",
            price,
            TestTime);

        job.Publish(TestTime.AddMinutes(5));

        await scope.ServiceProvider
            .GetRequiredService<IJobRepository>()
            .AddAsync(
                job,
                CancellationToken.None);
    }

    private static Payment CreatePendingTopUp(
        Guid customerUserId,
        string providerReference)
    {
        var payment = new Payment(
            Guid.NewGuid(),
            customerUserId,
            PaymentPurposeType.CreditTopUp,
            jobId: null,
            new Money(25_000),
            PaymentProvider.Development,
            TestTime,
            Guid.NewGuid());

        payment.MarkPending(
            providerReference,
            TestTime.AddMinutes(1));

        return payment;
    }

    private static Payment CreatePendingJobPayment(
        Guid customerUserId,
        Guid jobId,
        string providerReference)
    {
        var payment = new Payment(
            Guid.NewGuid(),
            customerUserId,
            PaymentPurposeType.Job,
            jobId,
            new Money(42_000),
            PaymentProvider.Development,
            TestTime);

        payment.MarkPending(
            providerReference,
            TestTime.AddMinutes(1));

        return payment;
    }

    private static VerifiedPaymentConfirmation CreateConfirmation(
        Payment payment)
    {
        return new VerifiedPaymentConfirmation(
            payment.Provider,
            payment.ProviderReference!,
            payment.Amount);
    }

    private static Task<AuditPage> FindAuditAsync(
        IServiceProvider services,
        Guid entityId)
    {
        return services.GetRequiredService<IAuditQueries>()
            .ListAsync(
                new AuditListFilter(Search: entityId.ToString()),
                new AuditPageRequest(limit: 100));
    }

    private async Task DeleteScenarioAsync(
        Guid customerUserId,
        Guid paymentId,
        Guid? jobId)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider
            .GetRequiredService<FuaPayDbContext>();

        await using var transaction =
            await dbContext.Database.BeginTransactionAsync();

        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            DELETE FROM notifications.outbox
            WHERE recipient_user_id = {customerUserId}
            """);

        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            DELETE FROM audit.events
            WHERE entity_id IN
            (
                {paymentId.ToString()},
                {(jobId ?? Guid.Empty).ToString()}
            )
            """);

        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            DELETE FROM payments.payments
            WHERE id = {paymentId}
            """);

        if (jobId.HasValue)
        {
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"""
                DELETE FROM jobs.jobs
                WHERE id = {jobId.Value}
                """);
        }

        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            DELETE FROM credits.movements
            WHERE account_id IN
            (
                SELECT id
                FROM credits.accounts
                WHERE owner_id = {customerUserId}
            )
            """);

        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            DELETE FROM credits.accounts
            WHERE owner_id = {customerUserId}
            """);

        await transaction.CommitAsync();
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

    private sealed class ThrowingSavePaymentRepository :
        IPaymentRepository
    {
        private readonly IPaymentRepository _inner;

        internal ThrowingSavePaymentRepository(
            IPaymentRepository inner)
        {
            _inner = inner;
        }

        public Task<Payment?> FindByIdAsync(
            Guid paymentId,
            CancellationToken cancellationToken = default)
        {
            return _inner.FindByIdAsync(
                paymentId,
                cancellationToken);
        }

        public Task<Payment?> FindBlockingForJobAsync(
            Guid jobId,
            CancellationToken cancellationToken = default)
        {
            return _inner.FindBlockingForJobAsync(
                jobId,
                cancellationToken);
        }

        public Task<Payment?> FindByProviderReferenceAsync(
            PaymentProvider provider,
            string providerReference,
            CancellationToken cancellationToken = default)
        {
            return _inner.FindByProviderReferenceAsync(
                provider,
                providerReference,
                cancellationToken);
        }

        public Task<Payment?> FindByCreationRequestIdAsync(
            Guid creationRequestId,
            CancellationToken cancellationToken = default)
        {
            return _inner.FindByCreationRequestIdAsync(
                creationRequestId,
                cancellationToken);
        }

        public Task AddAsync(
            Payment payment,
            CancellationToken cancellationToken = default)
        {
            return _inner.AddAsync(
                payment,
                cancellationToken);
        }

        public Task AddPreparedAsync(
            Payment payment,
            PaymentInitiation initiation,
            CancellationToken cancellationToken = default)
        {
            return _inner.AddPreparedAsync(
                payment,
                initiation,
                cancellationToken);
        }

        public Task SaveAsync(
            Payment payment,
            CancellationToken cancellationToken = default)
        {
            throw new TestPaymentSaveException();
        }
    }

    private sealed class TestPaymentSaveException : Exception
    {
    }
}
