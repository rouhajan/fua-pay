namespace FuaPay.Web.Modules.Payments.Infrastructure.Csob;

public sealed class CsobPaymentReconciliationHealth
{
    private readonly object _gate = new();

    private DateTimeOffset? _lastSuccessfulCycleAt;
    private DateTimeOffset? _lastFailedCycleAt;
    private string? _lastErrorType;
    private bool? _lastCycleSucceeded;

    public void RecordSuccessfulCycle(DateTimeOffset completedAt)
    {
        ValidateTimestamp(completedAt, nameof(completedAt));

        lock (_gate)
        {
            _lastSuccessfulCycleAt = completedAt;
            _lastCycleSucceeded = true;
        }
    }

    public void RecordFailedCycle(
        DateTimeOffset failedAt,
        Exception exception)
    {
        ValidateTimestamp(failedAt, nameof(failedAt));
        ArgumentNullException.ThrowIfNull(exception);

        lock (_gate)
        {
            _lastFailedCycleAt = failedAt;
            _lastErrorType = exception.GetType().Name;
            _lastCycleSucceeded = false;
        }
    }

    public CsobPaymentReconciliationHealthSnapshot GetSnapshot(
        DateTimeOffset observedAt,
        bool enabled,
        TimeSpan pollInterval)
    {
        ValidateTimestamp(observedAt, nameof(observedAt));

        if (pollInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(pollInterval));
        }

        var staleAfter = CalculateStaleAfter(pollInterval);

        lock (_gate)
        {
            var status = DetermineStatus(
                observedAt,
                enabled,
                staleAfter,
                _lastSuccessfulCycleAt,
                _lastCycleSucceeded);

            return new CsobPaymentReconciliationHealthSnapshot(
                status,
                _lastSuccessfulCycleAt,
                _lastFailedCycleAt,
                status == CsobPaymentReconciliationHealthStatus.Failed
                    ? _lastErrorType
                    : null,
                staleAfter);
        }
    }

    private static CsobPaymentReconciliationHealthStatus DetermineStatus(
        DateTimeOffset observedAt,
        bool enabled,
        TimeSpan staleAfter,
        DateTimeOffset? lastSuccessfulCycleAt,
        bool? lastCycleSucceeded)
    {
        if (!enabled)
        {
            return CsobPaymentReconciliationHealthStatus.Disabled;
        }

        if (lastCycleSucceeded == false)
        {
            return CsobPaymentReconciliationHealthStatus.Failed;
        }

        if (lastCycleSucceeded is null || !lastSuccessfulCycleAt.HasValue)
        {
            return CsobPaymentReconciliationHealthStatus.NotStarted;
        }

        if (observedAt - lastSuccessfulCycleAt.Value > staleAfter)
        {
            return CsobPaymentReconciliationHealthStatus.Stale;
        }

        return CsobPaymentReconciliationHealthStatus.Healthy;
    }

    private static TimeSpan CalculateStaleAfter(TimeSpan pollInterval)
    {
        var ticks = pollInterval.Ticks > long.MaxValue / 3
            ? long.MaxValue
            : pollInterval.Ticks * 3;

        return TimeSpan.FromTicks(ticks);
    }

    private static void ValidateTimestamp(
        DateTimeOffset value,
        string parameterName)
    {
        if (value == default)
        {
            throw new ArgumentException(
                "Čas nesmí být prázdný.",
                parameterName);
        }
    }
}

public sealed record CsobPaymentReconciliationHealthSnapshot(
    CsobPaymentReconciliationHealthStatus Status,
    DateTimeOffset? LastSuccessfulCycleAt,
    DateTimeOffset? LastFailedCycleAt,
    string? LastErrorType,
    TimeSpan StaleAfter);

public enum CsobPaymentReconciliationHealthStatus
{
    Disabled,
    NotStarted,
    Healthy,
    Failed,
    Stale
}
