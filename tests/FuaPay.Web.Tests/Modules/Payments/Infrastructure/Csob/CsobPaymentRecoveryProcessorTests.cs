using FuaPay.Web.BuildingBlocks.Application;
using FuaPay.Web.BuildingBlocks.Auditing;
using FuaPay.Web.Modules.Payments.Application;
using FuaPay.Web.Modules.Payments.Domain;
using FuaPay.Web.Modules.Payments.Infrastructure.Csob;

using Microsoft.Extensions.Logging.Abstractions;

namespace FuaPay.Web.Tests.Modules.Payments.Infrastructure.Csob;

public sealed class CsobPaymentRecoveryProcessorTests
{
    private static readonly DateTimeOffset TestTime =
        new(2026, 8, 14, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RunOnceAsync_PendingPayment_IsRescheduledWithBackoff()
    {
        var paymentId = Guid.NewGuid();
        var claim = CreateClaim(paymentId, attemptCount: 0);
        var repository = new RecordingRecoveryRepository(claim);
        var processor = CreateProcessor(
            repository,
            new StubReconciliationService(
                new CsobPaymentReconciliationResult(
                    paymentId,
                    PaymentStatus.Pending,
                    GatewayPaymentStatus: 2,
                    StateChanged: false)),
            maximumAttempts: 4);

        var result = await processor.RunOnceAsync();

        Assert.Equal(1, result.ClaimedCount);
        Assert.Equal(1, result.RescheduledCount);
        Assert.Equal(0, result.CompletedCount);
        Assert.Equal(0, result.RequiresAttentionCount);
        Assert.Equal(0, result.LostClaimCount);
        var reschedule = Assert.IsType<RescheduleCall>(repository.Reschedule);
        Assert.Equal(TestTime, reschedule.AttemptedAt);
        Assert.Equal(TestTime.AddSeconds(15), reschedule.NextAttemptAt);
        Assert.Equal(2, reschedule.GatewayPaymentStatus);
    }

    [Fact]
    public async Task RunOnceAsync_TerminalVerifiedPayment_CompletesRecoveryItem()
    {
        var paymentId = Guid.NewGuid();
        var claim = CreateClaim(paymentId, attemptCount: 0);
        var repository = new RecordingRecoveryRepository(claim);
        var processor = CreateProcessor(
            repository,
            new StubReconciliationService(
                new CsobPaymentReconciliationResult(
                    paymentId,
                    PaymentStatus.Succeeded,
                    GatewayPaymentStatus: 8,
                    StateChanged: true)),
            maximumAttempts: 4);

        var result = await processor.RunOnceAsync();

        Assert.Equal(1, result.CompletedCount);
        Assert.Equal(0, result.RescheduledCount);
        Assert.Equal(0, result.RequiresAttentionCount);
        Assert.Equal(0, result.LostClaimCount);
        var completed = Assert.IsType<CompletedCall>(repository.Completed);
        Assert.Equal(8, completed.GatewayPaymentStatus);
    }

    [Fact]
    public async Task RunOnceAsync_LostClaim_DoesNotReportCompletedTransition()
    {
        var paymentId = Guid.NewGuid();
        var claim = CreateClaim(paymentId, attemptCount: 0);
        var repository = new RecordingRecoveryRepository(claim)
        {
            TransitionSucceeds = false
        };
        var processor = CreateProcessor(
            repository,
            new StubReconciliationService(
                new CsobPaymentReconciliationResult(
                    paymentId,
                    PaymentStatus.Succeeded,
                    GatewayPaymentStatus: 8,
                    StateChanged: false)),
            maximumAttempts: 4);

        var result = await processor.RunOnceAsync();

        Assert.Equal(1, result.ClaimedCount);
        Assert.Equal(1, result.LostClaimCount);
        Assert.Equal(0, result.CompletedCount);
        Assert.Equal(0, result.RescheduledCount);
        Assert.Equal(0, result.RequiresAttentionCount);
        Assert.NotNull(repository.Completed);
        Assert.Null(repository.Reschedule);
        Assert.Null(repository.Attention);
    }

    [Fact]
    public async Task RunOnceAsync_UnsupportedVerifiedLifecycle_RequiresAttention()
    {
        var paymentId = Guid.NewGuid();
        var claim = CreateClaim(paymentId, attemptCount: 0);
        var repository = new RecordingRecoveryRepository(claim);
        var processor = CreateProcessor(
            repository,
            new StubReconciliationService(
                new CsobPaymentRequiresAttentionException(
                    "refund state",
                    gatewayPaymentStatus: 9)),
            maximumAttempts: 4);

        var result = await processor.RunOnceAsync();

        Assert.Equal(1, result.RequiresAttentionCount);
        Assert.Null(repository.Reschedule);
        var attention = Assert.IsType<AttentionCall>(repository.Attention);
        Assert.Equal(9, attention.GatewayPaymentStatus);
        Assert.Equal(
            "Ověřený stav ČSOB vyžaduje ruční provozní kontrolu.",
            attention.Error);
    }

    [Fact]
    public async Task RunOnceAsync_TransientFailureAtAttemptLimit_RequiresAttention()
    {
        var paymentId = Guid.NewGuid();
        var claim = CreateClaim(paymentId, attemptCount: 2);
        var repository = new RecordingRecoveryRepository(claim);
        var processor = CreateProcessor(
            repository,
            new StubReconciliationService(
                new CsobGatewayException(
                    "temporary provider failure",
                    innerException: new HttpRequestException(
                        "temporary provider failure"))),
            maximumAttempts: 3);

        var result = await processor.RunOnceAsync();

        Assert.Equal(1, result.RequiresAttentionCount);
        Assert.Null(repository.Reschedule);
        var attention = Assert.IsType<AttentionCall>(repository.Attention);
        Assert.Null(attention.ResultCode);
        Assert.Equal(
            "Reconciliation vyčerpala automatický limit pokusů.",
            attention.Error);
    }

    [Fact]
    public async Task RunOnceAsync_NonTransientProtocolFailureRequiresAttentionImmediately()
    {
        var paymentId = Guid.NewGuid();
        var claim = CreateClaim(paymentId, attemptCount: 0);
        var repository = new RecordingRecoveryRepository(claim);
        var processor = CreateProcessor(
            repository,
            new StubReconciliationService(
                new CsobGatewayException(
                    "invalid signed response")),
            maximumAttempts: 4);

        var result = await processor.RunOnceAsync();

        Assert.Equal(1, result.RequiresAttentionCount);
        Assert.Null(repository.Reschedule);
        Assert.IsType<AttentionCall>(repository.Attention);
    }

    [Fact]
    public async Task RunOnceAsync_WithoutDueWork_DoesNotReconcile()
    {
        var repository = new RecordingRecoveryRepository();
        var reconciliation = new CountingReconciliationService();
        var processor = CreateProcessor(
            repository,
            reconciliation,
            maximumAttempts: 3);

        var result = await processor.RunOnceAsync();

        Assert.Equal(0, result.ClaimedCount);
        Assert.Equal(0, reconciliation.CallCount);
    }

    [Fact]
    public async Task RunOnceAsync_RegistersStaleInProgressBeforeRecovery()
    {
        var repository = new RecordingRecoveryRepository(staleCount: 2);
        var processor = CreateProcessor(
            repository,
            new StubReconciliationService(
                new InvalidOperationException("no claims expected")),
            maximumAttempts: 3);

        var result = await processor.RunOnceAsync();

        Assert.Equal(2, result.StaleInProgressCount);
        Assert.Equal(TestTime.AddMinutes(-1), repository.StaleBefore);
        Assert.Equal(TestTime, repository.StaleObservedAt);
    }

    private static CsobPaymentRecoveryProcessor CreateProcessor(
        RecordingRecoveryRepository repository,
        ICsobPaymentReconciliationService reconciliationService,
        int maximumAttempts)
    {
        return new CsobPaymentRecoveryProcessor(
            repository,
            reconciliationService,
            new ImmediateTransaction(),
            new CsobReconciliationConfiguration(
                Enabled: true,
                PollInterval: TimeSpan.FromSeconds(15),
                PendingMinimumAge: TimeSpan.FromSeconds(15),
                LeaseDuration: TimeSpan.FromMinutes(3),
                BaseBackoff: TimeSpan.FromSeconds(15),
                MaximumBackoff: TimeSpan.FromMinutes(3),
                MaximumAttempts: maximumAttempts,
                BatchSize: 20),
            new FixedTimeProvider(TestTime),
            NullAuditTrail.Instance,
            NullLogger<CsobPaymentRecoveryProcessor>.Instance);
    }

    private static CsobPaymentRecoveryClaim CreateClaim(
        Guid paymentId,
        int attemptCount)
    {
        return new CsobPaymentRecoveryClaim(
            paymentId,
            "pay1234567890",
            attemptCount,
            Guid.NewGuid());
    }

    private sealed class StubReconciliationService :
        ICsobPaymentReconciliationService
    {
        private readonly CsobPaymentReconciliationResult? _result;
        private readonly Exception? _exception;

        public StubReconciliationService(
            CsobPaymentReconciliationResult result)
        {
            _result = result;
        }

        public StubReconciliationService(Exception exception)
        {
            _exception = exception;
        }

        public Task<CsobPaymentReconciliationResult> ReconcileAsync(
            Guid paymentId,
            string payId,
            CancellationToken cancellationToken = default)
        {
            if (_exception is not null)
            {
                return Task.FromException<CsobPaymentReconciliationResult>(
                    _exception);
            }

            return Task.FromResult(
                _result ?? throw new InvalidOperationException());
        }
    }

    private sealed class CountingReconciliationService :
        ICsobPaymentReconciliationService
    {
        public int CallCount { get; private set; }

        public Task<CsobPaymentReconciliationResult> ReconcileAsync(
            Guid paymentId,
            string payId,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            throw new InvalidOperationException(
                "Reconciliation nesmí bez due work proběhnout.");
        }
    }

    private sealed class RecordingRecoveryRepository :
        ICsobPaymentRecoveryRepository
    {
        private readonly IReadOnlyList<CsobPaymentRecoveryClaim> _claims;

        public RecordingRecoveryRepository(
            params CsobPaymentRecoveryClaim[] claims)
        {
            _claims = claims;
        }

        public RecordingRecoveryRepository(int staleCount)
        {
            _claims = [];
            StaleCount = staleCount;
        }

        public int StaleCount { get; }

        public bool TransitionSucceeds { get; set; } = true;

        public DateTimeOffset? StaleBefore { get; private set; }

        public DateTimeOffset? StaleObservedAt { get; private set; }

        public RescheduleCall? Reschedule { get; private set; }

        public AttentionCall? Attention { get; private set; }

        public CompletedCall? Completed { get; private set; }

        public Task<CsobBrowserReturnObservation?> ScheduleFromReturnAsync(
            string providerReference,
            DateTimeOffset observedAt,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<CsobBrowserReturnObservation?>(null);

        public Task<int> ScheduleLongOpenPaymentsAsync(
            DateTimeOffset pendingBefore,
            DateTimeOffset scheduledAt,
            int limit,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(0);

        public Task<int> RegisterStaleInProgressAsync(
            DateTimeOffset staleBefore,
            DateTimeOffset observedAt,
            int limit,
            CancellationToken cancellationToken = default)
        {
            StaleBefore = staleBefore;
            StaleObservedAt = observedAt;
            return Task.FromResult(StaleCount);
        }

        public Task<int> ScheduleRecoverableUncertainAsync(
            DateTimeOffset scheduledAt,
            int limit,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(0);

        public Task<int> RegisterUnrecoverableUncertainAsync(
            DateTimeOffset observedAt,
            int limit,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(0);

        public Task<IReadOnlyList<CsobPaymentRecoveryClaim>> ClaimDueAsync(
            DateTimeOffset now,
            TimeSpan leaseDuration,
            int limit,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_claims);

        public Task<bool> RescheduleAsync(
            CsobPaymentRecoveryClaim claim,
            DateTimeOffset attemptedAt,
            DateTimeOffset nextAttemptAt,
            int? gatewayPaymentStatus,
            int? resultCode,
            string? error,
            CancellationToken cancellationToken = default)
        {
            Reschedule = new RescheduleCall(
                attemptedAt,
                nextAttemptAt,
                gatewayPaymentStatus,
                resultCode,
                error);
            return Task.FromResult(TransitionSucceeds);
        }

        public Task<bool> MarkRequiresAttentionAsync(
            CsobPaymentRecoveryClaim claim,
            DateTimeOffset attemptedAt,
            int? gatewayPaymentStatus,
            int? resultCode,
            string error,
            CancellationToken cancellationToken = default)
        {
            Attention = new AttentionCall(
                attemptedAt,
                gatewayPaymentStatus,
                resultCode,
                error);
            return Task.FromResult(TransitionSucceeds);
        }

        public Task<bool> MarkCompletedAsync(
            CsobPaymentRecoveryClaim claim,
            DateTimeOffset attemptedAt,
            int gatewayPaymentStatus,
            int resultCode,
            CancellationToken cancellationToken = default)
        {
            Completed = new CompletedCall(
                attemptedAt,
                gatewayPaymentStatus,
                resultCode);
            return Task.FromResult(TransitionSucceeds);
        }
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

    private sealed class ImmediateTransaction : IApplicationTransaction
    {
        public Task<T> ExecuteAsync<T>(
            Func<CancellationToken, Task<T>> operation,
            CancellationToken cancellationToken = default) =>
            operation(cancellationToken);
    }

    private sealed record RescheduleCall(
        DateTimeOffset AttemptedAt,
        DateTimeOffset NextAttemptAt,
        int? GatewayPaymentStatus,
        int? ResultCode,
        string? Error);

    private sealed record AttentionCall(
        DateTimeOffset AttemptedAt,
        int? GatewayPaymentStatus,
        int? ResultCode,
        string Error);

    private sealed record CompletedCall(
        DateTimeOffset AttemptedAt,
        int GatewayPaymentStatus,
        int ResultCode);
}
