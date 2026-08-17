using System.Collections.ObjectModel;

namespace FuaPay.Web.Modules.Access.Domain;

public sealed class AccessUser
{
    private readonly List<RoleAssignment> _roleAssignments = [];

    private readonly ReadOnlyCollection<RoleAssignment>
        _readOnlyRoleAssignments;

    public AccessUser(
        Guid id,
        string displayName,
        string? email,
        DateTimeOffset createdAt)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException(
                "ID uživatele nesmí být prázdné.",
                nameof(id));
        }

        Id = id;
        DisplayName = NormalizeDisplayName(displayName);
        Email = NormalizeEmail(email);
        Status = AccessUserStatus.Active;
        CreatedAt = createdAt;
        LastSeenAt = createdAt;

        _readOnlyRoleAssignments =
            _roleAssignments.AsReadOnly();
    }

    public Guid Id { get; }

    public string DisplayName { get; private set; }

    public string? Email { get; private set; }

    public AccessUserStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset LastSeenAt { get; private set; }

    public IReadOnlyList<RoleAssignment> RoleAssignments =>
        _readOnlyRoleAssignments;

    public IReadOnlyCollection<AccessRole> AssignedRoles =>
        _roleAssignments
            .Where(assignment => assignment.IsActive)
            .Select(assignment => assignment.Role)
            .ToHashSet();

    public bool HasEffectiveRole(AccessRole role)
    {
        ValidateRole(role);

        return
            Status == AccessUserStatus.Active &&
            _roleAssignments.Any(
                assignment =>
                    assignment.IsActive &&
                    assignment.Role == role);
    }

    public RoleAssignment GrantRole(
        Guid assignmentId,
        AccessRole role,
        DateTimeOffset grantedAt,
        RoleChangeActor grantedBy)
    {
        ValidateRole(role);
        ArgumentNullException.ThrowIfNull(grantedBy);

        if (grantedAt < CreatedAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(grantedAt),
                "Role nesmí být přidělena před vytvořením uživatele.");
        }

        if (
            _roleAssignments.Any(
                assignment =>
                    assignment.IsActive &&
                    assignment.Role == role))
        {
            throw new DuplicateAccessRoleException(
                Id,
                role);
        }

        if (
            _roleAssignments.Any(
                assignment =>
                    assignment.Id == assignmentId))
        {
            throw new ArgumentException(
                "ID přiřazení role již bylo použito.",
                nameof(assignmentId));
        }

        var latestRevokedAt =
            _roleAssignments
                .Where(
                    assignment =>
                        assignment.Role == role &&
                        !assignment.IsActive)
                .Select(
                    assignment =>
                        assignment.RevokedAt!.Value)
                .DefaultIfEmpty(CreatedAt)
                .Max();

        if (grantedAt < latestRevokedAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(grantedAt),
                "Role nesmí být znovu přidělena před svým " +
                "předchozím odebráním.");
        }

        var assignment = new RoleAssignment(
            assignmentId,
            role,
            grantedAt,
            grantedBy);

        _roleAssignments.Add(assignment);

        return assignment;
    }

    public RoleAssignment RevokeRole(
        AccessRole role,
        DateTimeOffset revokedAt,
        RoleChangeActor revokedBy)
    {
        ValidateRole(role);
        ArgumentNullException.ThrowIfNull(revokedBy);

        var assignment =
            _roleAssignments.SingleOrDefault(
                item =>
                    item.IsActive &&
                    item.Role == role);

        if (assignment is null)
        {
            throw new AccessRoleNotAssignedException(
                Id,
                role);
        }

        assignment.Revoke(
            revokedAt,
            revokedBy);

        return assignment;
    }

    public void Block()
    {
        Status = AccessUserStatus.Blocked;
    }

    public void Activate()
    {
        Status = AccessUserStatus.Active;
    }

    public void SynchronizeProfile(
        string displayName,
        string? email,
        DateTimeOffset observedAt)
    {
        if (observedAt < LastSeenAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(observedAt),
                "Starší pozorování nesmí přepsat novější profil.");
        }

        DisplayName = NormalizeDisplayName(displayName);
        Email = NormalizeEmail(email);
        LastSeenAt = observedAt;
    }

    private static string NormalizeDisplayName(
        string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new ArgumentException(
                "Zobrazované jméno nesmí být prázdné.",
                nameof(displayName));
        }

        var normalized = displayName.Trim();

        if (
            normalized.Length >
            AccessTextLimits.DisplayNameMaxLength)
        {
            throw new ArgumentException(
                "Zobrazované jméno je příliš dlouhé.",
                nameof(displayName));
        }

        return normalized;
    }

    private static string? NormalizeEmail(string? email)
    {
        if (email is null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(email))
        {
            throw new ArgumentException(
                "E-mail nesmí být prázdný řetězec.",
                nameof(email));
        }

        var normalized = email.Trim();

        if (
            normalized.Length >
            AccessTextLimits.EmailMaxLength)
        {
            throw new ArgumentException(
                "E-mail je příliš dlouhý.",
                nameof(email));
        }

        return normalized;
    }

    private static void ValidateRole(AccessRole role)
    {
        if (!Enum.IsDefined(role))
        {
            throw new ArgumentOutOfRangeException(
                nameof(role),
                "Aplikační role není platná.");
        }
    }
}
