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
    public void Resolve_ThirtyMinuteTtl_ExtendsDefaultRecoveryHorizon()
    {
        var configuration = new ConfigurationBuilder().Build();
        var gateway = CreateGateway(enabled: true, paymentTtlSeconds: 1800);

        var resolved = CsobReconciliationConfiguration.Resolve(
            configuration,
            gateway);

        Assert.True(resolved.Enabled);
        Assert.Equal(14, resolved.MaximumAttempts);
        resolved.Validate(gateway);
    }

    [Theory]
    [InlineData(900, 8)]
    [InlineData(1800, 13)]
    public void Validate_RecoveryHorizonShorterThanTtlAndMargin_IsRejected(
        int paymentTtlSeconds,
        int maximumAttempts)
    {
        var gateway = CreateGateway(
            enabled: true,
            paymentTtlSeconds: paymentTtlSeconds);
        var reconciliation = CreateReconciliation(maximumAttempts);

        var exception = Assert.Throws<InvalidOperationException>(
            () => reconciliation.Validate(gateway));

        Assert.Contains(
            "PaymentTtlSeconds",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(900, 9)]
    [InlineData(1800, 14)]
    public void Validate_RecoveryHorizonBoundary_CoversTtlAndMargin(
        int paymentTtlSeconds,
        int maximumAttempts)
    {
        var gateway = CreateGateway(
            enabled: true,
            paymentTtlSeconds: paymentTtlSeconds);
        var reconciliation = CreateReconciliation(maximumAttempts);

        reconciliation.Validate(gateway);
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

    private static CsobReconciliationConfiguration CreateReconciliation(
        int maximumAttempts) =>
        new(
            Enabled: true,
            PollInterval: TimeSpan.FromSeconds(15),
            PendingMinimumAge: TimeSpan.FromSeconds(15),
            LeaseDuration: TimeSpan.FromMinutes(3),
            BaseBackoff: TimeSpan.FromSeconds(15),
            MaximumBackoff: TimeSpan.FromMinutes(3),
            MaximumAttempts: maximumAttempts,
            BatchSize: 20);

    private static CsobGatewayConfiguration CreateGateway(
        bool enabled,
        int paymentTtlSeconds = 900)
    {
        return new CsobGatewayConfiguration(
            enabled,
            CsobGatewayConfiguration.SandboxApiBaseUri,
            enabled ? "M1MIPS0000" : string.Empty,
            enabled ? "unused-private-key" : string.Empty,
            enabled ? "unused-public-key" : string.Empty,
            new Uri("https://localhost/payments/csob/return"),
            paymentTtlSeconds,
            TimeSpan.FromSeconds(30));
    }
}
