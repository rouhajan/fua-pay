using System.Net;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;

namespace FuaPay.Web.Hosting;

public sealed record FuaPayHostingConfiguration(
    PathString PathBase,
    bool ForwardedHeadersEnabled,
    IReadOnlyList<IPAddress> KnownProxies,
    string? DataProtectionKeyRingPath)
{
    public static FuaPayHostingConfiguration Resolve(
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var pathBase = ResolvePathBase(
            configuration["Hosting:PathBase"]);
        var forwardedHeadersEnabled =
            configuration.GetValue<bool>(
                "Hosting:UseForwardedHeaders");
        var knownProxies = ResolveKnownProxies(
            configuration
                .GetSection("Hosting:KnownProxies")
                .Get<string[]>() ?? []);
        var keyRingPath = ResolveKeyRingPath(
            configuration["DataProtection:KeyRingPath"]);

        if (
            forwardedHeadersEnabled &&
            knownProxies.Count == 0)
        {
            throw new InvalidOperationException(
                "Hosting:KnownProxies musí při zapnutých " +
                "forwarded headers obsahovat alespoň jednu " +
                "konkrétní IP adresu důvěryhodné reverzní proxy.");
        }

        if (
            !forwardedHeadersEnabled &&
            knownProxies.Count > 0)
        {
            throw new InvalidOperationException(
                "Hosting:KnownProxies nesmí být nastaveno, " +
                "pokud je Hosting:UseForwardedHeaders vypnuto.");
        }

        return new FuaPayHostingConfiguration(
            pathBase,
            forwardedHeadersEnabled,
            knownProxies,
            keyRingPath);
    }

    public void ValidateForEnvironment(
        string environmentName,
        IConfiguration configuration)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            environmentName);
        ArgumentNullException.ThrowIfNull(configuration);

        if (
            !string.Equals(
                environmentName,
                Environments.Production,
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (
            configuration.GetValue<bool>(
                "Database:ApplyMigrationsOnStart"))
        {
            throw new InvalidOperationException(
                "Database:ApplyMigrationsOnStart nesmí být v " +
                "Production zapnuto. Produkční migrace musí " +
                "proběhnout jako samostatný řízený deployment krok.");
        }

        ValidateProductionAllowedHosts(
            configuration["AllowedHosts"]);

        if (
            string.IsNullOrWhiteSpace(
                DataProtectionKeyRingPath))
        {
            throw new InvalidOperationException(
                "Produkční prostředí vyžaduje trvalou absolutní " +
                "cestu DataProtection:KeyRingPath mimo release " +
                "adresář.");
        }

        if (!Directory.Exists(DataProtectionKeyRingPath))
        {
            throw new InvalidOperationException(
                "Produkční Data Protection key ring adresář " +
                $"neexistuje: {DataProtectionKeyRingPath}");
        }
    }

    private static PathString ResolvePathBase(
        string? configuredPathBase)
    {
        if (string.IsNullOrWhiteSpace(configuredPathBase))
        {
            return PathString.Empty;
        }

        var value = configuredPathBase.Trim();

        if (
            value == "/" ||
            !value.StartsWith("/", StringComparison.Ordinal) ||
            value.EndsWith("/", StringComparison.Ordinal) ||
            value.Contains('?') ||
            value.Contains('#'))
        {
            throw new InvalidOperationException(
                "Hosting:PathBase musí být neprázdná cesta " +
                "začínající lomítkem a bez koncového lomítka, " +
                "například '/fuapay'.");
        }

        return new PathString(value);
    }

    private static IReadOnlyList<IPAddress> ResolveKnownProxies(
        IReadOnlyCollection<string> configuredProxies)
    {
        var result = new List<IPAddress>();

        foreach (var configuredProxy in configuredProxies)
        {
            if (
                string.IsNullOrWhiteSpace(configuredProxy) ||
                !IPAddress.TryParse(
                    configuredProxy.Trim(),
                    out var address) ||
                address.Equals(IPAddress.Any) ||
                address.Equals(IPAddress.IPv6Any))
            {
                throw new InvalidOperationException(
                    "Každá hodnota Hosting:KnownProxies musí být " +
                    "konkrétní IP adresa. Zástupné adresy " +
                    "0.0.0.0 a :: nejsou povoleny.");
            }

            if (!result.Contains(address))
            {
                result.Add(address);
            }
        }

        return result;
    }

    private static string? ResolveKeyRingPath(
        string? configuredPath)
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            return null;
        }

        var value = configuredPath.Trim();

        if (!Path.IsPathRooted(value))
        {
            throw new InvalidOperationException(
                "DataProtection:KeyRingPath musí být absolutní cesta.");
        }

        return value;
    }

    private static void ValidateProductionAllowedHosts(
        string? configuredAllowedHosts)
    {
        var allowedHosts =
            configuredAllowedHosts?
                .Split(
                    ';',
                    StringSplitOptions.RemoveEmptyEntries |
                    StringSplitOptions.TrimEntries) ?? [];

        if (
            allowedHosts.Length == 0 ||
            allowedHosts.Any(
                host =>
                    host is "*" or "+"))
        {
            throw new InvalidOperationException(
                "Produkční AllowedHosts musí obsahovat konkrétní " +
                "veřejný hostname a nesmí používat globální " +
                "zástupnou hodnotu '*' ani '+'.");
        }
    }
}
