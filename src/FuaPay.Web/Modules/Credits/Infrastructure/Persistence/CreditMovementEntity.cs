namespace FuaPay.Web.Modules.Credits.Infrastructure.Persistence;

internal sealed class CreditMovementEntity
{
    public long Id { get; set; }

    public Guid AccountId { get; set; }

    public long Sequence { get; set; }

    public Guid OperationId { get; set; }

    public int MovementType { get; set; }

    public long AmountMinorUnits { get; set; }

    public long BalanceAfterMinorUnits { get; set; }

    public DateTimeOffset RecordedAt { get; set; }

    public string Description { get; set; } = string.Empty;

    public CreditAccountEntity Account { get; set; } = null!;
}
