namespace FuaPay.Web.Modules.Credits.Infrastructure.Persistence;

internal sealed class CreditAccountEntity
{
    public Guid Id { get; set; }

    public Guid OwnerId { get; set; }

    public long BalanceMinorUnits { get; set; }

    public long Version { get; set; }

    public List<CreditMovementEntity> Movements { get; } = [];
}
