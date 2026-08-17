using FuaPay.Web.BuildingBlocks.Notifications;
using FuaPay.Web.BuildingBlocks.Persistence;

namespace FuaPay.Web.Modules.Notifications.Infrastructure.Persistence;

internal sealed class EfNotificationOutbox : INotificationOutbox
{
    private readonly FuaPayDbContext _dbContext;

    public EfNotificationOutbox(FuaPayDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public void Stage(NotificationMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        _dbContext.NotificationOutbox.Add(new NotificationOutboxEntity
        {
            Id = message.Id,
            RecipientUserId = message.RecipientUserId,
            Type = message.Type,
            Subject = message.Subject,
            Body = message.Body,
            CreatedAt = message.CreatedAt,
            AttemptCount = 0
        });
    }
}
