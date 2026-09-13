namespace FuaPay.Web.Modules.Credits.Infrastructure.Persistence;

internal sealed class ManualCreditTopUpCommandEntity
{
    public Guid CommandId { get; set; }

    public Guid AdministratorUserId { get; set; }

    public Guid OwnerId { get; set; }

    public long AmountMinorUnits { get; set; }

    public string Note { get; set; } = string.Empty;

    public DateTimeOffset AcceptedAt { get; set; }
}
