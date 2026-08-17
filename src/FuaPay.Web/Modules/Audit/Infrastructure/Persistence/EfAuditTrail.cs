using FuaPay.Web.BuildingBlocks.Auditing;
using FuaPay.Web.BuildingBlocks.Persistence;

namespace FuaPay.Web.Modules.Audit.Infrastructure.Persistence;

internal sealed class EfAuditTrail : IAuditTrail
{
    private readonly FuaPayDbContext _dbContext;

    public EfAuditTrail(FuaPayDbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        _dbContext = dbContext;
    }

    public void Stage(AuditEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        _dbContext.AuditEvents.Add(ToEntity(entry));
    }

    public async Task WriteAsync(
        AuditEntry entry,
        CancellationToken cancellationToken = default)
    {
        Stage(entry);
        await _dbContext.SaveChangesAsync(cancellationToken);
        _dbContext.ChangeTracker.Clear();
    }

    private static AuditEventEntity ToEntity(AuditEntry entry)
    {
        return new AuditEventEntity
        {
            Id = entry.Id,
            OccurredAt = entry.OccurredAt,
            ActorUserId = entry.ActorUserId,
            ActorProcessName = entry.ActorProcessName,
            Action = entry.Action,
            EntityType = entry.EntityType,
            EntityId = entry.EntityId,
            Description = entry.Description
        };
    }
}
