namespace FuaPay.Web.Modules.ServiceUnits.Domain;

public sealed class RequesterServiceUnitAssignment
{
    public RequesterServiceUnitAssignment(
        Guid id,
        Guid serviceUnitId,
        Guid userId,
        DateTimeOffset grantedAt,
        ServiceUnitChangeActor grantedBy)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException(
                "ID přiřazení zadavatele nesmí být prázdné.",
                nameof(id));
        }

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

        ArgumentNullException.ThrowIfNull(grantedBy);

        Id = id;
        ServiceUnitId = serviceUnitId;
        UserId = userId;
        GrantedAt = grantedAt;
        GrantedBy = grantedBy;
    }

    public Guid Id { get; }

    public Guid ServiceUnitId { get; }

    public Guid UserId { get; }

    public DateTimeOffset GrantedAt { get; }

    public ServiceUnitChangeActor GrantedBy { get; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public ServiceUnitChangeActor? RevokedBy { get; private set; }

    public bool IsActive => RevokedAt is null;

    public void Revoke(
        DateTimeOffset revokedAt,
        ServiceUnitChangeActor revokedBy)
    {
        ArgumentNullException.ThrowIfNull(revokedBy);

        if (!IsActive)
        {
            throw new InvalidOperationException(
                $"Přiřazení zadavatele '{Id}' již bylo odebráno.");
        }

        if (revokedAt < GrantedAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(revokedAt),
                "Přiřazení nesmí být odebráno před přidělením.");
        }

        RevokedAt = revokedAt;
        RevokedBy = revokedBy;
    }
}
