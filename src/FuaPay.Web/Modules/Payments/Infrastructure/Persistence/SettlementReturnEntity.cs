namespace FuaPay.Web.Modules.Payments.Infrastructure.Persistence;

internal sealed class SettlementReturnEntity
{
    public Guid Id { get; set; }

    public Guid RequestId { get; set; }

    public int Kind { get; set; }

    public Guid? OriginalPaymentId { get; set; }

    public Guid? JobId { get; set; }

    public Guid CustomerUserId { get; set; }

    public Guid AdministratorUserId { get; set; }

    public long AmountMinorUnits { get; set; }

    public string Currency { get; set; } = string.Empty;

    public string Reason { get; set; } = string.Empty;

    public int State { get; set; }

    public DateTimeOffset RequestedAt { get; set; }

    public DateTimeOffset? StartedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public long Version { get; set; }
}
