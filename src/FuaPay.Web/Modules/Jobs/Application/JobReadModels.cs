using FuaPay.Web.Modules.Jobs.Domain;

namespace FuaPay.Web.Modules.Jobs.Application;

public sealed record JobListFilter
{
    public JobListFilter(
        JobProductionStatus? productionStatus = null,
        JobPaymentStatus? paymentStatus = null,
        Guid? serviceUnitId = null,
        Guid? customerUserId = null,
        string? search = null,
        DateTimeOffset? createdFrom = null,
        DateTimeOffset? createdToExclusive = null)
    {
        ValidateOptionalEnum(
            productionStatus,
            JobProductionStatus.Unknown,
            nameof(productionStatus),
            "Výrobní stav zakázky není podporovaný.");

        ValidateOptionalEnum(
            paymentStatus,
            JobPaymentStatus.Unknown,
            nameof(paymentStatus),
            "Stav úhrady zakázky není podporovaný.");

        if (serviceUnitId == Guid.Empty)
        {
            throw new ArgumentException(
                "ID pracoviště nesmí být prázdné.",
                nameof(serviceUnitId));
        }

        if (customerUserId == Guid.Empty)
        {
            throw new ArgumentException(
                "ID zákazníka nesmí být prázdné.",
                nameof(customerUserId));
        }

        if (
            createdFrom.HasValue &&
            createdToExclusive.HasValue &&
            createdToExclusive.Value <= createdFrom.Value)
        {
            throw new ArgumentException(
                "Konec období musí následovat po jeho začátku.",
                nameof(createdToExclusive));
        }

        ProductionStatus = productionStatus;
        PaymentStatus = paymentStatus;
        ServiceUnitId = serviceUnitId;
        CustomerUserId = customerUserId;
        Search = string.IsNullOrWhiteSpace(search)
            ? null
            : search.Trim();
        CreatedFrom = createdFrom;
        CreatedToExclusive = createdToExclusive;
    }

    public JobProductionStatus? ProductionStatus { get; }

    public JobPaymentStatus? PaymentStatus { get; }

    public Guid? ServiceUnitId { get; }

    public Guid? CustomerUserId { get; }

    public string? Search { get; }

    public DateTimeOffset? CreatedFrom { get; }

    public DateTimeOffset? CreatedToExclusive { get; }

    private static void ValidateOptionalEnum<TEnum>(
        TEnum? value,
        TEnum unknownValue,
        string parameterName,
        string message)
        where TEnum : struct, Enum
    {
        if (
            value.HasValue &&
            (
                EqualityComparer<TEnum>.Default.Equals(
                    value.Value,
                    unknownValue) ||
                !Enum.IsDefined(value.Value)
            )
        )
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                message);
        }
    }
}

public sealed record JobPageRequest
{
    public const int DefaultLimit = 20;
    public const int MaximumLimit = 100;

    public JobPageRequest(
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

public sealed record JobListItem(
    Guid Id,
    string Number,
    Guid ServiceUnitId,
    Guid CustomerUserId,
    Guid CreatedByUserId,
    ServiceType ServiceType,
    string Title,
    long PriceMinorUnits,
    JobProductionStatus ProductionStatus,
    JobPaymentStatus PaymentStatus,
    DateTimeOffset CreatedAt,
    DateTimeOffset? PublishedAt,
    DateTimeOffset? SettledAt);

public sealed record JobDetail(
    Guid Id,
    string Number,
    Guid ServiceUnitId,
    Guid CustomerUserId,
    Guid CreatedByUserId,
    ServiceType ServiceType,
    string Title,
    string Description,
    long PriceMinorUnits,
    JobProductionStatus ProductionStatus,
    JobPaymentStatus PaymentStatus,
    JobSettlementType? SettlementType,
    Guid? SettlementReferenceId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? PublishedAt,
    DateTimeOffset? SettledAt,
    DateTimeOffset? ProductionStartedAt,
    DateTimeOffset? ReadyForPickupAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? CancelledAt,
    long Version);

public sealed record CustomerJobSummary
{
    public CustomerJobSummary(
        long totalCount,
        long awaitingPaymentCount)
    {
        if (totalCount < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(totalCount));
        }

        if (
            awaitingPaymentCount < 0 ||
            awaitingPaymentCount > totalCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(awaitingPaymentCount));
        }

        TotalCount = totalCount;
        AwaitingPaymentCount = awaitingPaymentCount;
    }

    public long TotalCount { get; }

    public long AwaitingPaymentCount { get; }
}

public sealed record ManagementJobSummary
{
    public ManagementJobSummary(
        long totalCount,
        long activeCount,
        long awaitingPaymentCount)
    {
        if (totalCount < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(totalCount));
        }

        if (activeCount < 0 || activeCount > totalCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(activeCount));
        }

        if (
            awaitingPaymentCount < 0 ||
            awaitingPaymentCount > activeCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(awaitingPaymentCount));
        }

        TotalCount = totalCount;
        ActiveCount = activeCount;
        AwaitingPaymentCount = awaitingPaymentCount;
    }

    public long TotalCount { get; }

    public long ActiveCount { get; }

    public long AwaitingPaymentCount { get; }
}

public sealed record JobPage<T>
{
    public JobPage(
        IReadOnlyList<T> items,
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

    public IReadOnlyList<T> Items { get; }

    public int Offset { get; }

    public int Limit { get; }

    public long TotalCount { get; }

    public bool HasMore =>
        (long)Offset + Items.Count < TotalCount;
}
