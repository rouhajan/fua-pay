using FuaPay.Web.Modules.Access.Application;
using FuaPay.Web.Modules.Access.Web;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace FuaPay.Web.Modules.Access.Infrastructure.Entra;

public static class EntraAuthenticationServiceCollectionExtensions
{
    public static AuthenticationBuilder AddEntraAuthentication(
        this AuthenticationBuilder authentication,
        EntraAuthenticationConfiguration configuration,
        string applicationRoot)
    {
        ArgumentNullException.ThrowIfNull(authentication);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationRoot);

        authentication.Services.AddSingleton(
            new EntraAuthenticationAvailability(
                configuration.Enabled,
                configuration.TenantId));

        if (!configuration.Enabled)
        {
            return authentication;
        }

        authentication.AddOpenIdConnect(
            EntraAuthenticationDefaults.AuthenticationScheme,
            options =>
            {
                options.Authority =
                    configuration.Authority!.AbsoluteUri;
                options.ClientId =
                    configuration.ClientId!.Value.ToString("D");
                options.ClientSecret = configuration.ClientSecret;
                options.CallbackPath = configuration.CallbackPath;
                options.SignedOutCallbackPath =
                    configuration.SignedOutCallbackPath;
                options.SignInScheme =
                    CookieAuthenticationDefaults.AuthenticationScheme;
                options.ResponseType = OpenIdConnectResponseType.Code;
                options.ResponseMode = OpenIdConnectResponseMode.FormPost;
                options.UsePkce = true;
                options.RequireHttpsMetadata = true;
                options.MapInboundClaims = false;
                options.GetClaimsFromUserInfoEndpoint = false;
                options.SaveTokens = false;

                options.Scope.Clear();
                options.Scope.Add("openid");
                options.Scope.Add("profile");
                options.Scope.Add("email");

                options.TokenValidationParameters.ValidateIssuer = true;
                options.TokenValidationParameters.NameClaimType = "name";

                options.CorrelationCookie.HttpOnly = true;
                options.CorrelationCookie.SecurePolicy =
                    CookieSecurePolicy.Always;
                options.NonceCookie.HttpOnly = true;
                options.NonceCookie.SecurePolicy =
                    CookieSecurePolicy.Always;

                options.Events.OnTokenValidated = async context =>
                {
                    if (context.Principal is null)
                    {
                        context.Fail(
                            "Ověřená Entra odpověď neobsahuje identitu.");
                        return;
                    }

                    try
                    {
                        var identity =
                            EntraClaimsMapper.CreateVerifiedIdentity(
                                context.Principal,
                                configuration.TenantId!.Value);
                        var service = context.HttpContext.RequestServices
                            .GetRequiredService<AccessIdentityService>();
                        var resolution = await service.ResolveAsync(
                            identity,
                            context.HttpContext.RequestAborted);

                        context.Principal =
                            AccessClaimsPrincipalFactory.Create(
                                resolution.User,
                                CookieAuthenticationDefaults
                                    .AuthenticationScheme);
                    }
                    catch (Exception exception)
                        when (exception is
                            ArgumentException or
                            AccessUserBlockedException or
                            AccessUserConcurrencyException or
                            AccessIdentityConcurrencyException)
                    {
                        context.Fail(
                            "Entra identitu nelze bezpečně propojit " +
                            "s účtem FUA Pay.");
                    }
                };

                options.Events.OnRemoteFailure = context =>
                {
                    context.HandleResponse();
                    context.Response.Redirect(
                        $"{applicationRoot}?authenticationError=true");
                    return Task.CompletedTask;
                };
            });

        return authentication;
    }
}
