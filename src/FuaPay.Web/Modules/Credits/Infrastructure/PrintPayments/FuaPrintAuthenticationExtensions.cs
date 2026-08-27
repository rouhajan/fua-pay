using System.Security.Claims;
using System.Threading.RateLimiting;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;

namespace FuaPay.Web.Modules.Credits.Infrastructure.PrintPayments;

public static class FuaPrintAuthenticationExtensions
{
    public static AuthenticationBuilder AddFuaPrintAuthentication(
        this AuthenticationBuilder authentication,
        PrintPaymentsConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(authentication);
        ArgumentNullException.ThrowIfNull(configuration);

        authentication.Services.AddSingleton(configuration);
        authentication.Services.AddSingleton<FuaPrintCredentialValidator>();
        authentication.Services.AddRateLimiter(
            options =>
            {
                options.RejectionStatusCode =
                    StatusCodes.Status429TooManyRequests;
                options.AddPolicy(
                    FuaPrintAuthenticationDefaults.RateLimitPolicy,
                    context => RateLimitPartition.GetFixedWindowLimiter(
                        context.Connection.RemoteIpAddress?.ToString()
                            ?? "unknown",
                        _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = 120,
                            Window = TimeSpan.FromMinutes(1),
                            QueueLimit = 0,
                            QueueProcessingOrder =
                                QueueProcessingOrder.OldestFirst,
                            AutoReplenishment = true
                        }));
            });

        return authentication.AddScheme<
            AuthenticationSchemeOptions,
            FuaPrintAuthenticationHandler>(
                FuaPrintAuthenticationDefaults.AuthenticationScheme,
                _ => { });
    }

    public static void AddFuaPrintPolicy(
        this AuthorizationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.AddPolicy(
            FuaPrintAuthenticationDefaults.AuthorizationPolicy,
            policy =>
            {
                policy.AddAuthenticationSchemes(
                    FuaPrintAuthenticationDefaults.AuthenticationScheme);
                policy.RequireAuthenticatedUser();
                policy.RequireClaim(
                    FuaPrintAuthenticationDefaults.PrintSourceIdClaim);
            });
    }

    public static Guid GetRequiredPrintSourceId(
        this ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);

        var values = principal
            .FindAll(FuaPrintAuthenticationDefaults.PrintSourceIdClaim)
            .Select(claim => claim.Value)
            .ToArray();

        if (
            values.Length != 1 ||
            !Guid.TryParse(values[0], out var printSourceId) ||
            printSourceId == Guid.Empty)
        {
            throw new InvalidOperationException(
                "Authenticated FUA Print identity has no valid source ID.");
        }

        return printSourceId;
    }
}
