namespace FuaPay.Web.Modules.Notifications.Infrastructure.Persistence;

internal sealed class NotificationOutboxEntity
{
    public Guid Id { get; set; }
    public Guid RecipientUserId { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? SentAt { get; set; }
    public int AttemptCount { get; set; }
    public string? LastError { get; set; }
}
