using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;

using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace FuaPay.Web.Modules.Credits.Infrastructure.PrintPayments;

internal sealed class FuaPrintAuthenticationHandler :
    AuthenticationHandler<AuthenticationSchemeOptions>
{
    private readonly FuaPrintCredentialValidator _credentialValidator;

    public FuaPrintAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        FuaPrintCredentialValidator credentialValidator)
        : base(options, logger, encoder)
    {
        _credentialValidator = credentialValidator;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var authorizationHeader =
            Request.Headers.Authorization.Count == 1
                ? Request.Headers.Authorization.ToString()
                : null;

        if (
            !_credentialValidator.TryValidate(
                authorizationHeader,
                out var printSourceId))
        {
            return Task.FromResult(
                AuthenticateResult.Fail(
                    "FUA Print service authentication failed."));
        }

        var claims = new[]
        {
            new Claim(
                FuaPrintAuthenticationDefaults.PrintSourceIdClaim,
                printSourceId.ToString("D"))
        };
        var identity = new ClaimsIdentity(
            claims,
            FuaPrintAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(
            principal,
            FuaPrintAuthenticationDefaults.AuthenticationScheme);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    protected override async Task HandleChallengeAsync(
        AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.ContentType = "application/problem+json";
        await JsonSerializer.SerializeAsync(
            Response.Body,
            new
            {
                type = "about:blank",
                title = "Service authentication failed.",
                status = StatusCodes.Status401Unauthorized,
                code = "service_authentication_failed"
            },
            cancellationToken: Context.RequestAborted);
    }
}
