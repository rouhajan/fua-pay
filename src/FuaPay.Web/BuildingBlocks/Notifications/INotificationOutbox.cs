namespace FuaPay.Web.BuildingBlocks.Notifications;

public interface INotificationOutbox
{
    void Stage(NotificationMessage message);
}

public sealed class NullNotificationOutbox : INotificationOutbox
{
    public static NullNotificationOutbox Instance { get; } = new();

    private NullNotificationOutbox()
    {
    }

    public void Stage(NotificationMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
    }
}
