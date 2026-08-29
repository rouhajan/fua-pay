namespace FuaPay.Web.Modules.Credits.Infrastructure.Persistence;

internal sealed class CreditReturnHoldEntity
{
    public Guid SettlementReturnId { get; set; }

    public Guid CreditAccountId { get; set; }

    public long AmountMinorUnits { get; set; }

    public int State { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset StateChangedAt { get; set; }

    public long Version { get; set; }
}
