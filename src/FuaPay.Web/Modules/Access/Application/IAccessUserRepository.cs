using FuaPay.Web.Modules.Access.Domain;

namespace FuaPay.Web.Modules.Access.Application;

public interface IAccessUserRepository
{
    Task<AccessUser?> FindByIdAsync(
        Guid userId,
        CancellationToken cancellationToken);

    Task<AccessUser?> FindByExternalIdentityAsync(
        ExternalIdentityKey identityKey,
        CancellationToken cancellationToken);

    Task AddAsync(
        AccessUser user,
        ExternalIdentityKey identityKey,
        CancellationToken cancellationToken);

    Task SaveAsync(
        AccessUser user,
        CancellationToken cancellationToken);
}
