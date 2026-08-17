namespace FuaPay.Web.Modules.Access.Infrastructure.Persistence;

internal sealed class AccessUserEntity
{
    public Guid Id { get; set; }

    public string DisplayName { get; set; } = string.Empty;

    public string? Email { get; set; }

    public int Status { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset LastSeenAt { get; set; }

    public long Version { get; set; }

    public List<ExternalIdentityEntity> ExternalIdentities { get; } = [];

    public List<RoleAssignmentEntity> RoleAssignments { get; } = [];
}
