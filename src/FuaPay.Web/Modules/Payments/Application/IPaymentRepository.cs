using FuaPay.Web.Modules.Payments.Domain;

namespace FuaPay.Web.Modules.Payments.Application;

public interface IPaymentRepository
{
    Task<Payment?> FindByIdAsync(
        Guid paymentId,
        CancellationToken cancellationToken = default);

    Task<Payment?> FindBlockingForJobAsync(
        Guid jobId,
        CancellationToken cancellationToken = default);

    Task<Payment?> FindByProviderReferenceAsync(
        PaymentProvider provider,
        string providerReference,
        CancellationToken cancellationToken = default);

    Task<Payment?> FindByCreationRequestIdAsync(
        Guid creationRequestId,
        CancellationToken cancellationToken = default);

    Task AddAsync(
        Payment payment,
        CancellationToken cancellationToken = default);

    Task AddPreparedAsync(
        Payment payment,
        PaymentInitiation initiation,
        CancellationToken cancellationToken = default);

    Task SaveAsync(
        Payment payment,
        CancellationToken cancellationToken = default);
}
