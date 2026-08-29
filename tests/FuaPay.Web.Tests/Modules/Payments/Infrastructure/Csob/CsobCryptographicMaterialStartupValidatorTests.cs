using System.Security.Cryptography;

using FuaPay.Web.Modules.Payments;
using FuaPay.Web.Modules.Payments.Domain;
using FuaPay.Web.Modules.Payments.Infrastructure.Csob;
using FuaPay.Web.Tests.Testing;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace FuaPay.Web.Tests.Modules.Payments.Infrastructure.Csob;

public sealed class CsobCryptographicMaterialStartupValidatorTests
{
    private const string InvalidPem = "not-valid-test-pem-content";

    [Fact]
    public async Task ActiveCsobProvider_ValidGeneratedKeys_Starts()
    {
        using var files = new TemporaryDirectory(
            "fua-pay-csob-startup-valid");
        var paths = WriteValidKeys(files.Path);
        using var host = CreateActiveCsobHost(Configuration(paths));

        await host.StartAsync();
        await host.StopAsync();
    }

    [Fact]
    public async Task ActiveCsobProvider_InvalidPem_FailsWithoutExposingMaterial()
    {
        using var files = new TemporaryDirectory(
            "fua-pay-csob-startup-invalid-pem");
        var paths = WriteValidKeys(files.Path);
        File.WriteAllText(paths.PrivateKeyPath, InvalidPem);
        using var host = CreateActiveCsobHost(Configuration(paths));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => host.StartAsync());

        AssertSafeFailure(exception, paths);
    }

    [Fact]
    public async Task ActiveCsobProvider_PublicMerchantKey_FailsCapabilityCheck()
    {
        using var files = new TemporaryDirectory(
            "fua-pay-csob-startup-public-merchant");
        var paths = WriteValidKeys(files.Path);

        using (var merchant = RSA.Create(2048))
        {
            File.WriteAllText(
                paths.PrivateKeyPath,
                merchant.ExportSubjectPublicKeyInfoPem());
        }

        using var host = CreateActiveCsobHost(Configuration(paths));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => host.StartAsync());

        AssertSafeFailure(exception, paths);
    }

    [Fact]
    public async Task ActiveCsobProvider_UndersizedRsaKey_FailsMinimumSizeCheck()
    {
        using var files = new TemporaryDirectory(
            "fua-pay-csob-startup-small-rsa");
        var paths = WriteValidKeys(files.Path);

        using (var merchant = RSA.Create(1024))
        {
            File.WriteAllText(
                paths.PrivateKeyPath,
                merchant.ExportPkcs8PrivateKeyPem());
        }

        using var host = CreateActiveCsobHost(Configuration(paths));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => host.StartAsync());

        AssertSafeFailure(exception, paths);
    }

    [Fact]
    public async Task DevelopmentProvider_DoesNotRequireOrResolveCsobCrypto()
    {
        var configuration = new CsobGatewayConfiguration(
            Enabled: false,
            CsobGatewayConfiguration.SandboxApiBaseUri,
            string.Empty,
            "missing-private-key",
            "missing-public-key",
            new Uri("https://localhost/payments/csob/return"),
            900,
            TimeSpan.FromSeconds(30));
        using var host = new HostBuilder()
            .ConfigureServices(
                services =>
                {
                    services.AddPaymentsModule(
                        PaymentProvider.Development,
                        developmentPaymentUiEnabled: true);
                    services.AddCsobPaymentGateway(configuration);
                })
            .Build();

        await host.StartAsync();

        Assert.Null(
            host.Services.GetService<ICsobGatewaySignature>());
        Assert.DoesNotContain(
            host.Services.GetServices<IHostedService>(),
            service => service is
                CsobCryptographicMaterialStartupValidator);

        await host.StopAsync();
    }

    private static IHost CreateActiveCsobHost(
        CsobGatewayConfiguration configuration) =>
        new HostBuilder()
            .ConfigureServices(
                services => services.AddCsobPaymentGateway(
                    configuration,
                    activateProviderInitiator: true))
            .Build();

    private static CsobGatewayConfiguration Configuration(
        KeyPaths paths) =>
        new(
            Enabled: true,
            CsobGatewayConfiguration.SandboxApiBaseUri,
            "M1MIPS0000",
            paths.PrivateKeyPath,
            paths.PublicKeyPath,
            new Uri("https://shop.example.com/payments/csob/return"),
            900,
            TimeSpan.FromSeconds(30));

    private static KeyPaths WriteValidKeys(string directory)
    {
        var privateKeyPath = Path.Combine(
            directory,
            "merchant-private.pem");
        var publicKeyPath = Path.Combine(
            directory,
            "gateway-public.pem");

        using var merchant = RSA.Create(2048);
        using var gateway = RSA.Create(2048);

        File.WriteAllText(
            privateKeyPath,
            merchant.ExportPkcs8PrivateKeyPem());
        File.WriteAllText(
            publicKeyPath,
            gateway.ExportSubjectPublicKeyInfoPem());

        return new KeyPaths(privateKeyPath, publicKeyPath);
    }

    private static void AssertSafeFailure(
        InvalidOperationException exception,
        KeyPaths paths)
    {
        var rendered = exception.ToString();

        Assert.Contains(
            "ČSOB",
            exception.Message,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            InvalidPem,
            rendered,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            paths.PrivateKeyPath,
            rendered,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            paths.PublicKeyPath,
            rendered,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "BEGIN PRIVATE KEY",
            rendered,
            StringComparison.Ordinal);
    }

    private sealed record KeyPaths(
        string PrivateKeyPath,
        string PublicKeyPath);
}
