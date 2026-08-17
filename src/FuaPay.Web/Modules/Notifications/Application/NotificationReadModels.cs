namespace FuaPay.Web.Modules.Notifications.Application;

public sealed record NotificationOutboxItem(
    Guid Id,
    Guid RecipientUserId,
    string Type,
    string Subject,
    string Body,
    DateTimeOffset CreatedAt,
    DateTimeOffset? SentAt,
    int AttemptCount,
    string? LastError);
