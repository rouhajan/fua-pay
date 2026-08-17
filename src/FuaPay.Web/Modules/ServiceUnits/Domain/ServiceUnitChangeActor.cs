namespace FuaPay.Web.Modules.ServiceUnits.Domain;

public sealed record ServiceUnitChangeActor
{
    private ServiceUnitChangeActor(
        ServiceUnitChangeActorType type,
        Guid? userId,
        string? processName)
    {
        Type = type;
        UserId = userId;
        ProcessName = processName;
    }

    public ServiceUnitChangeActorType Type { get; }

    public Guid? UserId { get; }

    public string? ProcessName { get; }

    public static ServiceUnitChangeActor ForUser(Guid userId)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException(
                "ID původce změny pracoviště nesmí být prázdné.",
                nameof(userId));
        }

        return new ServiceUnitChangeActor(
            ServiceUnitChangeActorType.User,
            userId,
            null);
    }

    public static ServiceUnitChangeActor ForProcess(
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
            ServiceUnitTextLimits.ProcessNameMaxLength)
        {
            throw new ArgumentException(
                "Název procesu je příliš dlouhý.",
                nameof(processName));
        }

        return new ServiceUnitChangeActor(
            ServiceUnitChangeActorType.Process,
            null,
            normalized);
    }
}
