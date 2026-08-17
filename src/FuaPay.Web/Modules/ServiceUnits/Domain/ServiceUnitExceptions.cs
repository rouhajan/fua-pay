namespace FuaPay.Web.Modules.ServiceUnits.Domain;

public sealed class InactiveServiceUnitException :
    InvalidOperationException
{
    public InactiveServiceUnitException(Guid serviceUnitId)
        : base(
            $"Pracoviště '{serviceUnitId}' není aktivní.")
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

public sealed class RequesterAlreadyAssignedException :
    InvalidOperationException
{
    public RequesterAlreadyAssignedException(
        Guid serviceUnitId,
        Guid userId)
        : base(
            $"Uživatel '{userId}' již má aktivní přiřazení " +
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
