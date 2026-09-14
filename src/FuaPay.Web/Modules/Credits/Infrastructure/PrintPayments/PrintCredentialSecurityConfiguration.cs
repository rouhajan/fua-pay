namespace FuaPay.Web.Modules.Credits.Infrastructure.PrintPayments;

public sealed class PrintCredentialSecurityConfiguration
{
    private PrintCredentialSecurityConfiguration(
        bool enabled,
        byte[] pepper)
    {
        Enabled = enabled;
        Pepper = pepper;
    }

    public bool Enabled { get; }

    internal byte[] Pepper { get; }

    public static PrintCredentialSecurityConfiguration Resolve(
        IConfiguration configuration,
        bool printPaymentsEnabled)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var enabled = configuration.GetValue<bool>(
            "PrintCredentials:Enabled");
        if (!enabled)
        {
            return new PrintCredentialSecurityConfiguration(false, []);
        }

        if (!printPaymentsEnabled)
        {
            throw new InvalidOperationException(
                "Enabled PrintCredentials requires enabled PrintPayments.");
        }

        var encoded = configuration["PrintCredentials:PepperBase64"];
        if (string.IsNullOrWhiteSpace(encoded))
        {
            throw new InvalidOperationException(
                "Enabled PrintCredentials requires PrintCredentials:PepperBase64.");
        }

        byte[] pepper;
        try
        {
            pepper = Convert.FromBase64String(encoded);
        }
        catch (FormatException exception)
        {
            throw new InvalidOperationException(
                "PrintCredentials:PepperBase64 must be valid Base64.",
                exception);
        }

        if (pepper.Length < 32)
        {
            throw new InvalidOperationException(
                "PrintCredentials:PepperBase64 must decode to at least 32 bytes.");
        }

        return new PrintCredentialSecurityConfiguration(true, pepper);
    }
}
