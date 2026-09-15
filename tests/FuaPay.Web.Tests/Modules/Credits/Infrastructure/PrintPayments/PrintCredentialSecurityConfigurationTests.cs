using FuaPay.Web.Modules.Credits.Infrastructure.PrintPayments;

using Microsoft.Extensions.Configuration;

namespace FuaPay.Web.Tests.Modules.Credits.Infrastructure.PrintPayments;

public sealed class PrintCredentialSecurityConfigurationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Resolve_WhenCredentialFeatureIsDisabledDoesNotRequirePepper(
        bool printPaymentsEnabled)
    {
        var resolved = PrintCredentialSecurityConfiguration.Resolve(
            Configuration(enabled: false, pepper: "not-base64"),
            printPaymentsEnabled);

        Assert.False(resolved.Enabled);
        Assert.Empty(resolved.Pepper);
    }

    [Fact]
    public void Resolve_WhenPrintCredentialsEnabledRequiresPrintPayments()
    {
        Assert.Throws<InvalidOperationException>(
            () => PrintCredentialSecurityConfiguration.Resolve(
                Configuration(enabled: true, ValidPepper()),
                printPaymentsEnabled: false));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-base64")]
    [InlineData("AQID")]
    public void Resolve_WhenEnabledRejectsMissingOrInvalidPepper(string? value)
    {
        Assert.Throws<InvalidOperationException>(
            () => PrintCredentialSecurityConfiguration.Resolve(
                Configuration(enabled: true, value),
                printPaymentsEnabled: true));
    }

    [Fact]
    public void Resolve_WhenBothFeaturesEnabledAcceptsValidPepper()
    {
        var resolved = PrintCredentialSecurityConfiguration.Resolve(
            Configuration(enabled: true, ValidPepper()),
            printPaymentsEnabled: true);

        Assert.True(resolved.Enabled);
        Assert.Equal(32, resolved.Pepper.Length);
    }

    private static IConfiguration Configuration(
        bool enabled,
        string? pepper)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PrintCredentials:Enabled"] = enabled.ToString(),
                ["PrintCredentials:PepperBase64"] = pepper
            })
            .Build();
    }

    private static string ValidPepper() =>
        Convert.ToBase64String(new byte[32]);
}
