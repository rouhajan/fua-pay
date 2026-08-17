using FuaPay.Web.Modules.Payments.Infrastructure.Csob;

using Microsoft.Extensions.Configuration;

namespace FuaPay.Web.Tests.Modules.Payments.Infrastructure.Csob;

public sealed class CsobReconciliationConfigurationTests
{
    [Fact]
    public void Resolve_DisabledGateway_DisablesReconciliation()
    {
        var configuration = new ConfigurationBuilder().Build();
        var gateway = CreateGateway(enabled: false);

        var resolved = CsobReconciliationConfiguration.Resolve(
            configuration,
            gateway);

        Assert.False(resolved.Enabled);
    }

    [Fact]
    public void Resolve_EnabledGateway_UsesSafeDefaults()
    {
        var configuration = new ConfigurationBuilder().Build();
        var gateway = CreateGateway(enabled: true);

        var resolved = CsobReconciliationConfiguration.Resolve(
            configuration,
            gateway);

        Assert.True(resolved.Enabled);
        Assert.Equal(TimeSpan.FromSeconds(15), resolved.PollInterval);
        Assert.Equal(TimeSpan.FromMinutes(1), resolved.InProgressMaximumAge);
        Assert.Equal(TimeSpan.FromMinutes(3), resolved.LeaseDuration);
        Assert.Equal(12, resolved.MaximumAttempts);
        Assert.Equal(20, resolved.BatchSize);
    }

    [Fact]
    public void Validate_InProgressAgeMustOutliveGatewayRequest()
    {
        var gateway = CreateGateway(enabled: true);
        var reconciliation = new CsobReconciliationConfiguration(
            Enabled: true,
            PollInterval: TimeSpan.FromSeconds(15),
            PendingMinimumAge: TimeSpan.FromSeconds(15),
            LeaseDuration: TimeSpan.FromMinutes(3),
            BaseBackoff: TimeSpan.FromSeconds(15),
            MaximumBackoff: TimeSpan.FromMinutes(3),
            MaximumAttempts: 12,
            BatchSize: 20)
        {
            InProgressMaximumAge = TimeSpan.FromSeconds(45)
        };

        Assert.Throws<InvalidOperationException>(
            () => reconciliation.Validate(gateway));
    }

    [Fact]
    public void Validate_LeaseMustExceedGatewayTimeout()
    {
        var gateway = CreateGateway(enabled: true);
        var reconciliation = new CsobReconciliationConfiguration(
            Enabled: true,
            PollInterval: TimeSpan.FromSeconds(15),
            PendingMinimumAge: TimeSpan.FromSeconds(15),
            LeaseDuration: TimeSpan.FromSeconds(45),
            BaseBackoff: TimeSpan.FromSeconds(15),
            MaximumBackoff: TimeSpan.FromMinutes(3),
            MaximumAttempts: 12,
            BatchSize: 20);

        Assert.Throws<InvalidOperationException>(
            () => reconciliation.Validate(gateway));
    }

    private static CsobGatewayConfiguration CreateGateway(bool enabled)
    {
        return new CsobGatewayConfiguration(
            enabled,
            CsobGatewayConfiguration.SandboxApiBaseUri,
            enabled ? "M1MIPS0000" : string.Empty,
            enabled ? "unused-private-key" : string.Empty,
            enabled ? "unused-public-key" : string.Empty,
            new Uri("https://localhost/payments/csob/return"),
            900,
            TimeSpan.FromSeconds(30));
    }
}
