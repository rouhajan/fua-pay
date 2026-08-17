namespace FuaPay.Web.Modules.Payments.Application;

public interface IPaymentQueries
{
    Task<PaymentDetail?> FindForCustomerAsync(
        Guid customerUserId,
        Guid paymentId,
        CancellationToken cancellationToken = default);

    Task<PaymentDetail?> FindForAdministrationAsync(
        Guid paymentId,
        CancellationToken cancellationToken = default);

    Task<PaymentPage> ListForCustomerAsync(
        Guid customerUserId,
        PaymentListFilter filter,
        PaymentPageRequest page,
        CancellationToken cancellationToken = default);

    Task<PaymentPage> ListForAdministrationAsync(
        PaymentListFilter filter,
        PaymentPageRequest page,
        CancellationToken cancellationToken = default);
}
