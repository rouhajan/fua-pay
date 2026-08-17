namespace FuaPay.Web.Modules.ServiceUnits.Application;

public sealed class ServiceUnitNotFoundException :
    InvalidOperationException
{
    public ServiceUnitNotFoundException(Guid serviceUnitId)
        : base($"Pracoviště '{serviceUnitId}' nebylo nalezeno.")
    {
        if (serviceUnitId == Guid.Empty)
        {
            throw new ArgumentException(
                "ID pracoviště nesmí být prázdné.",
                nameof(serviceUnitId));
        }

        ServiceUnitId = serviceUnitId;
    }

    public Guid ServiceUnitId { get; }
}

public sealed class ServiceUnitCodeAlreadyUsedException :
    InvalidOperationException
{
    public ServiceUnitCodeAlreadyUsedException(
        string code,
        Exception? innerException = null)
        : base(
            $"Kód pracoviště '{code}' již používá jiné pracoviště.",
            innerException)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ArgumentException(
                "Kód pracoviště nesmí být prázdný.",
                nameof(code));
        }

        Code = code;
    }

    public string Code { get; }
}

public sealed class ServiceUnitConcurrencyException :
    InvalidOperationException
{
    public ServiceUnitConcurrencyException(
        Guid serviceUnitId,
        Exception? innerException = null)
        : base(
            $"Pracoviště '{serviceUnitId}' bylo souběžně změněno.",
            innerException)
    {
        if (serviceUnitId == Guid.Empty)
        {
            throw new ArgumentException(
                "ID pracoviště nesmí být prázdné.",
                nameof(serviceUnitId));
        }

        ServiceUnitId = serviceUnitId;
    }

    public Guid ServiceUnitId { get; }
}

public sealed class RequesterAssignmentNotFoundException :
    InvalidOperationException
{
    public RequesterAssignmentNotFoundException(
        Guid serviceUnitId,
        Guid userId)
        : base(
            $"Uživatel '{userId}' nemá aktivní přiřazení " +
            $"k pracovišti '{serviceUnitId}'.")
    {
        if (serviceUnitId == Guid.Empty)
        {
            throw new ArgumentException(
                "ID pracoviště nesmí být prázdné.",
                nameof(serviceUnitId));
        }

        if (userId == Guid.Empty)
        {
            throw new ArgumentException(
                "ID uživatele nesmí být prázdné.",
                nameof(userId));
        }

        ServiceUnitId = serviceUnitId;
        UserId = userId;
    }

    public Guid ServiceUnitId { get; }

    public Guid UserId { get; }
}

public sealed class RequesterAssignmentConcurrencyException :
    InvalidOperationException
{
    public RequesterAssignmentConcurrencyException(
        Guid assignmentId,
        Exception? innerException = null)
        : base(
            $"Přiřazení zadavatele '{assignmentId}' bylo " +
            "souběžně změněno.",
            innerException)
    {
        if (assignmentId == Guid.Empty)
        {
            throw new ArgumentException(
                "ID přiřazení zadavatele nesmí být prázdné.",
                nameof(assignmentId));
        }

        AssignmentId = assignmentId;
    }

    public Guid AssignmentId { get; }
}

public sealed class RequesterRoleRequiredException :
    InvalidOperationException
{
    public RequesterRoleRequiredException(Guid userId)
        : base(
            $"Uživatel '{userId}' nemá aktivní roli zadavatele.")
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException(
                "ID uživatele nesmí být prázdné.",
                nameof(userId));
        }

        UserId = userId;
    }

    public Guid UserId { get; }
}
