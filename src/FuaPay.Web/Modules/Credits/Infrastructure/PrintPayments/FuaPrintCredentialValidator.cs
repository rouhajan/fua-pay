using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;

namespace FuaPay.Web.Modules.Credits.Infrastructure.PrintPayments;

internal sealed class FuaPrintCredentialValidator
{
    internal const int MinimumTokenLength = 43;
    internal const int MaximumTokenLength = 128;
    internal const int MaximumAuthorizationHeaderLength =
        MaximumTokenLength + 7;

    private readonly PrintPaymentsConfiguration _configuration;

    public FuaPrintCredentialValidator(
        PrintPaymentsConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        _configuration = configuration;
    }

    public bool TryValidate(
        string? authorizationHeader,
        out Guid printSourceId)
    {
        printSourceId = Guid.Empty;

        if (
            !_configuration.Enabled ||
            string.IsNullOrEmpty(authorizationHeader) ||
            authorizationHeader.Length > MaximumAuthorizationHeaderLength ||
            !AuthenticationHeaderValue.TryParse(
                authorizationHeader,
                out var parsed) ||
            !string.Equals(
                parsed.Scheme,
                "Bearer",
                StringComparison.OrdinalIgnoreCase) ||
            !IsValidOpaqueToken(parsed.Parameter))
        {
            return false;
        }

        var candidateDigest = SHA256.HashData(
            Encoding.ASCII.GetBytes(parsed.Parameter!));
        FuaPrintSourceConfiguration? matchedSource = null;

        foreach (var source in _configuration.Sources)
        {
            if (
                CryptographicOperations.FixedTimeEquals(
                    candidateDigest,
                    source.CredentialDigest))
            {
                matchedSource = source;
            }
        }

        if (matchedSource is null)
        {
            return false;
        }

        printSourceId = matchedSource.PrintSourceId;
        return true;
    }

    private static bool IsValidOpaqueToken(string? token)
    {
        return
            token is not null &&
            token.Length is >= MinimumTokenLength and <= MaximumTokenLength &&
            token.All(
                character =>
                    character is >= 'A' and <= 'Z' or
                    >= 'a' and <= 'z' or
                    >= '0' and <= '9' or
                    '-' or '_');
    }
}
