using FuaPay.Web.BuildingBlocks.Persistence;
using FuaPay.Web.Modules.Notifications.Application;

using Microsoft.EntityFrameworkCore;

namespace FuaPay.Web.Modules.Notifications.Infrastructure.Persistence;

internal sealed class EfNotificationQueries : INotificationQueries
{
    private readonly FuaPayDbContext _dbContext;

    public EfNotificationQueries(FuaPayDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<NotificationOutboxItem>> ListRecentAsync(
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        if (limit <= 0 || limit > 500)
        {
            throw new ArgumentOutOfRangeException(nameof(limit));
        }

        return await _dbContext.NotificationOutbox
            .AsNoTracking()
            .OrderByDescending(item => item.CreatedAt)
            .ThenByDescending(item => item.Id)
            .Take(limit)
            .Select(item => new NotificationOutboxItem(
                item.Id,
                item.RecipientUserId,
                item.Type,
                item.Subject,
                item.Body,
                item.CreatedAt,
                item.SentAt,
                item.AttemptCount,
                item.LastError))
            .ToArrayAsync(cancellationToken);
    }
}
