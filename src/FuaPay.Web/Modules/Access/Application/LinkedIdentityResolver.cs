using FuaPay.Web.Modules.Access.Domain;
using FuaPay.Web.Modules.Access.Infrastructure.Entra;

namespace FuaPay.Web.Modules.Access.Application;

public sealed class LinkedIdentityResolver
{
    private readonly IAccessUserRepository _repository;

    public LinkedIdentityResolver(IAccessUserRepository repository)
    {
        ArgumentNullException.ThrowIfNull(repository);
        _repository = repository;
    }

    public async Task<Guid> ResolveMicrosoftEntraAsync(
        Guid tenantId,
        Guid objectId,
        CancellationToken cancellationToken = default)
    {
        ValidateId(tenantId, nameof(tenantId));
        ValidateId(objectId, nameof(objectId));

        var identityKey = ExternalIdentityKey.FromGuidIdentifiers(
            EntraAuthenticationDefaults.ExternalIdentityProvider,
            tenantId.ToString("D"),
            objectId.ToString("D"));
        var user = await _repository.FindByExternalIdentityAsync(
            identityKey,
            cancellationToken);

        if (user is null)
        {
            throw new LinkedIdentityNotFoundException(identityKey);
        }

        if (
            user.Status != AccessUserStatus.Active ||
            !user.HasEffectiveRole(AccessRole.Customer))
        {
            throw new LinkedIdentityNotEligibleException(user.Id);
        }

        return user.Id;
    }

    private static void ValidateId(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException(
                "Microsoft Entra identity identifiers must not be empty.",
                parameterName);
        }
    }
}

public sealed class LinkedIdentityNotFoundException :
    InvalidOperationException
{
    public LinkedIdentityNotFoundException(
        ExternalIdentityKey identityKey)
        : base("The Microsoft Entra identity is not linked to FUA Pay.")
    {
        ArgumentNullException.ThrowIfNull(identityKey);
        IdentityKey = identityKey;
    }

    public ExternalIdentityKey IdentityKey { get; }
}

public sealed class LinkedIdentityNotEligibleException :
    InvalidOperationException
{
    public LinkedIdentityNotEligibleException(Guid userId)
        : base("The linked FUA Pay user is not eligible for printing.")
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException(
                "User ID must not be empty.",
                nameof(userId));
        }

        UserId = userId;
    }

    public Guid UserId { get; }
}
