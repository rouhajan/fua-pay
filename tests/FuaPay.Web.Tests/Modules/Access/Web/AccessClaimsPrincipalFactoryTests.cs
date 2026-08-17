using System.Security.Claims;

using FuaPay.Web.Modules.Access.Domain;
using FuaPay.Web.Modules.Access.Web;

namespace FuaPay.Web.Tests.Modules.Access.Web;

public sealed class AccessClaimsPrincipalFactoryTests
{
    [Fact]
    public void Create_ExportsInternalIdentityAndEffectiveRoles()
    {
        var createdAt =
            new DateTimeOffset(
                2026,
                7,
                28,
                12,
                0,
                0,
                TimeSpan.Zero);

        var user = new AccessUser(
            Guid.NewGuid(),
            "Test User",
            "test@example.cz",
            createdAt);

        user.GrantRole(
            Guid.NewGuid(),
            AccessRole.Customer,
            createdAt,
            RoleChangeActor.ForProcess("test"));

        user.GrantRole(
            Guid.NewGuid(),
            AccessRole.Requester,
            createdAt,
            RoleChangeActor.ForProcess("test"));

        var principal =
            AccessClaimsPrincipalFactory.Create(
                user,
                "TestScheme");

        Assert.True(principal.Identity?.IsAuthenticated);
        Assert.Equal(user.Id, principal.FindAccessUserId());
        Assert.Equal(user.DisplayName, principal.Identity?.Name);

        Assert.Equal(
            user.Email,
            principal.FindFirst(ClaimTypes.Email)?.Value);

        Assert.Equal(
            new[]
            {
                AccessRole.Customer,
                AccessRole.Requester
            },
            principal
                .FindAccessRoles()
                .OrderBy(role => role));
    }

    [Fact]
    public void FindAccessRoles_IgnoresUnsupportedRoleClaims()
    {
        var principal = new ClaimsPrincipal(
            new ClaimsIdentity(
                [
                    new Claim(
                        ClaimTypes.Role,
                        AccessRole.Customer.ToString()),
                    new Claim(
                        ClaimTypes.Role,
                        "Unsupported")
                ],
                "TestScheme"));

        Assert.Equal(
            new[] { AccessRole.Customer },
            principal.FindAccessRoles());
    }
}
