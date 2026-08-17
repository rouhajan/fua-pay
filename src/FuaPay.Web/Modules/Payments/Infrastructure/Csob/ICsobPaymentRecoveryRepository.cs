namespace FuaPay.Web.Modules.Payments.Infrastructure.Csob;

public interface ICsobPaymentRecoveryRepository
{
    Task<CsobBrowserReturnObservation?> ScheduleFromReturnAsync(
        string providerReference,
        DateTimeOffset observedAt,
        CancellationToken cancellationToken = default);

    Task<int> ScheduleLongOpenPaymentsAsync(
        DateTimeOffset pendingBefore,
        DateTimeOffset scheduledAt,
        int limit,
        CancellationToken cancellationToken = default);

    Task<int> RegisterStaleInProgressAsync(
        DateTimeOffset staleBefore,
        DateTimeOffset observedAt,
        int limit,
        CancellationToken cancellationToken = default);

    Task<int> ScheduleRecoverableUncertainAsync(
        DateTimeOffset scheduledAt,
        int limit,
        CancellationToken cancellationToken = default);

    Task<int> RegisterUnrecoverableUncertainAsync(
        DateTimeOffset observedAt,
        int limit,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CsobPaymentRecoveryClaim>> ClaimDueAsync(
        DateTimeOffset now,
        TimeSpan leaseDuration,
        int limit,
        CancellationToken cancellationToken = default);

    Task<bool> RescheduleAsync(
        CsobPaymentRecoveryClaim claim,
        DateTimeOffset attemptedAt,
        DateTimeOffset nextAttemptAt,
        int? gatewayPaymentStatus,
        int? resultCode,
        string? error,
        CancellationToken cancellationToken = default);

    Task<bool> MarkRequiresAttentionAsync(
        CsobPaymentRecoveryClaim claim,
        DateTimeOffset attemptedAt,
        int? gatewayPaymentStatus,
        int? resultCode,
        string error,
        CancellationToken cancellationToken = default);

    Task<bool> MarkCompletedAsync(
        CsobPaymentRecoveryClaim claim,
        DateTimeOffset attemptedAt,
        int gatewayPaymentStatus,
        int resultCode,
        CancellationToken cancellationToken = default);
}

public sealed record CsobBrowserReturnObservation(
    Guid PaymentId,
    bool IsFirstObservation);

public sealed record CsobPaymentRecoveryClaim(
    Guid PaymentId,
    string ProviderReference,
    int AttemptCount,
    Guid LeaseToken);
