namespace FuaPay.Web.Modules.Access.Infrastructure.Persistence;

internal sealed class RoleAssignmentEntity
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public int Role { get; set; }

    public DateTimeOffset GrantedAt { get; set; }

    public int GrantedByType { get; set; }

    public Guid? GrantedByUserId { get; set; }

    public string? GrantedByProcessName { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }

    public int? RevokedByType { get; set; }

    public Guid? RevokedByUserId { get; set; }

    public string? RevokedByProcessName { get; set; }

    public AccessUserEntity User { get; set; } = null!;
}
