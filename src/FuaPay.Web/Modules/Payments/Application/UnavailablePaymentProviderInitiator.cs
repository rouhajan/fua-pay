using FuaPay.Web.Modules.Payments.Domain;

namespace FuaPay.Web.Modules.Payments.Application;

public sealed class UnavailablePaymentProviderInitiator :
    IPaymentProviderInitiator
{
    public PaymentProvider Provider =>
        throw new PaymentProviderUnavailableException();

    public void EnsureAvailable() =>
        throw new PaymentProviderUnavailableException();

    public Task<PaymentProviderInitializationResult> InitializeAsync(
        PaymentProviderInitializationRequest request,
        CancellationToken cancellationToken = default) =>
        throw new PaymentProviderUnavailableException();

    public Task VerifyAsync(
        PaymentProviderInitializationResult candidate,
        CancellationToken cancellationToken = default) =>
        throw new PaymentProviderUnavailableException();

    public Uri? ResolveTrustedProcessUri(
        PaymentProvider provider,
        string? providerReference,
        string? processUri) =>
        null;
}
