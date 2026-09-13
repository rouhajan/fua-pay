using FuaPay.Web.BuildingBlocks.Application;
using FuaPay.Web.BuildingBlocks.Auditing;
using FuaPay.Web.BuildingBlocks.Domain;
using FuaPay.Web.BuildingBlocks.Persistence;
using FuaPay.Web.Modules.Jobs.Application;
using FuaPay.Web.Modules.Jobs.Domain;
using FuaPay.Web.Modules.Payments.Application;
using FuaPay.Web.Modules.Payments.Domain;
using FuaPay.Web.Modules.Payments.Infrastructure.Csob;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace FuaPay.DatabaseTests;

public sealed class CsobExpiryReconciliationRacePersistenceTests :
    IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly DateTimeOffset CreatedAt =
        new(2026, 9, 13, 9, 0, 0, TimeSpan.Zero);

    private readonly WebApplicationFactory<Program> _factory;

    public CsobExpiryReconciliationRacePersistenceTests(
        WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task EvidenceBeforeStatusReconciliation_ExpiresWithoutFinancialEffects()
    {
        var payment = CreatePendingTopUp("payevidence001");
        var gateway = new SequencedStatusGateway();

        try
        {
            await AddPaymentAsync(payment);
            await ScheduleAsync(
                CreateVerifiedExpiryReturn(payment.ProviderReference!),
                CreatedAt.AddMinutes(1));

            await RunWorkerAsync(
                gateway,
                CreatedAt.AddMinutes(2));

            Assert.Equal(1, gateway.StatusCalls);
            await AssertExpiredWithoutFinancialEffectsAsync(payment);
        }
        finally
        {
            await DeleteScenarioAsync(payment.Id, jobId: null);
        }
    }

    [Fact]
    public async Task FailedBeforeEvidence_ReopensAndRequiresNewStatusForJobPayment()
    {
        var customerUserId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var payment = await CreatePendingJobPaymentAsync(
            customerUserId,
            jobId,
            "payfailed00001");
        var gateway = new SequencedStatusGateway();

        try
        {
            await AddPaymentAsync(payment);
            await ScheduleAsync(
                CreateVerifiedReturn(payment.ProviderReference!),
                CreatedAt.AddMinutes(1));

            await RunWorkerAsync(gateway, CreatedAt.AddMinutes(2));

            var failed = await FindPaymentAsync(payment.Id);
            Assert.Equal(PaymentStatus.Failed, failed.Status);
            Assert.Equal(
                PaymentFailureProvenance.CsobResult0Status6,
                failed.FailureProvenance);
            Assert.Equal(1, gateway.StatusCalls);

            var observation = await ScheduleAsync(
                CreateVerifiedExpiryReturn(payment.ProviderReference!),
                CreatedAt.AddMinutes(3));
            Assert.True(observation.IsFirstVerifiedExpiryObservation);

            await RunWorkerAsync(gateway, CreatedAt.AddMinutes(4));

            Assert.Equal(2, gateway.StatusCalls);
            await AssertExpiredWithoutFinancialEffectsAsync(payment);
            await AssertJobUnpaidAsync(jobId);
        }
        finally
        {
            await DeleteScenarioAsync(payment.Id, jobId);
        }
    }

    [Fact]
    public async Task EvidenceDuringInFlightClaim_CannotBeCompletedByStaleClaim()
    {
        var payment = CreatePendingTopUp("payinflight001");
        var gateway = new SequencedStatusGateway(blockFirstCall: true);

        try
        {
            await AddPaymentAsync(payment);
            await ScheduleAsync(
                CreateVerifiedReturn(payment.ProviderReference!),
                CreatedAt.AddMinutes(1));

            var firstWorker = RunWorkerAsync(
                gateway,
                CreatedAt.AddMinutes(2));
            await gateway.FirstCallStarted.Task.WaitAsync(
                TimeSpan.FromSeconds(10));

            var observation = await ScheduleAsync(
                CreateVerifiedExpiryReturn(payment.ProviderReference!),
                CreatedAt.AddMinutes(3));
            Assert.True(observation.IsFirstVerifiedExpiryObservation);

            gateway.ReleaseFirstCall.TrySetResult();
            var firstCycle = await firstWorker;

            Assert.Equal(1, firstCycle.RescheduledCount);
            Assert.Equal(0, firstCycle.CompletedCount);
            Assert.Equal(PaymentStatus.Failed,
                (await FindPaymentAsync(payment.Id)).Status);

            await RunWorkerAsync(gateway, CreatedAt.AddMinutes(4));

            Assert.Equal(2, gateway.StatusCalls);
            await AssertExpiredWithoutFinancialEffectsAsync(payment);
        }
        finally
        {
            gateway.ReleaseFirstCall.TrySetResult();
            await DeleteScenarioAsync(payment.Id, jobId: null);
        }
    }

    [Fact]
    public async Task DuplicateExpiryEvidenceAfterCompletedFailure_IsIdempotent()
    {
        var payment = CreatePendingTopUp("payduplicate01");
        var gateway = new SequencedStatusGateway();
        var expiry = CreateVerifiedExpiryReturn(payment.ProviderReference!);

        try
        {
            await AddPaymentAsync(payment);
            await ScheduleAsync(
                CreateVerifiedReturn(payment.ProviderReference!),
                CreatedAt.AddMinutes(1));
            await RunWorkerAsync(gateway, CreatedAt.AddMinutes(2));

            var first = await ScheduleAsync(expiry, CreatedAt.AddMinutes(3));
            var duplicate = await ScheduleAsync(
                expiry,
                CreatedAt.AddMinutes(3).AddSeconds(1));

            Assert.True(first.IsFirstVerifiedExpiryObservation);
            Assert.False(duplicate.IsFirstVerifiedExpiryObservation);
            Assert.Equal(1, await CountRecoveryRowsAsync(payment.Id));

            await RunWorkerAsync(gateway, CreatedAt.AddMinutes(4));

            Assert.Equal(2, gateway.StatusCalls);
            await AssertExpiredWithoutFinancialEffectsAsync(payment);
        }
        finally
        {
            await DeleteScenarioAsync(payment.Id, jobId: null);
        }
    }

    private async Task<CsobPaymentRecoveryCycleResult> RunWorkerAsync(
        ICsobGatewayClient gateway,
        DateTimeOffset now)
    {
        using var scope = _factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        var settlement = new RejectingSettlementService();
        var reconciliation = new CsobPaymentReconciliationService(
            gateway,
            services.GetRequiredService<IPaymentRepository>(),
            services.GetRequiredService<IPaymentInitiationRepository>(),
            services.GetRequiredService<ICsobVerifiedReturnEvidenceReader>(),
            services.GetRequiredService<ICsobExpiryCorrectionGuard>(),
            settlement,
            services.GetRequiredService<IApplicationTransaction>(),
            new FixedTimeProvider(now),
            services.GetRequiredService<IAuditTrail>());
        var processor = new CsobPaymentRecoveryProcessor(
            services.GetRequiredService<ICsobPaymentRecoveryRepository>(),
            reconciliation,
            services.GetRequiredService<IApplicationTransaction>(),
            CreateConfiguration(),
            new FixedTimeProvider(now),
            services.GetRequiredService<IAuditTrail>(),
            NullLogger<CsobPaymentRecoveryProcessor>.Instance);

        var result = await processor.RunOnceAsync();
        Assert.Equal(0, settlement.CallCount);
        return result;
    }

    private async Task<CsobBrowserReturnObservation> ScheduleAsync(
        CsobVerifiedPaymentReturn verifiedReturn,
        DateTimeOffset observedAt)
    {
        using var scope = _factory.Services.CreateScope();
        return Assert.IsType<CsobBrowserReturnObservation>(
            await scope.ServiceProvider
                .GetRequiredService<ICsobPaymentRecoveryRepository>()
                .ScheduleFromReturnAsync(verifiedReturn, observedAt));
    }

    private async Task AddPaymentAsync(Payment payment)
    {
        using var scope = _factory.Services.CreateScope();
        await scope.ServiceProvider
            .GetRequiredService<IPaymentRepository>()
            .AddAsync(payment);
    }

    private async Task<Payment> FindPaymentAsync(Guid paymentId)
    {
        using var scope = _factory.Services.CreateScope();
        return Assert.IsType<Payment>(
            await scope.ServiceProvider
                .GetRequiredService<IPaymentRepository>()
                .FindByIdAsync(paymentId));
    }

    private async Task AssertExpiredWithoutFinancialEffectsAsync(
        Payment payment)
    {
        var persisted = await FindPaymentAsync(payment.Id);
        Assert.Equal(PaymentStatus.Expired, persisted.Status);
        Assert.Null(persisted.FailureProvenance);

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider
            .GetRequiredService<FuaPayDbContext>();
        Assert.False(await dbContext.Database
            .SqlQuery<bool>(
                $"SELECT EXISTS (SELECT 1 FROM credits.movements WHERE operation_id = {payment.Id}) AS \"Value\"")
            .SingleAsync());
        Assert.False(await dbContext.Database
            .SqlQuery<bool>(
                $"SELECT EXISTS (SELECT 1 FROM payments.settlement_returns WHERE original_payment_id = {payment.Id}) AS \"Value\"")
            .SingleAsync());
    }

    private async Task AssertJobUnpaidAsync(Guid jobId)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider
            .GetRequiredService<FuaPayDbContext>();
        var snapshot = await dbContext.Database
            .SqlQuery<JobPaymentSnapshot>(
                $"""
                SELECT
                    payment_status AS "PaymentStatus",
                    settlement_type AS "SettlementType",
                    settlement_reference_id AS "SettlementReferenceId",
                    settled_at AS "SettledAt"
                FROM jobs.jobs
                WHERE id = {jobId}
                """)
            .SingleAsync();

        Assert.Equal((int)JobPaymentStatus.Unpaid, snapshot.PaymentStatus);
        Assert.Null(snapshot.SettlementType);
        Assert.Null(snapshot.SettlementReferenceId);
        Assert.Null(snapshot.SettledAt);
    }

    private async Task<int> CountRecoveryRowsAsync(Guid paymentId)
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider
            .GetRequiredService<FuaPayDbContext>()
            .Database.SqlQuery<int>(
                $"SELECT count(*)::integer AS \"Value\" FROM payments.csob_payment_reconciliation WHERE payment_id = {paymentId}")
            .SingleAsync();
    }

    private async Task<Payment> CreatePendingJobPaymentAsync(
        Guid customerUserId,
        Guid jobId,
        string providerReference)
    {
        using var scope = _factory.Services.CreateScope();
        var price = new Money(42_000);
        var job = new Job(
            jobId,
            TestJobData.NextJobNumber(),
            Guid.NewGuid(),
            customerUserId,
            Guid.NewGuid(),
            ServiceType.ThreeDPrint,
            "CSOB expiry race",
            "Zakázka pro test souběhu expirace.",
            price,
            CreatedAt);
        job.Publish(CreatedAt);
        await scope.ServiceProvider
            .GetRequiredService<IJobRepository>()
            .AddAsync(job, CancellationToken.None);

        var payment = new Payment(
            Guid.NewGuid(),
            customerUserId,
            PaymentPurposeType.Job,
            jobId,
            price,
            PaymentProvider.Csob,
            CreatedAt);
        payment.MarkPending(providerReference, CreatedAt);
        return payment;
    }

    private static Payment CreatePendingTopUp(string providerReference)
    {
        var payment = new Payment(
            Guid.NewGuid(),
            Guid.NewGuid(),
            PaymentPurposeType.CreditTopUp,
            jobId: null,
            new Money(25_000),
            PaymentProvider.Csob,
            CreatedAt,
            Guid.NewGuid());
        payment.MarkPending(providerReference, CreatedAt);
        return payment;
    }

    private async Task DeleteScenarioAsync(Guid paymentId, Guid? jobId)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider
            .GetRequiredService<FuaPayDbContext>();
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM audit.events WHERE entity_type = 'payment' AND entity_id = {paymentId.ToString()}");
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM payments.payments WHERE id = {paymentId}");

        if (jobId.HasValue)
        {
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM jobs.jobs WHERE id = {jobId.Value}");
        }
    }

    private static CsobVerifiedPaymentReturn CreateVerifiedReturn(
        string providerReference) =>
        new(
            providerReference,
            "20260913100000",
            0,
            "OK",
            3,
            AuthCode: null,
            MerchantData: null,
            StatusDetail: null,
            $"{providerReference}|20260913100000|0|OK|3",
            "signature");

    private static CsobVerifiedPaymentReturn CreateVerifiedExpiryReturn(
        string providerReference) =>
        new(
            providerReference,
            "20260913103000",
            130,
            "Session expired",
            6,
            AuthCode: null,
            MerchantData: "AQIDBA==",
            StatusDetail: null,
            $"{providerReference}|20260913103000|130|Session expired|6|AQIDBA==",
            "signature");

    private static CsobReconciliationConfiguration CreateConfiguration() =>
        new(
            Enabled: true,
            PollInterval: TimeSpan.FromSeconds(15),
            PendingMinimumAge: TimeSpan.FromMinutes(30),
            LeaseDuration: TimeSpan.FromMinutes(3),
            BaseBackoff: TimeSpan.FromSeconds(15),
            MaximumBackoff: TimeSpan.FromMinutes(3),
            MaximumAttempts: 14,
            BatchSize: 20);

    private sealed class SequencedStatusGateway : ICsobGatewayClient
    {
        private readonly bool _blockFirstCall;

        public SequencedStatusGateway(bool blockFirstCall = false)
        {
            _blockFirstCall = blockFirstCall;
        }

        public TaskCompletionSource FirstCallStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleaseFirstCall { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int StatusCalls { get; private set; }

        public async Task<CsobPaymentStatusResult> GetStatusAsync(
            string payId,
            CancellationToken cancellationToken = default)
        {
            StatusCalls++;
            if (_blockFirstCall && StatusCalls == 1)
            {
                FirstCallStarted.TrySetResult();
                await ReleaseFirstCall.Task.WaitAsync(cancellationToken);
            }

            return new CsobPaymentStatusResult(
                payId,
                ResultCode: 0,
                ResultMessage: "OK",
                PaymentStatus: 6,
                AuthCode: null,
                StatusDetail: null);
        }

        public Task<CsobEchoResult> EchoAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<CsobEchoResult> EchoPostAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<CsobPaymentInitResult> InitializeAsync(
            CsobPaymentInit payment,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<CsobPaymentReverseResult> ReverseAsync(
            string payId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class RejectingSettlementService : IPaymentSettlementService
    {
        public int CallCount { get; private set; }

        public Task<bool> CompleteAsync(
            VerifiedPaymentConfirmation confirmation,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            throw new InvalidOperationException(
                "Expiry reconciliation must not call settlement.");
        }
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now;

        public FixedTimeProvider(DateTimeOffset now)
        {
            _now = now;
        }

        public override DateTimeOffset GetUtcNow() => _now;
    }

    private sealed record JobPaymentSnapshot(
        int PaymentStatus,
        int? SettlementType,
        Guid? SettlementReferenceId,
        DateTimeOffset? SettledAt);
}
