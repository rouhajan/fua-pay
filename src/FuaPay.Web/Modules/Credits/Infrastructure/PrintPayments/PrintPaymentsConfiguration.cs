namespace FuaPay.Web.Modules.Credits.Infrastructure.PrintPayments;

public sealed record PrintPaymentsConfiguration(
    bool Enabled,
    IReadOnlyList<FuaPrintSourceConfiguration> Sources)
{
    public static PrintPaymentsConfiguration Resolve(
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        if (!configuration.GetValue<bool>("PrintPayments:Enabled"))
        {
            return new PrintPaymentsConfiguration(false, []);
        }

        var sources = configuration
            .GetSection("PrintPayments:Sources")
            .GetChildren()
            .Select(ResolveSource)
            .ToArray();

        if (sources.Length == 0)
        {
            throw new InvalidOperationException(
                "Enabled PrintPayments requires at least one configured source.");
        }

        var duplicateSource = sources
            .GroupBy(source => source.PrintSourceId)
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicateSource is not null)
        {
            throw new InvalidOperationException(
                $"PrintPayments source ID '{duplicateSource.Key}' is duplicated.");
        }

        var duplicateDigest = sources
            .GroupBy(
                source => source.CredentialSha256,
                StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicateDigest is not null)
        {
            throw new InvalidOperationException(
                "A PrintPayments credential digest is configured more than once.");
        }

        return new PrintPaymentsConfiguration(true, sources);
    }

    private static FuaPrintSourceConfiguration ResolveSource(
        IConfigurationSection section)
    {
        if (
            !Guid.TryParse(
                section["PrintSourceId"],
                out var printSourceId) ||
            printSourceId == Guid.Empty)
        {
            throw new InvalidOperationException(
                $"{section.Path}:PrintSourceId must be a non-empty GUID.");
        }

        var digest = section["CredentialSha256"];

        if (
            digest is null ||
            digest.Length != FuaPrintSourceConfiguration.DigestHexLength ||
            !digest.All(Uri.IsHexDigit))
        {
            throw new InvalidOperationException(
                $"{section.Path}:CredentialSha256 must be exactly 64 hexadecimal characters.");
        }

        return new FuaPrintSourceConfiguration(
            printSourceId,
            digest.ToLowerInvariant());
    }
}

public sealed class FuaPrintSourceConfiguration
{
    internal const int DigestHexLength = 64;

    private readonly byte[] _credentialDigest;

    internal FuaPrintSourceConfiguration(
        Guid printSourceId,
        string credentialSha256)
    {
        PrintSourceId = printSourceId;
        CredentialSha256 = credentialSha256;
        _credentialDigest = Convert.FromHexString(credentialSha256);
    }

    public Guid PrintSourceId { get; }

    public string CredentialSha256 { get; }

    internal ReadOnlySpan<byte> CredentialDigest => _credentialDigest;
}
