namespace FuaPay.Web.Modules.ServiceUnits.Infrastructure.Persistence;

internal sealed class ServiceUnitEntity
{
    public Guid Id { get; set; }

    public string Code { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public int DefaultServiceType { get; set; }

    public int Status { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public int CreatedByType { get; set; }

    public Guid? CreatedByUserId { get; set; }

    public string? CreatedByProcessName { get; set; }

    public DateTimeOffset? DeactivatedAt { get; set; }

    public int? DeactivatedByType { get; set; }

    public Guid? DeactivatedByUserId { get; set; }

    public string? DeactivatedByProcessName { get; set; }

    public long Version { get; set; }

    public ICollection<RequesterServiceUnitAssignmentEntity>
        RequesterAssignments
    { get; set; } =
            new List<RequesterServiceUnitAssignmentEntity>();
}
