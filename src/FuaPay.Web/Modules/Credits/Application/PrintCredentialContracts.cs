using FuaPay.Web.Modules.Credits.Domain;

namespace FuaPay.Web.Modules.Credits.Application;

public interface IPrintCredentialRepository
{
    Task<PrintCredential?> FindByOwnerAsync(
        Guid ownerId,
        CancellationToken cancellationToken = default);

    Task<PrintCredentialAuthenticationCandidate?> FindAuthenticationCandidateAsync(
        string normalizedEmail,
        CancellationToken cancellationToken = default);

    Task<long> CountAccessUsersByNormalizedEmailAsync(
        string normalizedEmail,
        CancellationToken cancellationToken = default);

    void Add(PrintCredential credential);

    Task SaveAsync(CancellationToken cancellationToken = default);
}

public sealed record PrintCredentialAuthenticationCandidate(
    Guid OwnerId,
    string CodeHash,
    bool IsEligible);

public sealed record PrintCredentialView(
    string Email,
    bool IsConfigured,
    DateTimeOffset? ChangedAt);

public sealed class PrintCredentialUnavailableException : Exception
{
    public PrintCredentialUnavailableException()
        : base("The current customer has no unambiguous trusted printing e-mail.")
    {
    }
}

public sealed class PrintCredentialAuthenticationFailedException : Exception
{
    public PrintCredentialAuthenticationFailedException()
        : base("Printing credential authentication failed.")
    {
    }
}

public sealed class PrintCredentialRateLimitExceededException : Exception
{
    public PrintCredentialRateLimitExceededException()
        : base("Printing credential rate limit was exceeded.")
    {
    }
}
