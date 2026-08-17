using FuaPay.Web.Modules.Payments.Infrastructure.Csob;

namespace FuaPay.CsobSandboxTests;

public sealed class CsobSandboxEchoTests
{
    [Fact]
    public async Task EchoAsync_ValidatesMerchantAndBothKeys()
    {
        var configuration = CreateConfiguration();
        using var signature = new CsobGatewaySignature(configuration);
        using var httpClient = new HttpClient
        {
            BaseAddress = configuration.ApiBaseUri,
            Timeout = configuration.RequestTimeout
        };
        var client = new CsobGatewayClient(
            httpClient,
            configuration,
            new CsobGatewayAvailability(true),
            signature,
            TimeProvider.System);

        var result = await client.EchoAsync();

        Assert.Equal(0, result.ResultCode);
        Assert.Equal("OK", result.ResultMessage);
    }

    private static CsobGatewayConfiguration CreateConfiguration()
    {
        if (!string.Equals(
            Environment.GetEnvironmentVariable(
                "FUA_PAY_CSOB_SANDBOX_TESTS_ALLOWED"),
            "1",
            StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Živé ČSOB sandbox testy vyžadují FUA_PAY_CSOB_SANDBOX_TESTS_ALLOWED=1.");
        }

        var merchantId = RequireEnvironmentValue(
            "Csob__MerchantId");
        var privateKeyPath = RequireEnvironmentValue(
            "Csob__PrivateKeyPath");
        var publicKeyPath = RequireEnvironmentValue(
            "Csob__GatewayPublicKeyPath");

        var configuration = new CsobGatewayConfiguration(
            true,
            CsobGatewayConfiguration.SandboxApiBaseUri,
            merchantId,
            privateKeyPath,
            publicKeyPath,
            new Uri("https://sandbox-callback.invalid/payments/csob/return"),
            900,
            TimeSpan.FromSeconds(30));
        configuration.Validate("Staging");
        return configuration;
    }

    private static string RequireEnvironmentValue(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);

        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"Pro živý ČSOB sandbox test chybí proměnná {name}.");
        }

        return value.Trim();
    }
}
