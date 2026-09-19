namespace FuaPay.Web.Modules.Credits.Infrastructure.Persistence;

internal sealed class LegacySafeQCreditTransferEntity
{
    public Guid CommandId { get; set; }

    public Guid AdministratorUserId { get; set; }

    public Guid OwnerId { get; set; }

    public string SafeQUserId { get; set; } = string.Empty;

    public string SnapshotSha256 { get; set; } = string.Empty;

    public long AmountMinorUnits { get; set; }

    public DateTimeOffset AcceptedAt { get; set; }
}
