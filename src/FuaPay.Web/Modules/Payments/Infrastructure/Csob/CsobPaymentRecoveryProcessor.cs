using FuaPay.Web.BuildingBlocks.Application;
using FuaPay.Web.BuildingBlocks.Auditing;
using FuaPay.Web.Modules.Payments.Application;
using FuaPay.Web.Modules.Payments.Domain;

using Microsoft.Extensions.Logging;

namespace FuaPay.Web.Modules.Payments.Infrastructure.Csob;

public sealed class CsobPaymentRecoveryProcessor
{
    private readonly ICsobPaymentRecoveryRepository _repository;
    private readonly ICsobPaymentReconciliationService _reconciliationService;
    private readonly IApplicationTransaction _transaction;
    private readonly CsobReconciliationConfiguration _configuration;
    private readonly TimeProvider _timeProvider;
    private readonly IAuditTrail _auditTrail;
    private readonly ILogger<CsobPaymentRecoveryProcessor> _logger;

    public CsobPaymentRecoveryProcessor(
        ICsobPaymentRecoveryRepository repository,
        ICsobPaymentReconciliationService reconciliationService,
        IApplicationTransaction transaction,
        CsobReconciliationConfiguration configuration,
        TimeProvider timeProvider,
        IAuditTrail auditTrail,
        ILogger<CsobPaymentRecoveryProcessor> logger)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(reconciliationService);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(auditTrail);
        ArgumentNullException.ThrowIfNull(logger);

        _repository = repository;
        _reconciliationService = reconciliationService;
        _transaction = transaction;
        _configuration = configuration;
        _timeProvider = timeProvider;
        _auditTrail = auditTrail;
        _logger = logger;
    }

    public async Task<CsobPaymentRecoveryCycleResult> RunOnceAsync(
        CancellationToken cancellationToken = default)
    {
        if (!_configuration.Enabled)
        {
            return new CsobPaymentRecoveryCycleResult(
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0);
        }

        var now = _timeProvider.GetUtcNow();
        var staleInProgress =
            await _repository.RegisterStaleInProgressAsync(
                now - _configuration.InProgressMaximumAge,
                now,
                _configuration.BatchSize,
                cancellationToken);
        var scheduledUncertain =
            await _repository.ScheduleRecoverableUncertainAsync(
                now,
                _configuration.BatchSize,
                cancellationToken);
        var attentionSeeded =
            await _repository.RegisterUnrecoverableUncertainAsync(
                now,
                _configuration.BatchSize,
                cancellationToken);
        var scheduled = await _repository.ScheduleLongOpenPaymentsAsync(
            now - _configuration.PendingMinimumAge,
            now,
            _configuration.BatchSize,
            cancellationToken);
        var claims = await _repository.ClaimDueAsync(
            now,
            _configuration.LeaseDuration,
            _configuration.BatchSize,
            cancellationToken);

        var completed = 0;
        var rescheduled = 0;
        var attention = 0;
        var lostClaims = 0;

        foreach (var claim in claims)
        {
            var disposition = await ProcessClaimAsync(
                claim,
                cancellationToken);

            switch (disposition)
            {
                case CsobPaymentRecoveryDisposition.Completed:
                    completed++;
                    break;
                case CsobPaymentRecoveryDisposition.Rescheduled:
                    rescheduled++;
                    break;
                case CsobPaymentRecoveryDisposition.RequiresAttention:
                    attention++;
                    break;
                case CsobPaymentRecoveryDisposition.ClaimLost:
                    lostClaims++;
                    break;
            }
        }

        return new CsobPaymentRecoveryCycleResult(
            staleInProgress,
            scheduledUncertain,
            attentionSeeded,
            scheduled,
            claims.Count,
            completed,
            rescheduled,
            attention,
            lostClaims);
    }

    private async Task<CsobPaymentRecoveryDisposition> ProcessClaimAsync(
        CsobPaymentRecoveryClaim claim,
        CancellationToken cancellationToken)
    {
        var attemptedAt = _timeProvider.GetUtcNow();

        try
        {
            var result = await _reconciliationService.ReconcileAsync(
                claim.PaymentId,
                claim.ProviderReference,
                cancellationToken);

            if (result.PaymentStatus == PaymentStatus.Pending)
            {
                return await RetryOrRequireAttentionAsync(
                    claim,
                    attemptedAt,
                    result.GatewayPaymentStatus,
                    resultCode: 0,
                    "ČSOB platba zatím není v terminálním stavu.",
                    cancellationToken);
            }

            var completed = await TransitionWithAuditAsync(
                ct => _repository.MarkCompletedAsync(
                    claim,
                    attemptedAt,
                    result.GatewayPaymentStatus,
                    resultCode: 0,
                    ct),
                CreateAuditEntry(
                    claim.PaymentId,
                    "payment.reconciliation.completed",
                    $"Reconciliation platby {claim.PaymentId} skončila " +
                    $"ověřeným stavem ČSOB {result.GatewayPaymentStatus}.",
                    attemptedAt),
                cancellationToken);

            return completed
                ? CsobPaymentRecoveryDisposition.Completed
                : CsobPaymentRecoveryDisposition.ClaimLost;
        }
        catch (CsobPaymentRequiresAttentionException exception)
        {
            var attentionTransitioned = await RequireAttentionAsync(
                claim,
                attemptedAt,
                exception.GatewayPaymentStatus,
                exception.ResultCode,
                "Ověřený stav ČSOB vyžaduje ruční provozní kontrolu.",
                cancellationToken);
            return attentionTransitioned
                ? CsobPaymentRecoveryDisposition.RequiresAttention
                : CsobPaymentRecoveryDisposition.ClaimLost;
        }
        catch (PaymentProviderReferenceNotFoundException)
        {
            var transitioned = await RequireAttentionAsync(
                claim,
                attemptedAt,
                gatewayPaymentStatus: null,
                resultCode: null,
                "Lokální vazba ČSOB payId na platbu nebyla nalezena.",
                cancellationToken);
            return transitioned
                ? CsobPaymentRecoveryDisposition.RequiresAttention
                : CsobPaymentRecoveryDisposition.ClaimLost;
        }
        catch (CsobGatewayException exception)
        {
            if (!IsTransientGatewayFailure(exception))
            {
                var transitioned = await RequireAttentionAsync(
                    claim,
                    attemptedAt,
                    gatewayPaymentStatus: null,
                    exception.ResultCode,
                    "Serverové ověření ČSOB vrátilo nedůvěryhodný nebo netransientní výsledek.",
                    cancellationToken);
                return transitioned
                    ? CsobPaymentRecoveryDisposition.RequiresAttention
                    : CsobPaymentRecoveryDisposition.ClaimLost;
            }

            return await RetryOrRequireAttentionAsync(
                claim,
                attemptedAt,
                gatewayPaymentStatus: null,
                exception.ResultCode,
                "Serverové ověření stavu ČSOB selhalo.",
                cancellationToken);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                "Unexpected CSOB reconciliation failure for payment " +
                "{PaymentId}; exception type {ExceptionType}.",
                claim.PaymentId,
                exception.GetType().Name);

            return await RetryOrRequireAttentionAsync(
                claim,
                attemptedAt,
                gatewayPaymentStatus: null,
                resultCode: null,
                $"Neočekávaná chyba reconciliation: {exception.GetType().Name}.",
                cancellationToken);
        }
    }

    private static bool IsTransientGatewayFailure(
        CsobGatewayException exception)
    {
        if (
            exception.InnerException is HttpRequestException or
                TaskCanceledException)
        {
            return true;
        }

        return exception.HttpStatusCode is
                System.Net.HttpStatusCode.RequestTimeout or
                System.Net.HttpStatusCode.TooManyRequests ||
            exception.HttpStatusCode.HasValue &&
            (int)exception.HttpStatusCode.Value >= 500;
    }

    private async Task<CsobPaymentRecoveryDisposition>
        RetryOrRequireAttentionAsync(
            CsobPaymentRecoveryClaim claim,
            DateTimeOffset attemptedAt,
            int? gatewayPaymentStatus,
            int? resultCode,
            string error,
            CancellationToken cancellationToken)
    {
        var nextAttemptNumber = checked(claim.AttemptCount + 1);

        if (nextAttemptNumber >= _configuration.MaximumAttempts)
        {
            var attentionTransitioned = await RequireAttentionAsync(
                claim,
                attemptedAt,
                gatewayPaymentStatus,
                resultCode,
                "Reconciliation vyčerpala automatický limit pokusů.",
                cancellationToken);
            return attentionTransitioned
                ? CsobPaymentRecoveryDisposition.RequiresAttention
                : CsobPaymentRecoveryDisposition.ClaimLost;
        }

        var nextAttemptAt = attemptedAt + CalculateBackoff(nextAttemptNumber);
        var transitioned = await TransitionWithAuditAsync(
            ct => _repository.RescheduleAsync(
                claim,
                attemptedAt,
                nextAttemptAt,
                gatewayPaymentStatus,
                resultCode,
                error,
                ct),
            CreateAuditEntry(
                claim.PaymentId,
                "payment.reconciliation.retry-scheduled",
                $"Další reconciliation platby {claim.PaymentId} byla " +
                $"naplánována na {nextAttemptAt:O}.",
                attemptedAt),
            cancellationToken);

        return transitioned
            ? CsobPaymentRecoveryDisposition.Rescheduled
            : CsobPaymentRecoveryDisposition.ClaimLost;
    }

    private Task<bool> RequireAttentionAsync(
        CsobPaymentRecoveryClaim claim,
        DateTimeOffset attemptedAt,
        int? gatewayPaymentStatus,
        int? resultCode,
        string error,
        CancellationToken cancellationToken)
    {
        return TransitionWithAuditAsync(
            ct => _repository.MarkRequiresAttentionAsync(
                claim,
                attemptedAt,
                gatewayPaymentStatus,
                resultCode,
                error,
                ct),
            CreateAuditEntry(
                claim.PaymentId,
                "payment.reconciliation.requires-attention",
                $"Reconciliation platby {claim.PaymentId} vyžaduje " +
                "ruční provozní kontrolu; finanční stav nebyl domyšlen.",
                attemptedAt),
            cancellationToken);
    }

    private TimeSpan CalculateBackoff(int attemptNumber)
    {
        var delay = _configuration.BaseBackoff;

        for (var index = 1; index < attemptNumber; index++)
        {
            if (delay >= _configuration.MaximumBackoff)
            {
                return _configuration.MaximumBackoff;
            }

            var doubledTicks = delay.Ticks > long.MaxValue / 2
                ? long.MaxValue
                : delay.Ticks * 2;
            delay = TimeSpan.FromTicks(
                Math.Min(
                    doubledTicks,
                    _configuration.MaximumBackoff.Ticks));
        }

        return delay;
    }

    private async Task<bool> TransitionWithAuditAsync(
        Func<CancellationToken, Task<bool>> transition,
        AuditEntry auditEntry,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _transaction.ExecuteAsync(
                async ct =>
                {
                    _auditTrail.Stage(auditEntry);

                    if (!await transition(ct))
                    {
                        throw new RecoveryClaimLostException();
                    }

                    return true;
                },
                cancellationToken);
        }
        catch (RecoveryClaimLostException)
        {
            return false;
        }
    }

    private static AuditEntry CreateAuditEntry(
        Guid paymentId,
        string action,
        string description,
        DateTimeOffset occurredAt)
    {
        return AuditEntry.ForProcess(
            "payment-reconciliation",
            action,
            "payment",
            paymentId.ToString(),
            description,
            occurredAt);
    }

    private sealed class RecoveryClaimLostException : Exception
    {
    }
}

public sealed record CsobPaymentRecoveryCycleResult(
    int StaleInProgressCount,
    int ScheduledUncertainCount,
    int SeededAttentionCount,
    int ScheduledCount,
    int ClaimedCount,
    int CompletedCount,
    int RescheduledCount,
    int RequiresAttentionCount,
    int LostClaimCount);

internal enum CsobPaymentRecoveryDisposition
{
    Completed,
    Rescheduled,
    RequiresAttention,
    ClaimLost
}
