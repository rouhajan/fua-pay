using System.Net;

using FuaPay.Web.Hosting;
using FuaPay.Web.Tests.Testing;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace FuaPay.Web.Tests.Hosting;

public sealed class FuaPayHostingConfigurationTests
{
    [Fact]
    public void Resolve_EmptyConfiguration_UsesSafeDefaults()
    {
        var result = FuaPayHostingConfiguration.Resolve(
            CreateConfiguration(
                new Dictionary<string, string?>()));

        Assert.False(result.PathBase.HasValue);
        Assert.False(result.ForwardedHeadersEnabled);
        Assert.Empty(result.KnownProxies);
        Assert.Null(result.DataProtectionKeyRingPath);
    }

    [Fact]
    public void Resolve_ValidStagingConfiguration_ReturnsValues()
    {
        var result = FuaPayHostingConfiguration.Resolve(
            CreateConfiguration(
                new Dictionary<string, string?>
                {
                    ["Hosting:PathBase"] = "/fuapay",
                    ["Hosting:UseForwardedHeaders"] = "true",
                    ["Hosting:KnownProxies:0"] = "127.0.0.1",
                    ["DataProtection:KeyRingPath"] =
                        Path.GetFullPath("key-ring")
                }));

        Assert.Equal("/fuapay", result.PathBase.Value);
        Assert.True(result.ForwardedHeadersEnabled);
        Assert.Equal(
            IPAddress.Loopback,
            Assert.Single(result.KnownProxies));
        Assert.Equal(
            Path.GetFullPath("key-ring"),
            result.DataProtectionKeyRingPath);
    }

    [Theory]
    [InlineData("fuapay")]
    [InlineData("/fuapay/")]
    [InlineData("/")]
    [InlineData("/fuapay?test=1")]
    [InlineData("/fuapay#fragment")]
    public void Resolve_InvalidPathBase_Throws(string pathBase)
    {
        var configuration = CreateConfiguration(
            new Dictionary<string, string?>
            {
                ["Hosting:PathBase"] = pathBase
            });

        Assert.Throws<InvalidOperationException>(
            () => FuaPayHostingConfiguration.Resolve(
                configuration));
    }

    [Fact]
    public void Resolve_RelativeKeyRingPath_Throws()
    {
        var configuration = CreateConfiguration(
            new Dictionary<string, string?>
            {
                ["DataProtection:KeyRingPath"] = "relative/path"
            });

        Assert.Throws<InvalidOperationException>(
            () => FuaPayHostingConfiguration.Resolve(
                configuration));
    }

    [Fact]
    public void Resolve_ForwardedHeadersWithoutKnownProxy_Throws()
    {
        var configuration = CreateConfiguration(
            new Dictionary<string, string?>
            {
                ["Hosting:UseForwardedHeaders"] = "true"
            });

        Assert.Throws<InvalidOperationException>(
            () => FuaPayHostingConfiguration.Resolve(
                configuration));
    }

    [Fact]
    public void Resolve_KnownProxyWhileForwardingDisabled_Throws()
    {
        var configuration = CreateConfiguration(
            new Dictionary<string, string?>
            {
                ["Hosting:KnownProxies:0"] = "127.0.0.1"
            });

        Assert.Throws<InvalidOperationException>(
            () => FuaPayHostingConfiguration.Resolve(
                configuration));
    }

    [Theory]
    [InlineData("not-an-ip")]
    [InlineData("0.0.0.0")]
    [InlineData("::")]
    public void Resolve_UnsafeKnownProxy_Throws(
        string proxy)
    {
        var configuration = CreateConfiguration(
            new Dictionary<string, string?>
            {
                ["Hosting:UseForwardedHeaders"] = "true",
                ["Hosting:KnownProxies:0"] = proxy
            });

        Assert.Throws<InvalidOperationException>(
            () => FuaPayHostingConfiguration.Resolve(
                configuration));
    }

    [Fact]
    public void ValidateForEnvironment_ProductionWithExplicitValues_Passes()
    {
        using var keyRing =
            new TemporaryDirectory("fua-pay-key-ring-test");
        var configuration = CreateConfiguration(
            new Dictionary<string, string?>
            {
                ["AllowedHosts"] = "fuapay.tul.cz",
                ["DataProtection:KeyRingPath"] = keyRing.Path
            });
        var result =
            FuaPayHostingConfiguration.Resolve(configuration);

        result.ValidateForEnvironment(
            Environments.Production,
            configuration);
    }

    [Fact]
    public void ValidateForEnvironment_ProductionWithStartupMigrations_Throws()
    {
        var configuration = CreateConfiguration(
            new Dictionary<string, string?>
            {
                ["Database:ApplyMigrationsOnStart"] = "true"
            });
        var result =
            FuaPayHostingConfiguration.Resolve(configuration);

        var exception = Assert.Throws<InvalidOperationException>(
            () => result.ValidateForEnvironment(
                Environments.Production,
                configuration));

        Assert.Contains(
            "Database:ApplyMigrationsOnStart",
            exception.Message,
            StringComparison.Ordinal);
        Assert.Contains(
            "samostatný řízený deployment krok",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("*")]
    [InlineData("+")]
    public void ValidateForEnvironment_ProductionWithUnsafeAllowedHosts_Throws(
        string? allowedHosts)
    {
        using var keyRing =
            new TemporaryDirectory("fua-pay-key-ring-test");
        var configuration = CreateConfiguration(
            new Dictionary<string, string?>
            {
                ["AllowedHosts"] = allowedHosts,
                ["DataProtection:KeyRingPath"] = keyRing.Path
            });
        var result =
            FuaPayHostingConfiguration.Resolve(configuration);

        Assert.Throws<InvalidOperationException>(
            () => result.ValidateForEnvironment(
                Environments.Production,
                configuration));
    }

    [Fact]
    public void ValidateForEnvironment_ProductionWithoutKeyRing_Throws()
    {
        var configuration = CreateConfiguration(
            new Dictionary<string, string?>
            {
                ["AllowedHosts"] = "fuapay.tul.cz"
            });
        var result =
            FuaPayHostingConfiguration.Resolve(configuration);

        Assert.Throws<InvalidOperationException>(
            () => result.ValidateForEnvironment(
                Environments.Production,
                configuration));
    }

    [Fact]
    public void ValidateForEnvironment_ProductionWithMissingKeyRingDirectory_Throws()
    {
        var missingPath = Path.Combine(
            Path.GetTempPath(),
            $"fua-pay-missing-{Guid.NewGuid():N}");
        var configuration = CreateConfiguration(
            new Dictionary<string, string?>
            {
                ["AllowedHosts"] = "fuapay.tul.cz",
                ["DataProtection:KeyRingPath"] = missingPath
            });
        var result =
            FuaPayHostingConfiguration.Resolve(configuration);

        Assert.Throws<InvalidOperationException>(
            () => result.ValidateForEnvironment(
                Environments.Production,
                configuration));
    }

    [Fact]
    public void ValidateForEnvironment_StagingDoesNotRequireProductionValues()
    {
        var configuration = CreateConfiguration(
            new Dictionary<string, string?>());
        var result =
            FuaPayHostingConfiguration.Resolve(configuration);

        result.ValidateForEnvironment(
            Environments.Staging,
            configuration);
    }

    private static IConfiguration CreateConfiguration(
        IReadOnlyDictionary<string, string?> values)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }
}
