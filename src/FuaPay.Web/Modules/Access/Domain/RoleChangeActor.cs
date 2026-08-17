namespace FuaPay.Web.Modules.Access.Domain;

public sealed record RoleChangeActor
{
    private RoleChangeActor(
        RoleChangeActorType type,
        Guid? userId,
        string? processName)
    {
        Type = type;
        UserId = userId;
        ProcessName = processName;
    }

    public RoleChangeActorType Type { get; }

    public Guid? UserId { get; }

    public string? ProcessName { get; }

    public static RoleChangeActor ForUser(Guid userId)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException(
                "ID původce změny role nesmí být prázdné.",
                nameof(userId));
        }

        return new RoleChangeActor(
            RoleChangeActorType.User,
            userId,
            null);
    }

    public static RoleChangeActor ForProcess(
        string processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
        {
            throw new ArgumentException(
                "Název procesu nesmí být prázdný.",
                nameof(processName));
        }

        var normalized = processName.Trim();

        if (
            normalized.Length >
            AccessTextLimits.ProcessNameMaxLength)
        {
            throw new ArgumentException(
                "Název procesu je příliš dlouhý.",
                nameof(processName));
        }

        return new RoleChangeActor(
            RoleChangeActorType.Process,
            null,
            normalized);
    }
}
