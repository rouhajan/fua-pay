using FuaPay.Web.BuildingBlocks.Application;
using FuaPay.Web.BuildingBlocks.Auditing;
using FuaPay.Web.BuildingBlocks.Domain;
using FuaPay.Web.Modules.Payments.Application;
using FuaPay.Web.Modules.Payments.Domain;
using FuaPay.Web.Modules.Payments.Infrastructure.Csob;

namespace FuaPay.Web.Tests.Modules.Payments.Infrastructure.Csob;

public sealed class CsobPaymentReconciliationServiceTests
{
    private static readonly DateTimeOffset CreatedAt =
        new(2026, 8, 14, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ReconciledAt =
        CreatedAt.AddMinutes(5);

    [Theory]
    [InlineData(7)]
    [InlineData(8)]
    public async Task ReconcileAsync_PaidStatus_UsesSettlementBoundary(
        int gatewayPaymentStatus)
    {
        var payment = CreatePendingPayment();
        var settlement = new RecordingSettlementService(changed: true);
        var service = CreateService(
            payment,
            GatewayStatus(gatewayPaymentStatus),
            settlement);

        var result = await service.ReconcileAsync(
            payment.Id,
            payment.ProviderReference!);

        Assert.Equal(payment.Id, result.PaymentId);
        Assert.Equal(PaymentStatus.Succeeded, result.PaymentStatus);
        Assert.Equal(gatewayPaymentStatus, result.GatewayPaymentStatus);
        Assert.True(result.StateChanged);

        var confirmation = Assert.IsType<VerifiedPaymentConfirmation>(
            settlement.Confirmation);
        Assert.Equal(PaymentProvider.Csob, confirmation.Provider);
        Assert.Equal(
            payment.ProviderReference,
            confirmation.ProviderReference);
        Assert.Equal(payment.Amount, confirmation.Amount);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    public async Task ReconcileAsync_NonTerminalStatus_LeavesPaymentPending(
        int gatewayPaymentStatus)
    {
        var payment = CreatePendingPayment();
        var repository = new StubPaymentRepository(payment);
        var settlement = new RecordingSettlementService(changed: true);
        var service = CreateService(
            repository,
            GatewayStatus(gatewayPaymentStatus),
            settlement);

        var result = await service.ReconcileAsync(
            payment.Id,
            payment.ProviderReference!);

        Assert.Equal(PaymentStatus.Pending, result.PaymentStatus);
        Assert.False(result.StateChanged);
        Assert.Equal(0, repository.SaveCalls);
        Assert.Null(settlement.Confirmation);
    }

    [Fact]
    public async Task ReconcileAsync_CancelledStatus_CancelsPendingPayment()
    {
        var payment = CreatePendingPayment();
        var repository = new StubPaymentRepository(payment);
        var audit = new RecordingAuditTrail();
        var service = CreateService(
            repository,
            GatewayStatus(3),
            new RecordingSettlementService(changed: true),
            audit);

        var result = await service.ReconcileAsync(
            payment.Id,
            payment.ProviderReference!);

        Assert.Equal(PaymentStatus.Cancelled, payment.Status);
        Assert.Equal(PaymentStatus.Cancelled, result.PaymentStatus);
        Assert.True(result.StateChanged);
        Assert.Equal(1, repository.SaveCalls);
        Assert.Equal("payment.cancelled", Assert.Single(audit.Entries).Action);
    }

    [Fact]
    public async Task ReconcileAsync_DeniedStatus_FailsPendingPayment()
    {
        var payment = CreatePendingPayment();
        var repository = new StubPaymentRepository(payment);
        var audit = new RecordingAuditTrail();
        var service = CreateService(
            repository,
            GatewayStatus(6),
            new RecordingSettlementService(changed: true),
            audit);

        var result = await service.ReconcileAsync(
            payment.Id,
            payment.ProviderReference!);

        Assert.Equal(PaymentStatus.Failed, payment.Status);
        Assert.Equal(PaymentStatus.Failed, result.PaymentStatus);
        Assert.True(result.StateChanged);
        Assert.Equal(1, repository.SaveCalls);
        Assert.Equal("payment.failed", Assert.Single(audit.Entries).Action);
    }

    [Fact]
    public async Task ReconcileAsync_NonZeroResultCode_DoesNotMutatePayment()
    {
        var payment = CreatePendingPayment();
        var repository = new StubPaymentRepository(payment);
        var settlement = new RecordingSettlementService(changed: true);
        var service = CreateService(
            repository,
            GatewayStatus(2, resultCode: 140),
            settlement);

        var exception = await Assert.ThrowsAsync<
            CsobPaymentRequiresAttentionException>(
            () => service.ReconcileAsync(
                payment.Id,
                payment.ProviderReference!));

        Assert.Equal(140, exception.ResultCode);
        Assert.Equal(PaymentStatus.Pending, payment.Status);
        Assert.Equal(0, repository.SaveCalls);
        Assert.Null(settlement.Confirmation);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(99)]
    public async Task ReconcileAsync_UnsupportedLifecycleStatus_DoesNotMutatePayment(
        int gatewayPaymentStatus)
    {
        var payment = CreatePendingPayment();
        var repository = new StubPaymentRepository(payment);
        var settlement = new RecordingSettlementService(changed: true);
        var service = CreateService(
            repository,
            GatewayStatus(gatewayPaymentStatus),
            settlement);

        var exception =
            await Assert.ThrowsAsync<CsobPaymentRequiresAttentionException>(
                () => service.ReconcileAsync(
                    payment.Id,
                    payment.ProviderReference!));

        Assert.Equal(
            gatewayPaymentStatus,
            exception.GatewayPaymentStatus);
        Assert.Equal(PaymentStatus.Pending, payment.Status);
        Assert.Equal(0, repository.SaveCalls);
        Assert.Null(settlement.Confirmation);
    }

    [Fact]
    public async Task ReconcileAsync_UnknownLocalPayId_DoesNotCallGateway()
    {
        var gateway = new StubCsobGatewayClient(GatewayStatus(8));
        var service = new CsobPaymentReconciliationService(
            gateway,
            new StubPaymentRepository(payment: null),
            new StubPaymentInitiationRepository(initiation: null),
            new RecordingSettlementService(changed: true),
            new ImmediateTransaction(),
            new FixedTimeProvider(ReconciledAt),
            NullAuditTrail.Instance);

        await Assert.ThrowsAsync<PaymentProviderReferenceNotFoundException>(
            () => service.ReconcileAsync(
                Guid.NewGuid(),
                "unknown-pay-id"));

        Assert.Equal(0, gateway.StatusCalls);
    }

    [Fact]
    public async Task ReconcileAsync_ReplayedCancellation_IsIdempotent()
    {
        var payment = CreatePendingPayment();
        payment.Cancel(ReconciledAt);
        var repository = new StubPaymentRepository(payment);
        var service = CreateService(
            repository,
            GatewayStatus(3),
            new RecordingSettlementService(changed: true));

        var result = await service.ReconcileAsync(
            payment.Id,
            payment.ProviderReference!);

        Assert.Equal(PaymentStatus.Cancelled, result.PaymentStatus);
        Assert.False(result.StateChanged);
        Assert.Equal(0, repository.SaveCalls);
    }

    [Fact]
    public async Task ReconcileAsync_UncertainCandidate_StatusOneInitializesAfterProbe()
    {
        var payment = CreateCreatedPayment();
        var initiation = CreateUncertainInitiation(payment);
        var paymentRepository = new StubPaymentRepository(payment);
        var initiationRepository =
            new StubPaymentInitiationRepository(initiation);
        var audit = new RecordingAuditTrail();
        var service = CreateService(
            paymentRepository,
            GatewayStatus(1),
            new RecordingSettlementService(changed: true),
            audit,
            initiationRepository);

        var result = await service.ReconcileAsync(
            payment.Id,
            initiation.ObservedProviderReference!);

        Assert.Equal(PaymentStatus.Pending, result.PaymentStatus);
        Assert.True(result.StateChanged);
        Assert.Equal(PaymentStatus.Pending, payment.Status);
        Assert.Equal(
            PaymentInitiationState.Initialized,
            initiation.State);
        Assert.Equal(1, paymentRepository.SaveCalls);
        Assert.Equal(1, initiationRepository.SaveCalls);
        Assert.Equal(
            "payment.provider-initiation.status-verified",
            Assert.Single(audit.Entries).Action);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(8)]
    [InlineData(99)]
    public async Task ReconcileAsync_UncertainCandidate_NonPreProcessStatusFailsClosed(
        int gatewayPaymentStatus)
    {
        var payment = CreateCreatedPayment();
        var initiation = CreateUncertainInitiation(payment);
        var paymentRepository = new StubPaymentRepository(payment);
        var initiationRepository =
            new StubPaymentInitiationRepository(initiation);
        var service = CreateService(
            paymentRepository,
            GatewayStatus(gatewayPaymentStatus),
            new RecordingSettlementService(changed: true),
            initiationRepository: initiationRepository);

        await Assert.ThrowsAsync<CsobPaymentRequiresAttentionException>(
            () => service.ReconcileAsync(
                payment.Id,
                initiation.ObservedProviderReference!));

        Assert.Equal(PaymentStatus.Created, payment.Status);
        Assert.Equal(PaymentInitiationState.Uncertain, initiation.State);
        Assert.Equal(0, paymentRepository.SaveCalls);
        Assert.Equal(0, initiationRepository.SaveCalls);
    }

    private static CsobPaymentReconciliationService CreateService(
        Payment payment,
        CsobPaymentStatusResult status,
        IPaymentSettlementService settlementService)
    {
        return CreateService(
            new StubPaymentRepository(payment),
            status,
            settlementService);
    }

    private static CsobPaymentReconciliationService CreateService(
        StubPaymentRepository repository,
        CsobPaymentStatusResult status,
        IPaymentSettlementService settlementService,
        IAuditTrail? auditTrail = null,
        IPaymentInitiationRepository? initiationRepository = null)
    {
        return new CsobPaymentReconciliationService(
            new StubCsobGatewayClient(status),
            repository,
            initiationRepository ??
                new StubPaymentInitiationRepository(initiation: null),
            settlementService,
            new ImmediateTransaction(),
            new FixedTimeProvider(ReconciledAt),
            auditTrail ?? NullAuditTrail.Instance);
    }

    private static CsobPaymentStatusResult GatewayStatus(
        int paymentStatus,
        int resultCode = 0)
    {
        return new CsobPaymentStatusResult(
            "pay1234567890",
            resultCode,
            resultCode == 0 ? "OK" : "ERROR",
            paymentStatus,
            AuthCode: null,
            StatusDetail: null);
    }

    private static Payment CreatePendingPayment()
    {
        var payment = CreateCreatedPayment();

        payment.MarkPending("pay1234567890", CreatedAt);
        return payment;
    }

    private static Payment CreateCreatedPayment()
    {
        return new Payment(
            Guid.NewGuid(),
            Guid.NewGuid(),
            PaymentPurposeType.CreditTopUp,
            jobId: null,
            new Money(25_000),
            PaymentProvider.Csob,
            CreatedAt,
            Guid.NewGuid());
    }

    private static PaymentInitiation CreateUncertainInitiation(
        Payment payment)
    {
        var initiation = new PaymentInitiation(
            payment.Id,
            PaymentProvider.Csob,
            8_900_000_001L,
            Guid.NewGuid(),
            CreatedAt);
        initiation.Begin(CreatedAt.AddSeconds(1));
        initiation.MarkUncertain(
            "candidate pending status verification",
            CreatedAt.AddSeconds(2),
            "pay1234567890",
            new Uri("https://example.test/process/pay1234567890"));
        return initiation;
    }

    private sealed class StubCsobGatewayClient : ICsobGatewayClient
    {
        private readonly CsobPaymentStatusResult _status;

        public StubCsobGatewayClient(CsobPaymentStatusResult status)
        {
            _status = status;
        }

        public int StatusCalls { get; private set; }

        public Task<CsobEchoResult> EchoAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<CsobPaymentInitResult> InitializeAsync(
            CsobPaymentInit payment,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<CsobPaymentStatusResult> GetStatusAsync(
            string payId,
            CancellationToken cancellationToken = default)
        {
            StatusCalls++;
            return Task.FromResult(
                _status with
                {
                    PayId = payId
                });
        }
    }

    private sealed class StubPaymentRepository : IPaymentRepository
    {
        private readonly Payment? _payment;

        public StubPaymentRepository(Payment? payment)
        {
            _payment = payment;
        }

        public int SaveCalls { get; private set; }

        public Task<Payment?> FindByIdAsync(
            Guid paymentId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<Payment?>(
                _payment?.Id == paymentId ? _payment : null);

        public Task<Payment?> FindBlockingForJobAsync(
            Guid jobId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<Payment?>(null);

        public Task<Payment?> FindByProviderReferenceAsync(
            PaymentProvider provider,
            string providerReference,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<Payment?>(
                _payment is not null &&
                _payment.Provider == provider &&
                string.Equals(
                    _payment.ProviderReference,
                    providerReference.Trim(),
                    StringComparison.Ordinal)
                    ? _payment
                    : null);

        public Task<Payment?> FindByCreationRequestIdAsync(
            Guid creationRequestId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<Payment?>(null);

        public Task AddAsync(
            Payment payment,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task AddPreparedAsync(
            Payment payment,
            PaymentInitiation initiation,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task SaveAsync(
            Payment payment,
            CancellationToken cancellationToken = default)
        {
            SaveCalls++;
            return Task.CompletedTask;
        }
    }

    private sealed class StubPaymentInitiationRepository :
        IPaymentInitiationRepository
    {
        private readonly PaymentInitiation? _initiation;

        public StubPaymentInitiationRepository(
            PaymentInitiation? initiation)
        {
            _initiation = initiation;
        }

        public int SaveCalls { get; private set; }

        public Task<PaymentInitiation?> FindByPaymentIdAsync(
            Guid paymentId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<PaymentInitiation?>(
                _initiation?.PaymentId == paymentId
                    ? _initiation
                    : null);

        public Task SaveAsync(
            PaymentInitiation initiation,
            CancellationToken cancellationToken = default)
        {
            SaveCalls++;
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingSettlementService :
        IPaymentSettlementService
    {
        private readonly bool _changed;

        public RecordingSettlementService(bool changed)
        {
            _changed = changed;
        }

        public VerifiedPaymentConfirmation? Confirmation
        {
            get;
            private set;
        }

        public Task<bool> CompleteAsync(
            VerifiedPaymentConfirmation confirmation,
            CancellationToken cancellationToken = default)
        {
            Confirmation = confirmation;
            return Task.FromResult(_changed);
        }
    }

    private sealed class ImmediateTransaction : IApplicationTransaction
    {
        public Task<T> ExecuteAsync<T>(
            Func<CancellationToken, Task<T>> operation,
            CancellationToken cancellationToken = default) =>
            operation(cancellationToken);
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;

        public FixedTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public override DateTimeOffset GetUtcNow() => _utcNow;
    }

    private sealed class RecordingAuditTrail : IAuditTrail
    {
        public List<AuditEntry> Entries { get; } = [];

        public void Stage(AuditEntry entry)
        {
            Entries.Add(entry);
        }

        public Task WriteAsync(
            AuditEntry entry,
            CancellationToken cancellationToken = default)
        {
            Entries.Add(entry);
            return Task.CompletedTask;
        }
    }
}
