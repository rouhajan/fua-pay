namespace FuaPay.Web.Modules.Credits.Infrastructure.PrintPayments;

public sealed class PrintCredentialSecurityConfiguration
{
    private PrintCredentialSecurityConfiguration(byte[] pepper)
    {
        Pepper = pepper;
    }

    internal byte[] Pepper { get; }

    public static PrintCredentialSecurityConfiguration Resolve(
        IConfiguration configuration,
        bool required)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var encoded = configuration["PrintCredentials:PepperBase64"];
        if (string.IsNullOrWhiteSpace(encoded))
        {
            if (required)
            {
                throw new InvalidOperationException(
                    "Enabled PrintPayments requires PrintCredentials:PepperBase64.");
            }

            return new PrintCredentialSecurityConfiguration([]);
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

        return new PrintCredentialSecurityConfiguration(pepper);
    }
}
