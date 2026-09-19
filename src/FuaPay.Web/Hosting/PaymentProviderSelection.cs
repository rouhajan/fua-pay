using FuaPay.Web.Modules.Payments.Domain;

using Microsoft.Extensions.Hosting;

namespace FuaPay.Web.Hosting;

public sealed record PaymentProviderSelection(
    PaymentProvider? Provider,
    bool DevelopmentPaymentUiEnabled)
{
    public static PaymentProviderSelection Resolve(
        string environmentName,
        IConfiguration configuration,
        RuntimeFeatureSelection runtimeFeatures,
        bool csobGatewayEnabled)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(environmentName);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(runtimeFeatures);

        var configured = configuration["Payments:Provider"]?.Trim();

        if (string.Equals(
                configured,
                "None",
                StringComparison.OrdinalIgnoreCase))
        {
            if (csobGatewayEnabled)
            {
                throw new InvalidOperationException(
                    "Payments:Provider=None cannot be combined with an enabled CSOB gateway.");
            }

            return new PaymentProviderSelection(
                Provider: null,
                DevelopmentPaymentUiEnabled: false);
        }

        if (string.Equals(
                configured,
                "Development",
                StringComparison.OrdinalIgnoreCase))
        {
            if (
                Environments.Production.Equals(
                    environmentName,
                    StringComparison.OrdinalIgnoreCase) ||
                !runtimeFeatures.SimulatedPaymentsEnabled ||
                csobGatewayEnabled)
            {
                throw new InvalidOperationException(
                    "Development payment provider není v tomto prostředí výslovně povolen nebo koliduje s ČSOB konfigurací.");
            }

            return new PaymentProviderSelection(
                PaymentProvider.Development,
                DevelopmentPaymentUiEnabled: true);
        }

        if (string.Equals(
                configured,
                "Csob",
                StringComparison.OrdinalIgnoreCase))
        {
            if (!csobGatewayEnabled)
            {
                throw new InvalidOperationException(
                    "Payments:Provider=Csob vyžaduje platnou a aktivní ČSOB konfiguraci.");
            }

            return new PaymentProviderSelection(
                PaymentProvider.Csob,
                DevelopmentPaymentUiEnabled: false);
        }

        throw new InvalidOperationException(
            "Payments:Provider musí explicitně vybrat podporovanou hodnotu: None, Development nebo Csob.");
    }
}
