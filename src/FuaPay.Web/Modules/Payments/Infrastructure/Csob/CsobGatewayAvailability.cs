namespace FuaPay.Web.Modules.Payments.Infrastructure.Csob;

public sealed record CsobGatewayAvailability(bool IsEnabled)
{
    public void EnsureEnabled()
    {
        if (!IsEnabled)
        {
            throw new InvalidOperationException(
                "Platební brána ČSOB není v tomto prostředí nakonfigurována.");
        }
    }
}
