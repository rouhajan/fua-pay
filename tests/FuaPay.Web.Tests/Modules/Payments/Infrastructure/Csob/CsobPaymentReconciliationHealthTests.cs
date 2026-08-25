using FuaPay.Web.BuildingBlocks.Application;
using FuaPay.Web.BuildingBlocks.Auditing;
using FuaPay.Web.Modules.Payments.Infrastructure.Csob;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace FuaPay.Web.Tests.Modules.Payments.Infrastructure.Csob;

public sealed class CsobPaymentReconciliationHealthTests
{
    private static readonly DateTimeOffset TestTime =
        new(2026, 8, 25, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Snapshot_DistinguishesLifecycleStates()
    {
        var health = new CsobPaymentReconciliationHealth();
        var pollInterval = TimeSpan.FromSeconds(20);

        var notStarted = health.GetSnapshot(
            TestTime,
            enabled: true,
            pollInterval);

        Assert.Equal(
            CsobPaymentReconciliationHealthStatus.NotStarted,
            notStarted.Status);
        Assert.Null(notStarted.LastSuccessfulCycleAt);
        Assert.Null(notStarted.LastFailedCycleAt);

        health.RecordSuccessfulCycle(TestTime.AddSeconds(1));

        var healthy = health.GetSnapshot(
            TestTime.AddSeconds(2),
            enabled: true,
            pollInterval);

        Assert.Equal(
            CsobPaymentReconciliationHealthStatus.Healthy,
            healthy.Status);
        Assert.Equal(
            TestTime.AddSeconds(1),
            healthy.LastSuccessfulCycleAt);

        health.RecordFailedCycle(
            TestTime.AddSeconds(3),
            new InvalidOperationException("test failure"));

        var failed = health.GetSnapshot(
            TestTime.AddSeconds(4),
            enabled: true,
            pollInterval);

        Assert.Equal(
            CsobPaymentReconciliationHealthStatus.Failed,
            failed.Status);
        Assert.Equal(
            nameof(InvalidOperationException),
            failed.LastErrorType);

        health.RecordSuccessfulCycle(TestTime.AddSeconds(5));

        var recovered = health.GetSnapshot(
            TestTime.AddSeconds(6),
            enabled: true,
            pollInterval);

        Assert.Equal(
            CsobPaymentReconciliationHealthStatus.Healthy,
            recovered.Status);
        Assert.Null(recovered.LastErrorType);

        var stale = health.GetSnapshot(
            TestTime.AddSeconds(66),
            enabled: true,
            pollInterval);

        Assert.Equal(
            CsobPaymentReconciliationHealthStatus.Stale,
            stale.Status);

        var disabled = health.GetSnapshot(
            TestTime.AddSeconds(66),
            enabled: false,
            pollInterval);

        Assert.Equal(
            CsobPaymentReconciliationHealthStatus.Disabled,
            disabled.Status);
    }

    [Fact]
    public async Task SuccessfulIdleWorkerCycle_RecordsPositiveHealthSignal()
    {
        var configuration = new CsobReconciliationConfiguration(
            Enabled: true,
            PollInterval: TimeSpan.FromSeconds(20),
            PendingMinimumAge: TimeSpan.FromSeconds(20),
            LeaseDuration: TimeSpan.FromMinutes(3),
            BaseBackoff: TimeSpan.FromSeconds(15),
            MaximumBackoff: TimeSpan.FromMinutes(3),
            MaximumAttempts: 4,
            BatchSize: 20);
        var timeProvider = new FixedTimeProvider(TestTime);
        var health = new CsobPaymentReconciliationHealth();
        var services = new ServiceCollection();
        services.AddSingleton(configuration);
        services.AddSingleton<TimeProvider>(timeProvider);
        services.AddSingleton<IAuditTrail>(NullAuditTrail.Instance);
        services.AddSingleton<IApplicationTransaction, ImmediateTransaction>();
        services.AddScoped<
            ICsobPaymentRecoveryRepository,
            EmptyRecoveryRepository>();
        services.AddScoped<
            ICsobPaymentReconciliationService,
            UnexpectedReconciliationService>();
        services.AddScoped<CsobPaymentRecoveryProcessor>();
        services.AddSingleton<
            Microsoft.Extensions.Logging.ILogger<
                CsobPaymentRecoveryProcessor>>(
            NullLogger<CsobPaymentRecoveryProcessor>.Instance);

        await using var provider = services.BuildServiceProvider();
        var worker = new CsobPaymentReconciliationWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            configuration,
            health,
            timeProvider,
            NullLogger<CsobPaymentReconciliationWorker>.Instance);

        await worker.RunCycleAsync(CancellationToken.None);

        var snapshot = health.GetSnapshot(
            TestTime,
            enabled: true,
            configuration.PollInterval);

        Assert.Equal(
            CsobPaymentReconciliationHealthStatus.Healthy,
            snapshot.Status);
        Assert.Equal(TestTime, snapshot.LastSuccessfulCycleAt);
        Assert.Null(snapshot.LastFailedCycleAt);
    }

    private sealed class EmptyRecoveryRepository :
        ICsobPaymentRecoveryRepository
    {
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
            CancellationToken cancellationToken = default) =>
            Task.FromResult(0);

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
            Task.FromResult<IReadOnlyList<CsobPaymentRecoveryClaim>>([]);

        public Task<bool> RescheduleAsync(
            CsobPaymentRecoveryClaim claim,
            DateTimeOffset attemptedAt,
            DateTimeOffset nextAttemptAt,
            int? gatewayPaymentStatus,
            int? resultCode,
            string? error,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> MarkRequiresAttentionAsync(
            CsobPaymentRecoveryClaim claim,
            DateTimeOffset attemptedAt,
            int? gatewayPaymentStatus,
            int? resultCode,
            string error,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> MarkCompletedAsync(
            CsobPaymentRecoveryClaim claim,
            DateTimeOffset attemptedAt,
            int gatewayPaymentStatus,
            int resultCode,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class UnexpectedReconciliationService :
        ICsobPaymentReconciliationService
    {
        public Task<CsobPaymentReconciliationResult> ReconcileAsync(
            Guid paymentId,
            string payId,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException(
                "Idle cycle must not contact the gateway.");
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
}
