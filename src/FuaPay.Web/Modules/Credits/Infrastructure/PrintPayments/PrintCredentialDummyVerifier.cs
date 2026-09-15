using FuaPay.Web.Modules.Credits.Application;

namespace FuaPay.Web.Modules.Credits.Infrastructure.PrintPayments;

internal sealed class PrintCredentialDummyVerifier :
    IPreparedPrintCredentialHash
{
    public PrintCredentialDummyVerifier(
        PrintCredentialSecurityConfiguration configuration,
        IPrintCodeHasher hasher)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(hasher);

        Value = configuration.Enabled
            ? hasher.Hash("000000")
            : string.Empty;
    }

    public string Value { get; }
}
