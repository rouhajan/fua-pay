using FuaPay.Web.Hosting;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace FuaPay.Web.Tests.Hosting;

public sealed class RuntimeFeatureSelectionTests
{
    [Fact]
    public void Resolve_Development_PreservesDevelopmentFeatures()
    {
        var configuration = CreateConfiguration(
            new Dictionary<string, string?>
            {
                ["DevelopmentSignIn:Enabled"] = "true",
                ["DevelopmentData:Enabled"] = "true",
                ["DevelopmentData:ResetOnStart"] = "true"
            });

        var result = RuntimeFeatureSelection.Resolve(
            Environments.Development,
            configuration);

        Assert.False(result.IsStagingTestMode);
        Assert.True(result.InteractiveTestSignInEnabled);
        Assert.True(result.TestDataEnabled);
        Assert.True(result.ResetTestDataOnStart);
        Assert.True(result.SimulatedPaymentsEnabled);
    }

    [Fact]
    public void Resolve_StagingWithExplicitFlags_EnablesTestMode()
    {
        var configuration = CreateConfiguration(
            new Dictionary<string, string?>
            {
                ["StagingTestMode:Enabled"] = "true",
                ["StagingTestMode:InteractiveSignInEnabled"] =
                    "true",
                ["StagingTestMode:SeedDataEnabled"] = "true",
                ["StagingTestMode:ResetDataOnStart"] = "true",
                ["StagingTestMode:SimulatedPaymentsEnabled"] =
                    "true"
            });

        var result = RuntimeFeatureSelection.Resolve(
            Environments.Staging,
            configuration);

        Assert.True(result.IsStagingTestMode);
        Assert.True(result.InteractiveTestSignInEnabled);
        Assert.True(result.TestDataEnabled);
        Assert.True(result.ResetTestDataOnStart);
        Assert.True(result.SimulatedPaymentsEnabled);
    }

    [Fact]
    public void Resolve_StagingWithoutMasterFlag_DisablesAllTestFeatures()
    {
        var configuration = CreateConfiguration(
            new Dictionary<string, string?>
            {
                ["StagingTestMode:InteractiveSignInEnabled"] =
                    "true",
                ["StagingTestMode:SeedDataEnabled"] = "true",
                ["StagingTestMode:ResetDataOnStart"] = "true",
                ["StagingTestMode:SimulatedPaymentsEnabled"] =
                    "true"
            });

        var result = RuntimeFeatureSelection.Resolve(
            Environments.Staging,
            configuration);

        Assert.False(result.IsStagingTestMode);
        Assert.False(result.InteractiveTestSignInEnabled);
        Assert.False(result.TestDataEnabled);
        Assert.False(result.ResetTestDataOnStart);
        Assert.False(result.SimulatedPaymentsEnabled);
    }

    [Fact]
    public void Resolve_Production_IgnoresStagingAndDevelopmentFlags()
    {
        var configuration = CreateConfiguration(
            new Dictionary<string, string?>
            {
                ["DevelopmentSignIn:Enabled"] = "true",
                ["DevelopmentData:Enabled"] = "true",
                ["StagingTestMode:Enabled"] = "true",
                ["StagingTestMode:InteractiveSignInEnabled"] =
                    "true",
                ["StagingTestMode:SeedDataEnabled"] = "true",
                ["StagingTestMode:SimulatedPaymentsEnabled"] =
                    "true"
            });

        var result = RuntimeFeatureSelection.Resolve(
            Environments.Production,
            configuration);

        Assert.False(result.IsStagingTestMode);
        Assert.False(result.InteractiveTestSignInEnabled);
        Assert.False(result.TestDataEnabled);
        Assert.False(result.ResetTestDataOnStart);
        Assert.False(result.SimulatedPaymentsEnabled);
    }

    private static IConfiguration CreateConfiguration(
        IReadOnlyDictionary<string, string?> values)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }
}
