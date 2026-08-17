namespace FuaPay.Web.Modules.Payments.Application;

public sealed record DevelopmentPaymentAvailability(bool IsEnabled)
{
    public void EnsureEnabled()
    {
        if (!IsEnabled)
        {
            throw new PaymentProviderUnavailableException();
        }
    }
}
