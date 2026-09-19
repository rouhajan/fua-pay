using FuaPay.Web.Hosting;
using FuaPay.Web.Modules.Payments.Domain;

using Microsoft.Extensions.Configuration;

namespace FuaPay.Web.Tests.Hosting;

public sealed class PaymentProviderSelectionTests
{
    [Fact]
    public void Resolve_ExplicitNoneWithDisabledCsob_DisablesCardPayments()
    {
        var result = PaymentProviderSelection.Resolve(
            "Production",
            Configuration("None"),
            RuntimeFeatures(simulatedPaymentsEnabled: false),
            csobGatewayEnabled: false);

        Assert.Null(result.Provider);
        Assert.False(result.DevelopmentPaymentUiEnabled);
        Assert.DoesNotContain("None", Enum.GetNames<PaymentProvider>());
        Assert.DoesNotContain("Disabled", Enum.GetNames<PaymentProvider>());
    }

    [Fact]
    public void Resolve_ExplicitDevelopmentConfiguration_SelectsDevelopment()
    {
        var result = PaymentProviderSelection.Resolve(
            "Development",
            Configuration("Development"),
            RuntimeFeatures(simulatedPaymentsEnabled: true),
            csobGatewayEnabled: false);

        Assert.Equal(PaymentProvider.Development, result.Provider);
        Assert.True(result.DevelopmentPaymentUiEnabled);
    }

    [Fact]
    public void Resolve_ExplicitCsobConfiguration_SelectsCsobWithoutDevelopmentUi()
    {
        var result = PaymentProviderSelection.Resolve(
            "Development",
            Configuration("Csob"),
            RuntimeFeatures(simulatedPaymentsEnabled: true),
            csobGatewayEnabled: true);

        Assert.Equal(PaymentProvider.Csob, result.Provider);
        Assert.False(result.DevelopmentPaymentUiEnabled);
    }

    [Theory]
    [InlineData(null, "Production", false, false)]
    [InlineData("", "Production", false, false)]
    [InlineData("Unknown", "Production", false, false)]
    [InlineData("None", "Production", false, true)]
    [InlineData("Development", "Production", true, false)]
    [InlineData("Development", "Development", false, false)]
    [InlineData("Development", "Development", true, true)]
    [InlineData("Csob", "Production", false, false)]
    public void Resolve_InvalidOrConflictingConfigurationFailsAtStartup(
        string? provider,
        string environmentName,
        bool simulatedPaymentsEnabled,
        bool csobGatewayEnabled)
    {
        Assert.Throws<InvalidOperationException>(
            () => PaymentProviderSelection.Resolve(
                environmentName,
                Configuration(provider),
                RuntimeFeatures(simulatedPaymentsEnabled),
                csobGatewayEnabled));
    }

    private static IConfiguration Configuration(string? provider) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Payments:Provider"] = provider
                })
            .Build();

    private static RuntimeFeatureSelection RuntimeFeatures(
        bool simulatedPaymentsEnabled) =>
        new(
            IsStagingTestMode: false,
            InteractiveTestSignInEnabled: false,
            TestDataEnabled: false,
            ResetTestDataOnStart: false,
            SimulatedPaymentsEnabled: simulatedPaymentsEnabled);
}
