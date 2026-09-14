using FuaPay.Web.Modules.Credits.Application;

namespace FuaPay.Web.Modules.Credits.Infrastructure.PrintPayments;

public sealed class PrintCredentialDummyVerifier
{
    public PrintCredentialDummyVerifier(
        PrintCredentialSecurityConfiguration configuration,
        IPrintCodeHasher hasher)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(hasher);

        Hash = configuration.Enabled
            ? hasher.Hash("000000")
            : string.Empty;
    }

    public string Hash { get; }
}
