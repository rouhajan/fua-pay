using FuaPay.Web.Modules.Access.Domain;

namespace FuaPay.Web.Modules.Access.Application;

public sealed class ExternalIdentityAlreadyAssignedException :
    InvalidOperationException
{
    public ExternalIdentityAlreadyAssignedException(
        ExternalIdentityKey identityKey,
        Exception? innerException = null)
        : base(
            "Externí identita už je přiřazena účtu FUA Pay.",
            innerException)
    {
        ArgumentNullException.ThrowIfNull(identityKey);
        IdentityKey = identityKey;
    }

    public ExternalIdentityKey IdentityKey { get; }
}

public sealed class ExternalIdentityProviderAlreadyAssignedException :
    InvalidOperationException
{
    public ExternalIdentityProviderAlreadyAssignedException(
        Guid userId,
        string provider,
        string tenant,
        Exception? innerException = null)
        : base(
            "Uživatel už má přiřazenou jinou identitu stejného " +
            "poskytovatele a tenantu.",
            innerException)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException(
                "ID uživatele nesmí být prázdné.",
                nameof(userId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(tenant);

        UserId = userId;
        Provider = provider;
        Tenant = tenant;
    }

    public Guid UserId { get; }

    public string Provider { get; }

    public string Tenant { get; }
}
