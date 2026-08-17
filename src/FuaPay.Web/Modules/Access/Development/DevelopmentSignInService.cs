using FuaPay.Web.Modules.Access.Application;
using FuaPay.Web.Modules.Access.Domain;

namespace FuaPay.Web.Modules.Access.Development;

public sealed class DevelopmentSignInService
{
    private const string RoleGrantProcess =
        "development-sign-in";

    private readonly AccessIdentityService _identityService;
    private readonly IAccessUserRepository _repository;
    private readonly TimeProvider _timeProvider;

    public DevelopmentSignInService(
        AccessIdentityService identityService,
        IAccessUserRepository repository,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(identityService);
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _identityService = identityService;
        _repository = repository;
        _timeProvider = timeProvider;
    }

    public async Task<AccessUser> ResolveAsync(
        DevelopmentIdentityProfile profile,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var resolution =
            await _identityService.ResolveAsync(
                profile.Identity,
                cancellationToken);

        var rolesChanged = false;
        var now = _timeProvider.GetUtcNow();
        var actor = RoleChangeActor.ForProcess(
            RoleGrantProcess);

        foreach (var role in profile.Roles)
        {
            if (resolution.User.HasEffectiveRole(role))
            {
                continue;
            }

            resolution.User.GrantRole(
                Guid.NewGuid(),
                role,
                now,
                actor);

            rolesChanged = true;
        }

        foreach (var role in new[]
        {
            AccessRole.Requester,
            AccessRole.Admin
        })
        {
            if (
                profile.Roles.Contains(role) ||
                !resolution.User.HasEffectiveRole(role)
            )
            {
                continue;
            }

            resolution.User.RevokeRole(
                role,
                now,
                actor);

            rolesChanged = true;
        }

        if (rolesChanged)
        {
            await _repository.SaveAsync(
                resolution.User,
                cancellationToken);
        }

        return resolution.User;
    }
}
