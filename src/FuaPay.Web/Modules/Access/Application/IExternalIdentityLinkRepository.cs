using FuaPay.Web.Modules.Access.Domain;

namespace FuaPay.Web.Modules.Access.Application;

public interface IExternalIdentityLinkRepository
{
    Task AttachAsync(
        Guid userId,
        ExternalIdentityKey identityKey,
        CancellationToken cancellationToken = default);
}
