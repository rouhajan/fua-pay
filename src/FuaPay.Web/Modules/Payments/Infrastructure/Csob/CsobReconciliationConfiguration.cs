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

    private const int BaselineMaximumAttempts = 12;

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
                "Csob:Reconciliation:MaximumAttempts") ??
                ResolveDefaultMaximumAttempts(
                    TimeSpan.FromSeconds(
                        configuration.GetValue<int?>(
                            "Csob:Reconciliation:PendingMinimumAgeSeconds")
                        ?? 15),
                    TimeSpan.FromSeconds(
                        configuration.GetValue<int?>(
                            "Csob:Reconciliation:BaseBackoffSeconds")
                        ?? 15),
                    TimeSpan.FromSeconds(
                        configuration.GetValue<int?>(
                            "Csob:Reconciliation:MaximumBackoffSeconds")
                        ?? 180),
                    TimeSpan.FromSeconds(
                        configuration.GetValue<int?>(
                            "Csob:Reconciliation:PollIntervalSeconds")
                        ?? 15),
                    gatewayConfiguration.PaymentTtlSeconds),
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

        var requiredHorizon =
            TimeSpan.FromSeconds(gatewayConfiguration.PaymentTtlSeconds) +
            ProviderCompletionSafetyMargin +
            PollInterval;

        if (CalculateExhaustionHorizon() < requiredHorizon)
        {
            throw new InvalidOperationException(
                "Csob:Reconciliation:MaximumAttempts musí pokrýt " +
                "Csob:PaymentTtlSeconds, bezpečnostní rezervu poskytovatele " +
                "a jeden interval recovery workeru.");
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

    internal TimeSpan CalculateBackoff(int attemptNumber) =>
        CalculateBackoff(BaseBackoff, MaximumBackoff, attemptNumber);

    private TimeSpan CalculateExhaustionHorizon()
    {
        var horizon = PendingMinimumAge;

        for (var attemptNumber = 1;
             attemptNumber < MaximumAttempts;
             attemptNumber++)
        {
            horizon += CalculateBackoff(attemptNumber);
        }

        return horizon;
    }

    private static int ResolveDefaultMaximumAttempts(
        TimeSpan pendingMinimumAge,
        TimeSpan baseBackoff,
        TimeSpan maximumBackoff,
        TimeSpan pollInterval,
        int paymentTtlSeconds)
    {
        var requiredHorizon =
            TimeSpan.FromSeconds(paymentTtlSeconds) +
            ProviderCompletionSafetyMargin +
            pollInterval;
        var horizon = pendingMinimumAge;
        var maximumAttempts = 1;

        while (
            horizon < requiredHorizon &&
            maximumAttempts < 100)
        {
            horizon += CalculateBackoff(
                baseBackoff,
                maximumBackoff,
                maximumAttempts);
            maximumAttempts++;
        }

        return Math.Max(BaselineMaximumAttempts, maximumAttempts);
    }

    private static TimeSpan CalculateBackoff(
        TimeSpan baseBackoff,
        TimeSpan maximumBackoff,
        int attemptNumber)
    {
        if (attemptNumber < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(attemptNumber));
        }

        var delay = baseBackoff;

        for (var index = 1; index < attemptNumber; index++)
        {
            if (delay >= maximumBackoff)
            {
                return maximumBackoff;
            }

            var doubledTicks = delay.Ticks > long.MaxValue / 2
                ? long.MaxValue
                : delay.Ticks * 2;
            delay = TimeSpan.FromTicks(
                Math.Min(doubledTicks, maximumBackoff.Ticks));
        }

        return delay;
    }
}
