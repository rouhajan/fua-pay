using FuaPay.Web.BuildingBlocks.Auditing;
using FuaPay.Web.Modules.Access.Domain;

namespace FuaPay.Web.Modules.Access.Application;

public sealed class AccessIdentityService
{
    private const string FirstLoginProcess =
        "first-login";

    private readonly IAccessUserRepository _repository;
    private readonly IAuditTrail _auditTrail;
    private readonly TimeProvider _timeProvider;

    public AccessIdentityService(
        IAccessUserRepository repository,
        IAuditTrail auditTrail,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(auditTrail);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _repository = repository;
        _auditTrail = auditTrail;
        _timeProvider = timeProvider;
    }

    public async Task<AccessIdentityResolution> ResolveAsync(
        VerifiedExternalIdentity identity,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identity);

        var user =
            await _repository.FindByExternalIdentityAsync(
                identity.Key,
                cancellationToken);

        if (user is null)
        {
            return await CreateUserAsync(
                identity,
                cancellationToken);
        }

        if (user.Status == AccessUserStatus.Blocked)
        {
            throw new AccessUserBlockedException(user.Id);
        }

        user.SynchronizeProfile(
            identity.DisplayName,
            identity.Email,
            _timeProvider.GetUtcNow());

        await _repository.SaveAsync(
            user,
            cancellationToken);

        return new AccessIdentityResolution(
            user,
            isNewUser: false);
    }

    private async Task<AccessIdentityResolution> CreateUserAsync(
        VerifiedExternalIdentity identity,
        CancellationToken cancellationToken)
    {
        var createdAt =
            _timeProvider.GetUtcNow();

        var user = new AccessUser(
            Guid.NewGuid(),
            identity.DisplayName,
            identity.Email,
            createdAt);

        user.GrantRole(
            Guid.NewGuid(),
            AccessRole.Customer,
            createdAt,
            RoleChangeActor.ForProcess(
                FirstLoginProcess));

        _auditTrail.Stage(AuditEntry.ForProcess(
            FirstLoginProcess,
            "access.user-provisioned",
            "access-user",
            user.Id.ToString(),
            "Při prvním přihlášení byl vytvořen interní " +
            "uživatel, externí identita a role Customer.",
            createdAt));

        try
        {
            await _repository.AddAsync(
                user,
                identity.Key,
                cancellationToken);
        }
        catch (AccessIdentityConcurrencyException)
        {
            var existingUser =
                await _repository.FindByExternalIdentityAsync(
                    identity.Key,
                    cancellationToken);

            if (existingUser is null)
            {
                throw;
            }

            if (existingUser.Status == AccessUserStatus.Blocked)
            {
                throw new AccessUserBlockedException(
                    existingUser.Id);
            }

            return new AccessIdentityResolution(
                existingUser,
                isNewUser: false);
        }

        return new AccessIdentityResolution(
            user,
            isNewUser: true);
    }
}
