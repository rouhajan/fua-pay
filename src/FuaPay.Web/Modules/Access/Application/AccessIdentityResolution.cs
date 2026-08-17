using FuaPay.Web.Modules.Access.Domain;

namespace FuaPay.Web.Modules.Access.Application;

public sealed record AccessIdentityResolution
{
    public AccessIdentityResolution(
        AccessUser user,
        bool isNewUser)
    {
        ArgumentNullException.ThrowIfNull(user);

        User = user;
        IsNewUser = isNewUser;
    }

    public AccessUser User { get; }

    public bool IsNewUser { get; }
}
