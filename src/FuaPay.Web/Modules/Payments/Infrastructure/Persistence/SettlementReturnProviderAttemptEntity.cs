namespace FuaPay.Web.Modules.Payments.Infrastructure.Persistence;

internal sealed class SettlementReturnProviderAttemptEntity
{
    public Guid Id { get; set; }

    public Guid SettlementReturnId { get; set; }

    public int Provider { get; set; }

    public int Operation { get; set; }

    public string ProviderReference { get; set; } = string.Empty;

    public int State { get; set; }

    public string? Diagnostic { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public DateTimeOffset? StartedAt { get; set; }

    public DateTimeOffset? FinishedAt { get; set; }

    public long Version { get; set; }
}
