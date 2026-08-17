using System.Collections.Concurrent;

using FuaPay.Web.BuildingBlocks.Application;
using FuaPay.Web.BuildingBlocks.Auditing;
using FuaPay.Web.Modules.Payments.Infrastructure.Csob;

namespace FuaPay.Web.Tests.Modules.Payments.Infrastructure.Csob;

public sealed class CsobPaymentRecoverySchedulerTests
{
    private static readonly DateTimeOffset ObservedAt =
        new(2026, 8, 14, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ConcurrentDuplicateReturnsWriteOnlyFirstObservationAudit()
    {
        var paymentId = Guid.NewGuid();
        var repository = new FirstObservationRepository(paymentId);
        var audit = new RecordingAuditTrail();
        var scheduler = new CsobPaymentRecoveryScheduler(
            repository,
            new ImmediateTransaction(),
            new FixedTimeProvider(ObservedAt),
            audit);

        var results = await Task.WhenAll(
            Enumerable.Range(0, 8)
                .Select(_ => scheduler.ScheduleReturnAsync(
                    " pay1234567890 ")));

        Assert.All(results, result => Assert.Equal(paymentId, result));
        Assert.Equal(8, repository.CallCount);
        var entry = Assert.Single(audit.Entries);
        Assert.Equal(
            "payment.reconciliation.return-observed",
            entry.Action);
    }

    private sealed class FirstObservationRepository :
        ICsobPaymentRecoveryRepository
    {
        private readonly Guid _paymentId;
        private int _callCount;

        public FirstObservationRepository(Guid paymentId)
        {
            _paymentId = paymentId;
        }

        public int CallCount => _callCount;

        public Task<CsobBrowserReturnObservation?> ScheduleFromReturnAsync(
            string providerReference,
            DateTimeOffset observedAt,
            CancellationToken cancellationToken = default)
        {
            Assert.Equal("pay1234567890", providerReference);
            var call = Interlocked.Increment(ref _callCount);
            return Task.FromResult<CsobBrowserReturnObservation?>(
                new CsobBrowserReturnObservation(
                    _paymentId,
                    IsFirstObservation: call == 1));
        }

        public Task<int> ScheduleLongOpenPaymentsAsync(
            DateTimeOffset pendingBefore,
            DateTimeOffset scheduledAt,
            int limit,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<int> RegisterStaleInProgressAsync(
            DateTimeOffset staleBefore,
            DateTimeOffset observedAt,
            int limit,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<int> ScheduleRecoverableUncertainAsync(
            DateTimeOffset scheduledAt,
            int limit,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<int> RegisterUnrecoverableUncertainAsync(
            DateTimeOffset observedAt,
            int limit,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<CsobPaymentRecoveryClaim>> ClaimDueAsync(
            DateTimeOffset now,
            TimeSpan leaseDuration,
            int limit,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

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

    private sealed class RecordingAuditTrail : IAuditTrail
    {
        public ConcurrentBag<AuditEntry> Entries { get; } = [];

        public void Stage(AuditEntry entry) => Entries.Add(entry);

        public Task WriteAsync(
            AuditEntry entry,
            CancellationToken cancellationToken = default)
        {
            Entries.Add(entry);
            return Task.CompletedTask;
        }
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _time;

        public FixedTimeProvider(DateTimeOffset time)
        {
            _time = time;
        }

        public override DateTimeOffset GetUtcNow() => _time;
    }

    private sealed class ImmediateTransaction : IApplicationTransaction
    {
        public Task<T> ExecuteAsync<T>(
            Func<CancellationToken, Task<T>> operation,
            CancellationToken cancellationToken = default) =>
            operation(cancellationToken);
    }
}
