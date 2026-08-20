using FuaPay.Web.Modules.Access.Infrastructure.Entra;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace FuaPay.Web.Tests.Modules.Access.Infrastructure.Entra;

public sealed class EntraAuthenticationRegistrationTests
{
    [Fact]
    public void AddEntraAuthentication_UsesCodePkceWithoutTokenPersistence()
    {
        var tenantId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var services = new ServiceCollection();
        services.AddLogging();
        services
            .AddAuthentication(
                CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie()
            .AddEntraAuthentication(
                new EntraAuthenticationConfiguration(
                    true,
                    tenantId,
                    clientId,
                    "test-secret",
                    new Uri(
                        $"https://login.microsoftonline.com/" +
                        $"{tenantId:D}/v2.0"),
                    new PathString("/signin-oidc"),
                    new PathString("/signout-callback-oidc")),
                "/");
        using var provider = services.BuildServiceProvider();

        var options = provider
            .GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>()
            .Get(EntraAuthenticationDefaults.AuthenticationScheme);

        Assert.Equal("code", options.ResponseType);
        Assert.Equal("form_post", options.ResponseMode);
        Assert.True(options.UsePkce);
        Assert.False(options.SaveTokens);
        Assert.False(options.MapInboundClaims);
        Assert.False(options.GetClaimsFromUserInfoEndpoint);
        Assert.True(options.RequireHttpsMetadata);
        Assert.Equal(
            ["openid", "profile", "email"],
            options.Scope);
        Assert.True(
            options.TokenValidationParameters.ValidateIssuer);
        Assert.Equal(
            CookieSecurePolicy.Always,
            options.CorrelationCookie.SecurePolicy);
        Assert.Equal(
            CookieSecurePolicy.Always,
            options.NonceCookie.SecurePolicy);
    }

    [Fact]
    public async Task AddEntraAuthentication_RemoteFailureUsesTrustedApplicationRoot()
    {
        var tenantId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var services = new ServiceCollection();
        services.AddLogging();
        services
            .AddAuthentication(
                CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie()
            .AddEntraAuthentication(
                new EntraAuthenticationConfiguration(
                    true,
                    tenantId,
                    clientId,
                    "test-secret",
                    new Uri(
                        $"https://login.microsoftonline.com/" +
                        $"{tenantId:D}/v2.0"),
                    new PathString("/signin-oidc"),
                    new PathString("/signout-callback-oidc")),
                "/fuapay/");

        using var provider = services.BuildServiceProvider();

        var options = provider
            .GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>()
            .Get(EntraAuthenticationDefaults.AuthenticationScheme);

        var scheme = Assert.IsType<AuthenticationScheme>(
            await provider
                .GetRequiredService<IAuthenticationSchemeProvider>()
                .GetSchemeAsync(
                    EntraAuthenticationDefaults.AuthenticationScheme));

        var httpContext = new DefaultHttpContext();
        httpContext.Request.PathBase =
            new PathString("//attacker.example");

        var failureContext = new RemoteFailureContext(
            httpContext,
            scheme,
            options,
            new InvalidOperationException("test failure"));

        await options.Events.OnRemoteFailure(failureContext);

        Assert.Equal(
            "/fuapay/?authenticationError=true",
            httpContext.Response.Headers["Location"].ToString());
    }
}
