using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FuaPay.Web.Modules.Payments.Infrastructure.Csob;

public sealed class CsobPaymentReconciliationWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly CsobReconciliationConfiguration _configuration;
    private readonly ILogger<CsobPaymentReconciliationWorker> _logger;

    public CsobPaymentReconciliationWorker(
        IServiceScopeFactory scopeFactory,
        CsobReconciliationConfiguration configuration,
        ILogger<CsobPaymentReconciliationWorker> logger)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(logger);

        _scopeFactory = scopeFactory;
        _configuration = configuration;
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
                await using var scope = _scopeFactory.CreateAsyncScope();
                var processor = scope.ServiceProvider
                    .GetRequiredService<CsobPaymentRecoveryProcessor>();
                var result = await processor.RunOnceAsync(stoppingToken);

                if (
                    result.ScheduledUncertainCount > 0 ||
                    result.StaleInProgressCount > 0 ||
                    result.SeededAttentionCount > 0 ||
                    result.ClaimedCount > 0 ||
                    result.ScheduledCount > 0 ||
                    result.RequiresAttentionCount > 0)
                {
                    _logger.LogInformation(
                        "CSOB reconciliation cycle: stale in-progress {Stale}, " +
                        "recovered {Recovered}, " +
                        "seeded attention {SeededAttention}, scheduled {Scheduled}, " +
                        "claimed {Claimed}, completed {Completed}, " +
                        "rescheduled {Rescheduled}, attention {Attention}.",
                        result.StaleInProgressCount,
                        result.ScheduledUncertainCount,
                        result.SeededAttentionCount,
                        result.ScheduledCount,
                        result.ClaimedCount,
                        result.CompletedCount,
                        result.RescheduledCount,
                        result.RequiresAttentionCount);
                }
            }
            catch (OperationCanceledException)
                when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(
                    exception,
                    "CSOB reconciliation worker cycle failed.");
            }

            await Task.Delay(
                _configuration.PollInterval,
                stoppingToken);
        }
    }
}
