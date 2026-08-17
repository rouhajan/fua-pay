namespace FuaPay.Web.Modules.Jobs.Infrastructure.Persistence;

internal sealed class JobEntity
{
    public Guid Id { get; set; }

    public string Number { get; set; } = string.Empty;

    public Guid ServiceUnitId { get; set; }

    public Guid CustomerUserId { get; set; }

    public Guid CreatedByUserId { get; set; }

    public int ServiceType { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public long PriceMinorUnits { get; set; }

    public int ProductionStatus { get; set; }

    public int PaymentStatus { get; set; }

    public int? SettlementType { get; set; }

    public Guid? SettlementReferenceId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? PublishedAt { get; set; }

    public DateTimeOffset? SettledAt { get; set; }

    public DateTimeOffset? ProductionStartedAt { get; set; }

    public DateTimeOffset? ReadyForPickupAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public DateTimeOffset? CancelledAt { get; set; }

    public long Version { get; set; }
}
