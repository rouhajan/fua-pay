using FuaPay.Web.Modules.Payments.Infrastructure.Csob;
using FuaPay.Web.Tests.Testing;

using Microsoft.Extensions.Configuration;

namespace FuaPay.Web.Tests.Modules.Payments.Infrastructure.Csob;

public sealed class CsobGatewayConfigurationTests
{
    [Fact]
    public void Resolve_DisabledConfigurationDoesNotRequireKeys()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Csob:Enabled"] = "false"
                })
            .Build();

        var result = CsobGatewayConfiguration.Resolve(
            configuration,
            "Production");

        Assert.False(result.Enabled);
        Assert.Equal(
            CsobGatewayConfiguration.ProductionApiBaseUri,
            result.ApiBaseUri);
    }

    [Fact]
    public void Resolve_EnabledConfigurationRejectsProductionApiOutsideProduction()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Csob:Enabled"] = "true",
                    ["Csob:ApiBaseUrl"] =
                        "https://api.platebnibrana.csob.cz/"
                })
            .Build();

        var exception = Assert.Throws<InvalidOperationException>(
            () => CsobGatewayConfiguration.Resolve(
                configuration,
                "Staging"));

        Assert.Contains(
            "Neprodukční",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Resolve_EnabledIntegrationApiRejectsProductionEnvironment()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Csob:Enabled"] = "true",
                    ["Csob:ApiBaseUrl"] =
                        "https://iapi.iplatebnibrana.csob.cz/"
                })
            .Build();

        var exception = Assert.Throws<InvalidOperationException>(
            () => CsobGatewayConfiguration.Resolve(
                configuration,
                "Production"));

        Assert.Contains(
            "produkční",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Resolve_EnabledProductionConfigurationUsesProductionApi()
    {
        using var keys = new TemporaryDirectory("fuapay-csob-keys");
        var privateKeyPath = Path.Combine(keys.Path, "merchant.key");
        var publicKeyPath = Path.Combine(keys.Path, "gateway.pub");
        File.WriteAllText(privateKeyPath, "test-only");
        File.WriteAllText(publicKeyPath, "test-only");
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Csob:Enabled"] = "true",
                    ["Csob:MerchantId"] = "M123456789",
                    ["Csob:PrivateKeyPath"] = privateKeyPath,
                    ["Csob:GatewayPublicKeyPath"] = publicKeyPath,
                    ["Csob:ReturnUrl"] =
                        "https://fuapay.tul.cz/payments/csob/return"
                })
            .Build();

        var result = CsobGatewayConfiguration.Resolve(
            configuration,
            "Production");

        Assert.True(result.Enabled);
        Assert.Equal(
            CsobGatewayConfiguration.ProductionApiBaseUri,
            result.ApiBaseUri);
    }

    [Fact]
    public void Resolve_EnabledConfigurationRejectsMerchantIdLongerThanTenCharacters()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Csob:Enabled"] = "true",
                    ["Csob:MerchantId"] = "M1MIPS00000"
                })
            .Build();

        var exception = Assert.Throws<InvalidOperationException>(
            () => CsobGatewayConfiguration.Resolve(
                configuration,
                "Staging"));

        Assert.Contains(
            "1 až 10",
            exception.Message,
            StringComparison.Ordinal);
    }
}
