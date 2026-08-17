namespace FuaPay.Web.Modules.Payments.Infrastructure.Persistence;

internal sealed class PaymentEntity
{
    public Guid Id { get; set; }

    public Guid CustomerUserId { get; set; }

    public int PurposeType { get; set; }

    public Guid? JobId { get; set; }

    public long AmountMinorUnits { get; set; }

    public int Provider { get; set; }

    public Guid? CreationRequestId { get; set; }

    public int Status { get; set; }

    public string? ProviderReference { get; set; }

    public string? FailureReason { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public long Version { get; set; }
}
