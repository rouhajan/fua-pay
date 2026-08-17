namespace FuaPay.Web.BuildingBlocks.Auditing;

public sealed record AuditEntry
{
    public AuditEntry(
        Guid id,
        DateTimeOffset occurredAt,
        Guid? actorUserId,
        string? actorProcessName,
        string action,
        string entityType,
        string entityId,
        string description)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException(
                "ID auditní události nesmí být prázdné.",
                nameof(id));
        }

        if (occurredAt == default)
        {
            throw new ArgumentException(
                "Čas auditní události nesmí být prázdný.",
                nameof(occurredAt));
        }

        if (actorUserId == Guid.Empty)
        {
            throw new ArgumentException(
                "ID uživatele auditu nesmí být prázdné.",
                nameof(actorUserId));
        }

        if (actorUserId.HasValue == !string.IsNullOrWhiteSpace(actorProcessName))
        {
            throw new ArgumentException(
                "Auditní událost musí mít právě jednoho původce: uživatele nebo proces.");
        }

        Id = id;
        OccurredAt = occurredAt;
        ActorUserId = actorUserId;
        ActorProcessName = Normalize(actorProcessName, 120, nameof(actorProcessName));
        Action = NormalizeRequired(action, 100, nameof(action));
        EntityType = NormalizeRequired(entityType, 80, nameof(entityType));
        EntityId = NormalizeRequired(entityId, 160, nameof(entityId));
        Description = NormalizeRequired(description, 500, nameof(description));
    }

    public Guid Id { get; }

    public DateTimeOffset OccurredAt { get; }

    public Guid? ActorUserId { get; }

    public string? ActorProcessName { get; }

    public string Action { get; }

    public string EntityType { get; }

    public string EntityId { get; }

    public string Description { get; }

    public static AuditEntry ForUser(
        Guid actorUserId,
        string action,
        string entityType,
        string entityId,
        string description,
        DateTimeOffset occurredAt)
    {
        return new AuditEntry(
            Guid.NewGuid(),
            occurredAt,
            actorUserId,
            actorProcessName: null,
            action,
            entityType,
            entityId,
            description);
    }

    public static AuditEntry ForProcess(
        string actorProcessName,
        string action,
        string entityType,
        string entityId,
        string description,
        DateTimeOffset occurredAt)
    {
        return new AuditEntry(
            Guid.NewGuid(),
            occurredAt,
            actorUserId: null,
            actorProcessName,
            action,
            entityType,
            entityId,
            description);
    }

    private static string NormalizeRequired(
        string value,
        int maximumLength,
        string parameterName)
    {
        return Normalize(value, maximumLength, parameterName)
            ?? throw new ArgumentException(
                "Hodnota nesmí být prázdná.",
                parameterName);
    }

    private static string? Normalize(
        string? value,
        int maximumLength,
        string parameterName)
    {
        if (value is null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(
                "Hodnota nesmí být prázdná.",
                parameterName);
        }

        var normalized = value.Trim();

        if (normalized.Length > maximumLength)
        {
            throw new ArgumentException(
                $"Hodnota překračuje limit {maximumLength} znaků.",
                parameterName);
        }

        return normalized;
    }
}
