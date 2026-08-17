using FuaPay.Web.BuildingBlocks.Notifications;
using FuaPay.Web.Modules.Notifications.Application;
using FuaPay.Web.Modules.Notifications.Infrastructure.Persistence;

namespace FuaPay.Web.Modules.Notifications;

public static class NotificationsModule
{
    public static IServiceCollection AddNotificationsModule(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddScoped<INotificationOutbox, EfNotificationOutbox>();
        services.AddScoped<INotificationQueries, EfNotificationQueries>();
        return services;
    }
}
