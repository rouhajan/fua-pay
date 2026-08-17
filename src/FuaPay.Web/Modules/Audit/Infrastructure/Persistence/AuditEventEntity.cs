namespace FuaPay.Web.Modules.Audit.Infrastructure.Persistence;

internal sealed class AuditEventEntity
{
    public Guid Id { get; set; }

    public DateTimeOffset OccurredAt { get; set; }

    public Guid? ActorUserId { get; set; }

    public string? ActorProcessName { get; set; }

    public string Action { get; set; } = string.Empty;

    public string EntityType { get; set; } = string.Empty;

    public string EntityId { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;
}
