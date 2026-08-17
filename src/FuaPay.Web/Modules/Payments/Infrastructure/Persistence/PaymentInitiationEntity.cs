namespace FuaPay.Web.Modules.Payments.Infrastructure.Persistence;

internal sealed class PaymentInitiationEntity
{
    public Guid PaymentId { get; set; }

    public int Provider { get; set; }

    public long OrderNumber { get; set; }

    public Guid CorrelationId { get; set; }

    public int State { get; set; }

    public string? LastError { get; set; }

    public string? ProcessUri { get; set; }

    public string? ObservedProviderReference { get; set; }

    public string? ObservedProcessUri { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public DateTimeOffset? StartedAt { get; set; }

    public DateTimeOffset? FinishedAt { get; set; }

    public long Version { get; set; }
}
