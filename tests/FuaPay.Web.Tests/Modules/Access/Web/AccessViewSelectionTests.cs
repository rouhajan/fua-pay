using System.Security.Claims;

using FuaPay.Web.Modules.Access.Domain;
using FuaPay.Web.Modules.Access.Web;

namespace FuaPay.Web.Tests.Modules.Access.Web;

public sealed class AccessViewSelectionTests
{
    [Fact]
    public void Select_CustomerOnly_DefaultsToCustomer()
    {
        var selection = Assert.IsType<AccessViewSelection>(
            AccessViewSelector.Select(
                CreatePrincipal(
                    AccessRole.Customer)));

        Assert.Equal(
            AccessView.Customer,
            selection.Active.View);

        Assert.Equal(
            new[] { AccessView.Customer },
            selection.Available.Select(option => option.View));
    }

    [Fact]
    public void Select_Requester_DefaultsToRequester()
    {
        var selection = Assert.IsType<AccessViewSelection>(
            AccessViewSelector.Select(
                CreatePrincipal(
                    AccessRole.Customer,
                    AccessRole.Requester)));

        Assert.Equal(
            AccessView.Requester,
            selection.Active.View);
    }

    [Fact]
    public void Select_Admin_DefaultsToAdministration()
    {
        var selection = Assert.IsType<AccessViewSelection>(
            AccessViewSelector.Select(
                CreatePrincipal(
                    AccessRole.Customer,
                    AccessRole.Requester,
                    AccessRole.Admin)));

        Assert.Equal(
            AccessView.Admin,
            selection.Active.View);
    }

    [Fact]
    public void Select_AuthorizedRequestedView_UsesRequestedView()
    {
        var selection = Assert.IsType<AccessViewSelection>(
            AccessViewSelector.Select(
                CreatePrincipal(
                    AccessRole.Customer,
                    AccessRole.Requester),
                " CUSTOMER "));

        Assert.Equal(
            AccessView.Customer,
            selection.Active.View);
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("unsupported")]
    public void Select_UnavailableRequestedView_FallsBackToDefault(
        string requestedView)
    {
        var selection = Assert.IsType<AccessViewSelection>(
            AccessViewSelector.Select(
                CreatePrincipal(
                    AccessRole.Customer,
                    AccessRole.Requester),
                requestedView));

        Assert.Equal(
            AccessView.Requester,
            selection.Active.View);
    }

    [Fact]
    public void Select_AuthenticatedPrincipalWithoutRoles_ReturnsNull()
    {
        Assert.Null(
            AccessViewSelector.Select(
                CreatePrincipal()));
    }

    [Fact]
    public void Select_UnauthenticatedPrincipal_ReturnsNull()
    {
        var principal = new ClaimsPrincipal(
            new ClaimsIdentity());

        Assert.Null(
            AccessViewSelector.Select(principal));
    }

    private static ClaimsPrincipal CreatePrincipal(
        params AccessRole[] roles)
    {
        var claims = roles.Select(
            role =>
                new Claim(
                    ClaimTypes.Role,
                    role.ToString()));

        return new ClaimsPrincipal(
            new ClaimsIdentity(
                claims,
                "TestScheme",
                ClaimTypes.Name,
                ClaimTypes.Role));
    }
}
