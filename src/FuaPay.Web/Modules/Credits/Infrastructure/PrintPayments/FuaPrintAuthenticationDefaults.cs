namespace FuaPay.Web.Modules.Credits.Infrastructure.PrintPayments;

public static class FuaPrintAuthenticationDefaults
{
    public const string AuthenticationScheme = "FuaPrintService";

    public const string AuthorizationPolicy = "FuaPrintService";

    public const string PrintSourceIdClaim =
        "fua-pay:print-source-id";

    public const string RateLimitPolicy = "FuaPrintService";
}
