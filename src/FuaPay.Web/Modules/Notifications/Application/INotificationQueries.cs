namespace FuaPay.Web.Modules.Notifications.Application;

public interface INotificationQueries
{
    Task<IReadOnlyList<NotificationOutboxItem>> ListRecentAsync(
        int limit = 100,
        CancellationToken cancellationToken = default);
}
