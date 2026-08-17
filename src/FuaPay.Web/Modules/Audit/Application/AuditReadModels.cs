namespace FuaPay.Web.Modules.Audit.Application;

public sealed record AuditPageRequest
{
    public const int DefaultLimit = 50;
    public const int MaximumLimit = 100;

    public AuditPageRequest(
        int offset = 0,
        int limit = DefaultLimit)
    {
        if (offset < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(offset));
        }

        if (limit <= 0 || limit > MaximumLimit)
        {
            throw new ArgumentOutOfRangeException(nameof(limit));
        }

        Offset = offset;
        Limit = limit;
    }

    public int Offset { get; }

    public int Limit { get; }
}

public sealed record AuditListFilter(
    string? Search = null,
    Guid? ActorUserId = null,
    DateTimeOffset? OccurredFrom = null,
    DateTimeOffset? OccurredToExclusive = null);

public sealed record AuditListItem(
    Guid Id,
    DateTimeOffset OccurredAt,
    Guid? ActorUserId,
    string? ActorProcessName,
    string Action,
    string EntityType,
    string EntityId,
    string Description);

public sealed record AuditPage(
    IReadOnlyList<AuditListItem> Items,
    int Offset,
    int Limit,
    long TotalCount)
{
    public bool HasMore => (long)Offset + Items.Count < TotalCount;
}
