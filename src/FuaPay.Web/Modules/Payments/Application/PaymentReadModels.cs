using FuaPay.Web.Modules.Payments.Domain;

namespace FuaPay.Web.Modules.Payments.Application;

public sealed record PaymentPageRequest
{
    public const int DefaultLimit = 30;
    public const int MaximumLimit = 100;

    public PaymentPageRequest(
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

public sealed record PaymentListFilter
{
    public PaymentListFilter(
        PaymentStatus? status = null,
        PaymentPurposeType? purposeType = null,
        string? search = null,
        Guid? customerUserId = null,
        DateTimeOffset? createdFrom = null,
        DateTimeOffset? createdToExclusive = null)
    {
        ValidateOptionalEnum(
            status,
            PaymentStatus.Unknown,
            nameof(status));
        ValidateOptionalEnum(
            purposeType,
            PaymentPurposeType.Unknown,
            nameof(purposeType));

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

        Status = status;
        PurposeType = purposeType;
        Search = string.IsNullOrWhiteSpace(search)
            ? null
            : search.Trim();
        CustomerUserId = customerUserId;
        CreatedFrom = createdFrom;
        CreatedToExclusive = createdToExclusive;
    }

    public PaymentStatus? Status { get; }

    public PaymentPurposeType? PurposeType { get; }

    public string? Search { get; }

    public Guid? CustomerUserId { get; }

    public DateTimeOffset? CreatedFrom { get; }

    public DateTimeOffset? CreatedToExclusive { get; }

    private static void ValidateOptionalEnum<TEnum>(
        TEnum? value,
        TEnum unknown,
        string parameterName)
        where TEnum : struct, Enum
    {
        if (
            value.HasValue &&
            (
                EqualityComparer<TEnum>.Default.Equals(
                    value.Value,
                    unknown) ||
                !Enum.IsDefined(value.Value)
            )
        )
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }
}

public sealed record PaymentListItem(
    Guid Id,
    Guid CustomerUserId,
    PaymentPurposeType PurposeType,
    Guid? JobId,
    long AmountMinorUnits,
    PaymentProvider Provider,
    PaymentStatus Status,
    string? ProviderReference,
    string? FailureReason,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? CompletedAt);

public sealed record PaymentDetail(
    Guid Id,
    Guid CustomerUserId,
    PaymentPurposeType PurposeType,
    Guid? JobId,
    long AmountMinorUnits,
    PaymentProvider Provider,
    PaymentStatus Status,
    string? ProviderReference,
    string? FailureReason,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? CompletedAt,
    string? ProcessUri,
    long Version);

public sealed record PaymentPage(
    IReadOnlyList<PaymentListItem> Items,
    int Offset,
    int Limit,
    long TotalCount)
{
    public bool HasMore => (long)Offset + Items.Count < TotalCount;
}
