using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;

namespace FuaPay.Web.Modules.Access.Infrastructure.Entra;

public sealed record EntraAuthenticationConfiguration(
    bool Enabled,
    Guid? TenantId,
    Guid? ClientId,
    string ClientSecret,
    Uri? Authority,
    PathString CallbackPath,
    PathString SignedOutCallbackPath)
{
    private const string DefaultCallbackPath = "/signin-oidc";
    private const string DefaultSignedOutCallbackPath =
        "/signout-callback-oidc";

    public static EntraAuthenticationConfiguration Resolve(
        IConfiguration configuration,
        string environmentName,
        bool interactiveTestSignInEnabled)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(environmentName);

        var enabled = configuration.GetValue<bool>("Entra:Enabled");
        var isProduction = Environments.Production.Equals(
            environmentName,
            StringComparison.OrdinalIgnoreCase);

        if (isProduction && !enabled)
        {
            throw new InvalidOperationException(
                "Produkční prostředí vyžaduje zapnutou a platnou " +
                "konfiguraci Microsoft Entra ID.");
        }

        if (enabled && interactiveTestSignInEnabled)
        {
            throw new InvalidOperationException(
                "Microsoft Entra ID a interaktivní testovací " +
                "přihlášení nesmějí být aktivní současně.");
        }

        if (!enabled)
        {
            return new EntraAuthenticationConfiguration(
                false,
                null,
                null,
                string.Empty,
                null,
                new PathString(DefaultCallbackPath),
                new PathString(DefaultSignedOutCallbackPath));
        }

        var tenantId = ParseRequiredGuid(
            configuration["Entra:TenantId"],
            "Entra:TenantId");
        var clientId = ParseRequiredGuid(
            configuration["Entra:ClientId"],
            "Entra:ClientId");
        var clientSecret =
            configuration["Entra:ClientSecret"];

        if (string.IsNullOrWhiteSpace(clientSecret))
        {
            throw new InvalidOperationException(
                "Entra:ClientSecret musí být dodán bezpečnou " +
                "deployment konfigurací mimo Git.");
        }

        var callbackPath = ParseCallbackPath(
            configuration["Entra:CallbackPath"],
            DefaultCallbackPath,
            "Entra:CallbackPath");
        var signedOutCallbackPath = ParseCallbackPath(
            configuration["Entra:SignedOutCallbackPath"],
            DefaultSignedOutCallbackPath,
            "Entra:SignedOutCallbackPath");

        if (callbackPath == signedOutCallbackPath)
        {
            throw new InvalidOperationException(
                "Entra callback a signed-out callback musí používat " +
                "odlišné cesty.");
        }

        return new EntraAuthenticationConfiguration(
            true,
            tenantId,
            clientId,
            clientSecret,
            new Uri(
                $"https://login.microsoftonline.com/{tenantId:D}/v2.0"),
            callbackPath,
            signedOutCallbackPath);
    }

    private static Guid ParseRequiredGuid(
        string? value,
        string configurationKey)
    {
        if (
            !Guid.TryParse(value, out var parsed) ||
            parsed == Guid.Empty)
        {
            throw new InvalidOperationException(
                $"{configurationKey} musí být neprázdné GUID.");
        }

        return parsed;
    }

    private static PathString ParseCallbackPath(
        string? value,
        string defaultValue,
        string configurationKey)
    {
        var normalized = string.IsNullOrWhiteSpace(value)
            ? defaultValue
            : value.Trim();

        if (
            normalized == "/" ||
            !normalized.StartsWith("/", StringComparison.Ordinal) ||
            normalized.EndsWith("/", StringComparison.Ordinal) ||
            normalized.Contains('?') ||
            normalized.Contains('#'))
        {
            throw new InvalidOperationException(
                $"{configurationKey} musí být samostatná absolutní " +
                "aplikační cesta bez koncového lomítka.");
        }

        return new PathString(normalized);
    }
}
