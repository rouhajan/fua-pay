using Microsoft.Extensions.Hosting;

namespace FuaPay.Web.Hosting;

public sealed record RuntimeFeatureSelection(
    bool IsStagingTestMode,
    bool InteractiveTestSignInEnabled,
    bool TestDataEnabled,
    bool ResetTestDataOnStart,
    bool SimulatedPaymentsEnabled)
{
    public static RuntimeFeatureSelection Resolve(
        string environmentName,
        IConfiguration configuration)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            environmentName);
        ArgumentNullException.ThrowIfNull(configuration);

        var isDevelopment =
            Environments.Development.Equals(
                environmentName,
                StringComparison.OrdinalIgnoreCase);

        if (isDevelopment)
        {
            var signInEnabled =
                configuration.GetValue<bool>(
                    "DevelopmentSignIn:Enabled");
            var dataEnabled =
                signInEnabled &&
                configuration.GetValue<bool>(
                    "DevelopmentData:Enabled");

            return new RuntimeFeatureSelection(
                IsStagingTestMode: false,
                InteractiveTestSignInEnabled: signInEnabled,
                TestDataEnabled: dataEnabled,
                ResetTestDataOnStart:
                    dataEnabled &&
                    configuration.GetValue<bool>(
                        "DevelopmentData:ResetOnStart"),
                SimulatedPaymentsEnabled: true);
        }

        var isStaging =
            Environments.Staging.Equals(
                environmentName,
                StringComparison.OrdinalIgnoreCase);
        var stagingTestModeEnabled =
            isStaging &&
            configuration.GetValue<bool>(
                "StagingTestMode:Enabled");

        if (!stagingTestModeEnabled)
        {
            return new RuntimeFeatureSelection(
                IsStagingTestMode: false,
                InteractiveTestSignInEnabled: false,
                TestDataEnabled: false,
                ResetTestDataOnStart: false,
                SimulatedPaymentsEnabled: false);
        }

        var stagingSignInEnabled =
            configuration.GetValue<bool>(
                "StagingTestMode:InteractiveSignInEnabled");
        var stagingDataEnabled =
            stagingSignInEnabled &&
            configuration.GetValue<bool>(
                "StagingTestMode:SeedDataEnabled");

        return new RuntimeFeatureSelection(
            IsStagingTestMode: true,
            InteractiveTestSignInEnabled:
                stagingSignInEnabled,
            TestDataEnabled: stagingDataEnabled,
            ResetTestDataOnStart:
                stagingDataEnabled &&
                configuration.GetValue<bool>(
                    "StagingTestMode:ResetDataOnStart"),
            SimulatedPaymentsEnabled:
                configuration.GetValue<bool>(
                    "StagingTestMode:SimulatedPaymentsEnabled"));
    }
}
