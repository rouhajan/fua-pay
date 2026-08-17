using FuaPay.Web.Modules.Payments.Domain;

namespace FuaPay.Web.Modules.Payments.Application;

public sealed class DevelopmentPaymentProviderInitiator :
    IPaymentProviderInitiator
{
    private readonly DevelopmentPaymentAvailability _availability;

    public DevelopmentPaymentProviderInitiator(
        DevelopmentPaymentAvailability availability)
    {
        ArgumentNullException.ThrowIfNull(availability);
        _availability = availability;
    }

    public PaymentProvider Provider => PaymentProvider.Development;

    public void EnsureAvailable() => _availability.EnsureEnabled();

    public Task<PaymentProviderInitializationResult> InitializeAsync(
        PaymentProviderInitializationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        EnsureAvailable();

        if (request.Provider != Provider)
        {
            throw new InvalidOperationException(
                "Vývojový poskytovatel obdržel platbu určenou jinému poskytovateli.");
        }

        return Task.FromResult(
            new PaymentProviderInitializationResult(
                Provider,
                $"DEV-{request.PaymentId:N}".ToUpperInvariant(),
                processUri: null));
    }

    public Task VerifyAsync(
        PaymentProviderInitializationResult candidate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        cancellationToken.ThrowIfCancellationRequested();
        EnsureAvailable();

        if (candidate.Provider != Provider)
        {
            throw new InvalidOperationException(
                "The development provider received a candidate for a different provider.");
        }

        return Task.CompletedTask;
    }
}
