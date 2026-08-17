using FuaPay.Web.Modules.Payments.Domain;

namespace FuaPay.Web.Modules.Payments.Application;

public interface IPaymentInitiationRepository
{
    Task<PaymentInitiation?> FindByPaymentIdAsync(
        Guid paymentId,
        CancellationToken cancellationToken = default);

    Task SaveAsync(
        PaymentInitiation initiation,
        CancellationToken cancellationToken = default);
}
