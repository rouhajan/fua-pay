namespace FuaPay.Web.Modules.Access.Domain;

public sealed class DuplicateAccessRoleException :
    InvalidOperationException
{
    public DuplicateAccessRoleException(
        Guid userId,
        AccessRole role)
        : base(
            $"Uživatel '{userId}' již má aktivní roli '{role}'.")
    {
        UserId = userId;
        Role = role;
    }

    public Guid UserId { get; }

    public AccessRole Role { get; }
}

public sealed class AccessRoleNotAssignedException :
    InvalidOperationException
{
    public AccessRoleNotAssignedException(
        Guid userId,
        AccessRole role)
        : base(
            $"Uživatel '{userId}' nemá aktivní roli '{role}'.")
    {
        UserId = userId;
        Role = role;
    }

    public Guid UserId { get; }

    public AccessRole Role { get; }
}
