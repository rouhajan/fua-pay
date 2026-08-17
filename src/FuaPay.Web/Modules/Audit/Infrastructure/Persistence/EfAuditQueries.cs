using FuaPay.Web.BuildingBlocks.Persistence;
using FuaPay.Web.Modules.Audit.Application;

using Microsoft.EntityFrameworkCore;

namespace FuaPay.Web.Modules.Audit.Infrastructure.Persistence;

internal sealed class EfAuditQueries : IAuditQueries
{
    private readonly FuaPayDbContext _dbContext;

    public EfAuditQueries(FuaPayDbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        _dbContext = dbContext;
    }

    public async Task<AuditPage> ListAsync(
        AuditListFilter filter,
        AuditPageRequest page,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(page);

        var query = _dbContext.AuditEvents.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var pattern = $"%{filter.Search.Trim()}%";
            query = query.Where(
                item =>
                    EF.Functions.ILike(item.Action, pattern) ||
                    EF.Functions.ILike(item.EntityType, pattern) ||
                    EF.Functions.ILike(item.EntityId, pattern) ||
                    EF.Functions.ILike(item.Description, pattern));
        }

        if (filter.ActorUserId.HasValue)
        {
            query = query.Where(
                item => item.ActorUserId == filter.ActorUserId.Value);
        }

        if (filter.OccurredFrom.HasValue)
        {
            query = query.Where(
                item => item.OccurredAt >= filter.OccurredFrom.Value);
        }

        if (filter.OccurredToExclusive.HasValue)
        {
            query = query.Where(
                item => item.OccurredAt < filter.OccurredToExclusive.Value);
        }

        var totalCount = await query.LongCountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(item => item.OccurredAt)
            .ThenByDescending(item => item.Id)
            .Skip(page.Offset)
            .Take(page.Limit)
            .Select(item => new AuditListItem(
                item.Id,
                item.OccurredAt,
                item.ActorUserId,
                item.ActorProcessName,
                item.Action,
                item.EntityType,
                item.EntityId,
                item.Description))
            .ToArrayAsync(cancellationToken);

        return new AuditPage(
            items,
            page.Offset,
            page.Limit,
            totalCount);
    }
}
