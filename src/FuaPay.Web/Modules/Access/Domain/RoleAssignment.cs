namespace FuaPay.Web.Modules.Access.Domain;

public sealed class RoleAssignment
{
    internal RoleAssignment(
        Guid id,
        AccessRole role,
        DateTimeOffset grantedAt,
        RoleChangeActor grantedBy)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException(
                "ID přiřazení role nesmí být prázdné.",
                nameof(id));
        }

        ArgumentNullException.ThrowIfNull(grantedBy);

        Id = id;
        Role = role;
        GrantedAt = grantedAt;
        GrantedBy = grantedBy;
    }

    public Guid Id { get; }

    public AccessRole Role { get; }

    public DateTimeOffset GrantedAt { get; }

    public RoleChangeActor GrantedBy { get; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public RoleChangeActor? RevokedBy { get; private set; }

    public bool IsActive => RevokedAt is null;

    internal void Revoke(
        DateTimeOffset revokedAt,
        RoleChangeActor revokedBy)
    {
        ArgumentNullException.ThrowIfNull(revokedBy);

        if (!IsActive)
        {
            throw new InvalidOperationException(
                $"Přiřazení role '{Id}' již bylo odebráno.");
        }

        if (revokedAt < GrantedAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(revokedAt),
                "Role nesmí být odebrána před svým přidělením.");
        }

        RevokedAt = revokedAt;
        RevokedBy = revokedBy;
    }
}
