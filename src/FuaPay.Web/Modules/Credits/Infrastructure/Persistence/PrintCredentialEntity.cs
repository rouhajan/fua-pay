namespace FuaPay.Web.Modules.Credits.Infrastructure.Persistence;

internal sealed class PrintCredentialEntity
{
    public Guid OwnerId { get; set; }

    public string NormalizedEmail { get; set; } = string.Empty;

    public string CodeHash { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ChangedAt { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }

    public long Version { get; set; }
}
