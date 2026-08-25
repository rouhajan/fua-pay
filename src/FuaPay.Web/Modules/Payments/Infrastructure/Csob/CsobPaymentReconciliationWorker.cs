using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FuaPay.Web.Modules.Payments.Infrastructure.Csob;

public sealed class CsobPaymentReconciliationWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly CsobReconciliationConfiguration _configuration;
    private readonly CsobPaymentReconciliationHealth _health;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<CsobPaymentReconciliationWorker> _logger;

    public CsobPaymentReconciliationWorker(
        IServiceScopeFactory scopeFactory,
        CsobReconciliationConfiguration configuration,
        CsobPaymentReconciliationHealth health,
        TimeProvider timeProvider,
        ILogger<CsobPaymentReconciliationWorker> logger)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(health);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _health = health;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        if (!_configuration.Enabled)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunCycleAsync(stoppingToken);
            }
            catch (OperationCanceledException)
                when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _health.RecordFailedCycle(
                    _timeProvider.GetUtcNow(),
                    exception);
                _logger.LogError(
                    exception,
                    "CSOB reconciliation worker cycle failed.");
            }

            await Task.Delay(
                _configuration.PollInterval,
                stoppingToken);
        }
    }

    internal async Task RunCycleAsync(
        CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var processor = scope.ServiceProvider
            .GetRequiredService<CsobPaymentRecoveryProcessor>();
        var result = await processor.RunOnceAsync(cancellationToken);

        _health.RecordSuccessfulCycle(
            _timeProvider.GetUtcNow());

        if (
            result.ScheduledUncertainCount > 0 ||
            result.StaleInProgressCount > 0 ||
            result.SeededAttentionCount > 0 ||
            result.ClaimedCount > 0 ||
            result.ScheduledCount > 0 ||
            result.RequiresAttentionCount > 0 ||
            result.LostClaimCount > 0)
        {
            _logger.LogInformation(
                "CSOB reconciliation cycle: stale in-progress {Stale}, " +
                "recovered {Recovered}, " +
                "seeded attention {SeededAttention}, scheduled {Scheduled}, " +
                "claimed {Claimed}, completed {Completed}, " +
                "rescheduled {Rescheduled}, attention {Attention}, " +
                "lost claims {LostClaims}.",
                result.StaleInProgressCount,
                result.ScheduledUncertainCount,
                result.SeededAttentionCount,
                result.ScheduledCount,
                result.ClaimedCount,
                result.CompletedCount,
                result.RescheduledCount,
                result.RequiresAttentionCount,
                result.LostClaimCount);
        }
    }
}
