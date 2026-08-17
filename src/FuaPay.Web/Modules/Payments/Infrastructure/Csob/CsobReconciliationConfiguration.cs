namespace FuaPay.Web.Modules.Payments.Infrastructure.Csob;

public sealed record CsobReconciliationConfiguration(
    bool Enabled,
    TimeSpan PollInterval,
    TimeSpan PendingMinimumAge,
    TimeSpan LeaseDuration,
    TimeSpan BaseBackoff,
    TimeSpan MaximumBackoff,
    int MaximumAttempts,
    int BatchSize)
{
    private static readonly TimeSpan ProviderCompletionSafetyMargin =
        TimeSpan.FromSeconds(30);

    public TimeSpan InProgressMaximumAge { get; init; } =
        TimeSpan.FromMinutes(1);

    public static CsobReconciliationConfiguration Resolve(
        IConfiguration configuration,
        CsobGatewayConfiguration gatewayConfiguration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(gatewayConfiguration);

        var enabled = gatewayConfiguration.Enabled;
        var resolved = new CsobReconciliationConfiguration(
            enabled,
            TimeSpan.FromSeconds(
                configuration.GetValue<int?>(
                    "Csob:Reconciliation:PollIntervalSeconds") ?? 15),
            TimeSpan.FromSeconds(
                configuration.GetValue<int?>(
                    "Csob:Reconciliation:PendingMinimumAgeSeconds") ?? 15),
            TimeSpan.FromSeconds(
                configuration.GetValue<int?>(
                    "Csob:Reconciliation:LeaseSeconds") ?? 180),
            TimeSpan.FromSeconds(
                configuration.GetValue<int?>(
                    "Csob:Reconciliation:BaseBackoffSeconds") ?? 15),
            TimeSpan.FromSeconds(
                configuration.GetValue<int?>(
                    "Csob:Reconciliation:MaximumBackoffSeconds") ?? 180),
            configuration.GetValue<int?>(
                "Csob:Reconciliation:MaximumAttempts") ?? 12,
            configuration.GetValue<int?>(
                "Csob:Reconciliation:BatchSize") ?? 20)
        {
            InProgressMaximumAge = TimeSpan.FromSeconds(
                configuration.GetValue<int?>(
                    "Csob:Reconciliation:InProgressMaximumAgeSeconds")
                ?? (int)(gatewayConfiguration.RequestTimeout +
                    ProviderCompletionSafetyMargin).TotalSeconds)
        };

        resolved.Validate(gatewayConfiguration);
        return resolved;
    }

    public void Validate(CsobGatewayConfiguration gatewayConfiguration)
    {
        ArgumentNullException.ThrowIfNull(gatewayConfiguration);

        if (!Enabled)
        {
            return;
        }

        if (!gatewayConfiguration.Enabled)
        {
            throw new InvalidOperationException(
                "ČSOB reconciliation nelze zapnout bez aktivní ČSOB brány.");
        }

        ValidateRange(
            PollInterval,
            TimeSpan.FromSeconds(5),
            TimeSpan.FromMinutes(5),
            "Csob:Reconciliation:PollIntervalSeconds");
        ValidateRange(
            PendingMinimumAge,
            TimeSpan.Zero,
            TimeSpan.FromMinutes(30),
            "Csob:Reconciliation:PendingMinimumAgeSeconds");
        ValidateRange(
            InProgressMaximumAge,
            gatewayConfiguration.RequestTimeout +
                ProviderCompletionSafetyMargin,
            TimeSpan.FromMinutes(30),
            "Csob:Reconciliation:InProgressMaximumAgeSeconds");
        ValidateRange(
            BaseBackoff,
            TimeSpan.FromSeconds(5),
            TimeSpan.FromMinutes(5),
            "Csob:Reconciliation:BaseBackoffSeconds");
        ValidateRange(
            MaximumBackoff,
            BaseBackoff,
            TimeSpan.FromMinutes(30),
            "Csob:Reconciliation:MaximumBackoffSeconds");

        if (LeaseDuration < gatewayConfiguration.RequestTimeout + TimeSpan.FromSeconds(30))
        {
            throw new InvalidOperationException(
                "Csob:Reconciliation:LeaseSeconds musí být nejméně o 30 sekund delší než timeout ČSOB požadavku.");
        }

        if (LeaseDuration > TimeSpan.FromMinutes(10))
        {
            throw new InvalidOperationException(
                "Csob:Reconciliation:LeaseSeconds smí být nejvýše 600 sekund.");
        }

        if (MaximumAttempts is < 1 or > 100)
        {
            throw new InvalidOperationException(
                "Csob:Reconciliation:MaximumAttempts musí být v rozsahu 1 až 100.");
        }

        if (BatchSize is < 1 or > 100)
        {
            throw new InvalidOperationException(
                "Csob:Reconciliation:BatchSize musí být v rozsahu 1 až 100.");
        }
    }

    private static void ValidateRange(
        TimeSpan value,
        TimeSpan minimum,
        TimeSpan maximum,
        string key)
    {
        if (value < minimum || value > maximum)
        {
            throw new InvalidOperationException(
                $"{key} je mimo povolený rozsah.");
        }
    }
}
