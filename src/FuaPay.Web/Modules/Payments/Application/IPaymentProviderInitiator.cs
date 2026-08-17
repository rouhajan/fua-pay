using FuaPay.Web.Modules.Payments.Domain;

namespace FuaPay.Web.Modules.Payments.Application;

public interface IPaymentProviderInitiator
{
    PaymentProvider Provider { get; }

    void EnsureAvailable();

    Task<PaymentProviderInitializationResult> InitializeAsync(
        PaymentProviderInitializationRequest request,
        CancellationToken cancellationToken = default);

    Task VerifyAsync(
        PaymentProviderInitializationResult candidate,
        CancellationToken cancellationToken = default);
}
