namespace FuaPay.Web.Modules.Access.Infrastructure.Persistence;

internal sealed class ExternalIdentityEntity
{
    public string Provider { get; set; } = string.Empty;

    public string Tenant { get; set; } = string.Empty;

    public string Subject { get; set; } = string.Empty;

    public Guid UserId { get; set; }

    public AccessUserEntity User { get; set; } = null!;
}
