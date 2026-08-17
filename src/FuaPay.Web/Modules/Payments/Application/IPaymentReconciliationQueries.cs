using FuaPay.Web.Modules.Payments.Domain;

namespace FuaPay.Web.Modules.Payments.Application;

public interface IPaymentReconciliationQueries
{
    Task<IReadOnlyList<PaymentReconciliationAdminItem>> ListOpenAsync(
        int limit,
        CancellationToken cancellationToken = default);
}

public sealed record PaymentReconciliationAdminItem(
    Guid PaymentId,
    PaymentProvider Provider,
    string? ProviderReference,
    Guid? CorrelationId,
    PaymentReconciliationState State,
    int AttemptCount,
    DateTimeOffset NextAttemptAt,
    DateTimeOffset? LeaseExpiresAt,
    DateTimeOffset? LastAttemptAt,
    DateTimeOffset? LastBrowserReturnAt,
    int? LastGatewayPaymentStatus,
    int? LastResultCode,
    string? LastError,
    DateTimeOffset UpdatedAt);
