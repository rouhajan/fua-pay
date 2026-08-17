using FuaPay.Web.Modules.Payments.Domain;

namespace FuaPay.Web.Modules.Payments.Infrastructure.Csob;

public interface ICsobPaymentReconciliationService
{
    Task<CsobPaymentReconciliationResult> ReconcileAsync(
        Guid paymentId,
        string payId,
        CancellationToken cancellationToken = default);
}

public sealed record CsobPaymentReconciliationResult(
    Guid PaymentId,
    PaymentStatus PaymentStatus,
    int GatewayPaymentStatus,
    bool StateChanged);
