namespace FuaPay.Web.BuildingBlocks.Notifications;

public sealed record NotificationMessage
{
    public NotificationMessage(
        Guid id,
        Guid recipientUserId,
        string type,
        string subject,
        string body,
        DateTimeOffset createdAt)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("ID oznámení nesmí být prázdné.", nameof(id));
        }

        if (recipientUserId == Guid.Empty)
        {
            throw new ArgumentException("ID příjemce nesmí být prázdné.", nameof(recipientUserId));
        }

        if (createdAt == default)
        {
            throw new ArgumentException("Čas vytvoření nesmí být prázdný.", nameof(createdAt));
        }

        Id = id;
        RecipientUserId = recipientUserId;
        Type = Normalize(type, 80, nameof(type));
        Subject = Normalize(subject, 160, nameof(subject));
        Body = Normalize(body, 2000, nameof(body));
        CreatedAt = createdAt;
    }

    public Guid Id { get; }
    public Guid RecipientUserId { get; }
    public string Type { get; }
    public string Subject { get; }
    public string Body { get; }
    public DateTimeOffset CreatedAt { get; }

    public static NotificationMessage Create(
        Guid recipientUserId,
        string type,
        string subject,
        string body,
        DateTimeOffset createdAt) =>
        new(Guid.NewGuid(), recipientUserId, type, subject, body, createdAt);

    private static string Normalize(string value, int maximumLength, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Hodnota nesmí být prázdná.", parameterName);
        }

        var normalized = value.Trim();
        if (normalized.Length > maximumLength)
        {
            throw new ArgumentException($"Hodnota překračuje limit {maximumLength} znaků.", parameterName);
        }

        return normalized;
    }
}
