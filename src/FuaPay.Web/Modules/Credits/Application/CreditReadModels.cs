using FuaPay.Web.Modules.Credits.Domain;

namespace FuaPay.Web.Modules.Credits.Application;

public sealed record CreditMovementPageRequest
{
    public const int DefaultLimit = 20;
    public const int MaximumLimit = 100;

    public CreditMovementPageRequest(
        int offset = 0,
        int limit = DefaultLimit)
    {
        if (offset < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(offset),
                "Posun stránkování nesmí být záporný.");
        }

        if (limit <= 0 || limit > MaximumLimit)
        {
            throw new ArgumentOutOfRangeException(
                nameof(limit),
                $"Počet položek musí být mezi 1 a {MaximumLimit}.");
        }

        Offset = offset;
        Limit = limit;
    }

    public int Offset { get; }

    public int Limit { get; }
}

public sealed record CreditAccountSummary(
    Guid Id,
    Guid OwnerId,
    long BalanceMinorUnits,
    long Version);

public sealed record CreditMovementListItem(
    Guid OperationId,
    CreditMovementType Type,
    long AmountMinorUnits,
    long BalanceAfterMinorUnits,
    string Description,
    DateTimeOffset RecordedAt,
    long Sequence);

public sealed record CreditMovementPage
{
    public CreditMovementPage(
        IReadOnlyList<CreditMovementListItem> items,
        int offset,
        int limit,
        long totalCount)
    {
        ArgumentNullException.ThrowIfNull(items);

        if (offset < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(offset));
        }

        if (limit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(limit));
        }

        if (totalCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(totalCount));
        }

        Items = items;
        Offset = offset;
        Limit = limit;
        TotalCount = totalCount;
    }

    public IReadOnlyList<CreditMovementListItem> Items { get; }

    public int Offset { get; }

    public int Limit { get; }

    public long TotalCount { get; }

    public bool HasMore =>
        (long)Offset + Items.Count < TotalCount;
}


public sealed record CreditAdministrationMovementFilter
{
    public CreditAdministrationMovementFilter(
        Guid? ownerId = null,
        DateTimeOffset? recordedFrom = null,
        DateTimeOffset? recordedToExclusive = null)
    {
        if (ownerId == Guid.Empty)
        {
            throw new ArgumentException(
                "ID vlastníka kreditu nesmí být prázdné.",
                nameof(ownerId));
        }

        if (
            recordedFrom.HasValue &&
            recordedToExclusive.HasValue &&
            recordedToExclusive.Value <= recordedFrom.Value)
        {
            throw new ArgumentException(
                "Konec období musí následovat po jeho začátku.",
                nameof(recordedToExclusive));
        }

        OwnerId = ownerId;
        RecordedFrom = recordedFrom;
        RecordedToExclusive = recordedToExclusive;
    }

    public Guid? OwnerId { get; }

    public DateTimeOffset? RecordedFrom { get; }

    public DateTimeOffset? RecordedToExclusive { get; }
}

public sealed record CreditAdministrationMovementListItem(
    Guid OwnerId,
    Guid OperationId,
    CreditMovementType Type,
    long AmountMinorUnits,
    long BalanceAfterMinorUnits,
    string Description,
    DateTimeOffset RecordedAt,
    long Sequence);

public sealed record CreditAdministrationMovementPage(
    IReadOnlyList<CreditAdministrationMovementListItem> Items,
    int Offset,
    int Limit,
    long TotalCount)
{
    public bool HasMore => (long)Offset + Items.Count < TotalCount;
}
