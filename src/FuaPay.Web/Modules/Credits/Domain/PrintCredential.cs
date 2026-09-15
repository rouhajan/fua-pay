namespace FuaPay.Web.Modules.Credits.Domain;

public sealed class PrintCredential
{
    public PrintCredential(
        Guid ownerId,
        string normalizedEmail,
        string codeHash,
        DateTimeOffset createdAt)
    {
        if (ownerId == Guid.Empty)
        {
            throw new ArgumentException("Owner ID must not be empty.", nameof(ownerId));
        }

        OwnerId = ownerId;
        NormalizedEmail = PrintCredentialEmail.Normalize(normalizedEmail);
        CodeHash = ValidateHash(codeHash);
        CreatedAt = createdAt;
        ChangedAt = createdAt;
    }

    public Guid OwnerId { get; }

    public string NormalizedEmail { get; private set; }

    public string CodeHash { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset ChangedAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public bool IsActive => RevokedAt is null;

    public void Change(
        string normalizedEmail,
        string codeHash,
        DateTimeOffset changedAt)
    {
        if (changedAt < ChangedAt)
        {
            throw new ArgumentOutOfRangeException(nameof(changedAt));
        }

        NormalizedEmail = PrintCredentialEmail.Normalize(normalizedEmail);
        CodeHash = ValidateHash(codeHash);
        ChangedAt = changedAt;
        RevokedAt = null;
    }

    public void Revoke(DateTimeOffset revokedAt)
    {
        if (revokedAt < ChangedAt)
        {
            throw new ArgumentOutOfRangeException(nameof(revokedAt));
        }

        ChangedAt = revokedAt;
        RevokedAt = revokedAt;
    }

    internal static PrintCredential Restore(
        Guid ownerId,
        string normalizedEmail,
        string codeHash,
        DateTimeOffset createdAt,
        DateTimeOffset changedAt,
        DateTimeOffset? revokedAt)
    {
        var credential = new PrintCredential(
            ownerId,
            normalizedEmail,
            codeHash,
            createdAt);

        credential.ChangedAt = changedAt;
        credential.RevokedAt = revokedAt;
        return credential;
    }

    private static string ValidateHash(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 512)
        {
            throw new ArgumentException("Print-code verifier is invalid.", nameof(value));
        }

        return value;
    }
}
