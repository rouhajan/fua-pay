using FuaPay.Web.Modules.Access.Domain;

namespace FuaPay.Web.Modules.Access.Application;

public sealed class AccessUserBlockedException :
    InvalidOperationException
{
    public AccessUserBlockedException(Guid userId)
        : base(
            $"Uživatel '{userId}' je zablokovaný.")
    {
        UserId = userId;
    }

    public Guid UserId { get; }
}

public sealed class AccessUserConcurrencyException :
    InvalidOperationException
{
    public AccessUserConcurrencyException(
        Guid userId,
        Exception? innerException = null)
        : base(
            $"Uživatel '{userId}' byl souběžně změněn " +
            "jiným požadavkem.",
            innerException)
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

public sealed class AccessIdentityConcurrencyException :
    InvalidOperationException
{
    public AccessIdentityConcurrencyException(
        ExternalIdentityKey identityKey,
        Exception? innerException = null)
        : base(
            "Externí identita byla souběžně zpracována " +
            "jiným požadavkem.",
            innerException)
    {
        ArgumentNullException.ThrowIfNull(identityKey);

        IdentityKey = identityKey;
    }

    public ExternalIdentityKey IdentityKey { get; }
}
