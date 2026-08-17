namespace FuaPay.Web.Modules.ServiceUnits.Infrastructure.Persistence;

internal sealed class RequesterServiceUnitAssignmentEntity
{
    public Guid Id { get; set; }

    public Guid ServiceUnitId { get; set; }

    public Guid UserId { get; set; }

    public DateTimeOffset GrantedAt { get; set; }

    public int GrantedByType { get; set; }

    public Guid? GrantedByUserId { get; set; }

    public string? GrantedByProcessName { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }

    public int? RevokedByType { get; set; }

    public Guid? RevokedByUserId { get; set; }

    public string? RevokedByProcessName { get; set; }

    public long Version { get; set; }

    public ServiceUnitEntity ServiceUnit { get; set; } = null!;
}
