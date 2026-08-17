using FuaPay.Web.BuildingBlocks.Auditing;
using FuaPay.Web.Modules.Access.Domain;
using FuaPay.Web.Modules.Access.Infrastructure.Entra;

namespace FuaPay.Web.Modules.Access.Application;

public sealed class ExternalIdentityAdministrationService
{
    private readonly IAccessUserQueries _userQueries;
    private readonly IExternalIdentityLinkRepository _repository;
    private readonly IAuditTrail _auditTrail;
    private readonly TimeProvider _timeProvider;

    public ExternalIdentityAdministrationService(
        IAccessUserQueries userQueries,
        IExternalIdentityLinkRepository repository,
        IAuditTrail auditTrail,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(userQueries);
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(auditTrail);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _userQueries = userQueries;
        _repository = repository;
        _auditTrail = auditTrail;
        _timeProvider = timeProvider;
    }

    public async Task<bool> AttachEntraIdentityAsync(
        Guid administratorUserId,
        Guid targetUserId,
        Guid tenantId,
        Guid objectId,
        CancellationToken cancellationToken = default)
    {
        ValidateUserId(
            administratorUserId,
            nameof(administratorUserId));
        ValidateUserId(targetUserId, nameof(targetUserId));

        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException(
                "Entra tenant ID nesmí být prázdné.",
                nameof(tenantId));
        }

        if (objectId == Guid.Empty)
        {
            throw new ArgumentException(
                "Entra object ID nesmí být prázdné.",
                nameof(objectId));
        }

        var user = await _userQueries.FindDetailAsync(
                targetUserId,
                cancellationToken)
            ?? throw new AccessUserNotFoundException(targetUserId);
        var identityKey = ExternalIdentityKey.FromGuidIdentifiers(
            EntraAuthenticationDefaults.ExternalIdentityProvider,
            tenantId.ToString("D"),
            objectId.ToString("D"));
        var tenantIdentity = user.ExternalIdentities
            .SingleOrDefault(
                identity =>
                    string.Equals(
                        identity.Provider,
                        identityKey.Provider,
                        StringComparison.Ordinal) &&
                    string.Equals(
                        identity.Tenant,
                        identityKey.Tenant,
                        StringComparison.Ordinal));

        if (tenantIdentity is not null)
        {
            if (string.Equals(
                    tenantIdentity.Subject,
                    identityKey.Subject,
                    StringComparison.Ordinal))
            {
                return false;
            }

            throw new ExternalIdentityProviderAlreadyAssignedException(
                targetUserId,
                identityKey.Provider,
                identityKey.Tenant);
        }

        var now = _timeProvider.GetUtcNow();
        _auditTrail.Stage(AuditEntry.ForUser(
            administratorUserId,
            "access.external-identity.attached",
            "access-user",
            targetUserId.ToString(),
            $"K účtu byla ručně připojena ověřená Entra identita " +
            $"tenantu {identityKey.Tenant} a objektu " +
            $"{identityKey.Subject}.",
            now));

        await _repository.AttachAsync(
            targetUserId,
            identityKey,
            cancellationToken);

        return true;
    }

    private static void ValidateUserId(
        Guid value,
        string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException(
                "ID uživatele nesmí být prázdné.",
                parameterName);
        }
    }
}
