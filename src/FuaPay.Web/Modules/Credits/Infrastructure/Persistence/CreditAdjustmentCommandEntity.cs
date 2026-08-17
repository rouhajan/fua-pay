namespace FuaPay.Web.Modules.Credits.Infrastructure.Persistence;

internal sealed class CreditAdjustmentCommandEntity
{
    public Guid CommandId { get; set; }

    public Guid AdministratorUserId { get; set; }

    public Guid OwnerId { get; set; }

    public long SignedAmountMinorUnits { get; set; }

    public string Reason { get; set; } = string.Empty;

    public DateTimeOffset AcceptedAt { get; set; }
}
