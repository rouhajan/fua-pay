using FuaPay.Web.Modules.Access.Infrastructure.Entra;
using FuaPay.Web.Pages.Account;

using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;

namespace FuaPay.Web.Tests.Pages;

public sealed class SignOutPageTests
{
    [Fact]
    public void EntraEnabled_PostSignsOutCookieAndOidcAndReturnsHome()
    {
        var model = Create(enabled: true);

        var result = Assert.IsType<SignOutResult>(model.OnPost());

        Assert.Equal("/", result.Properties?.RedirectUri);
        Assert.Equal(
            new[]
            {
                CookieAuthenticationDefaults.AuthenticationScheme,
                EntraAuthenticationDefaults.AuthenticationScheme
            },
            result.AuthenticationSchemes);
    }

    [Fact]
    public void EntraDisabled_PostSignsOutOnlyLocalCookie()
    {
        var model = Create(enabled: false);

        var result = Assert.IsType<SignOutResult>(model.OnPost());

        Assert.Equal("/", result.Properties?.RedirectUri);
        Assert.Equal(
            new[] { CookieAuthenticationDefaults.AuthenticationScheme },
            result.AuthenticationSchemes);
    }

    private static SignOutModel Create(bool enabled)
    {
        var model = new SignOutModel(
            new EntraAuthenticationAvailability(enabled, null))
        {
            PageContext = new PageContext
            {
                HttpContext = new DefaultHttpContext()
            },
            Url = new RootUrlHelper()
        };
        return model;
    }

    private sealed class RootUrlHelper : IUrlHelper
    {
        public ActionContext ActionContext { get; } = new();

        public string? Action(UrlActionContext actionContext) => null;

        public string? Content(string? contentPath) =>
            contentPath == "~/" ? "/" : contentPath;

        public bool IsLocalUrl(string? url) => true;

        public string? Link(string? routeName, object? values) => null;

        public string? RouteUrl(UrlRouteContext routeContext) => null;
    }
}
