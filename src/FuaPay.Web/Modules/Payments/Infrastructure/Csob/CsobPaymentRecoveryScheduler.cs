using FuaPay.Web.BuildingBlocks.Application;
using FuaPay.Web.BuildingBlocks.Auditing;
using FuaPay.Web.Modules.Payments.Application;
using FuaPay.Web.Modules.Payments.Domain;

namespace FuaPay.Web.Modules.Payments.Infrastructure.Csob;

public sealed class CsobPaymentRecoveryScheduler :
    ICsobPaymentRecoveryScheduler
{
    private readonly ICsobPaymentRecoveryRepository _repository;
    private readonly IApplicationTransaction _transaction;
    private readonly TimeProvider _timeProvider;
    private readonly IAuditTrail _auditTrail;

    public CsobPaymentRecoveryScheduler(
        ICsobPaymentRecoveryRepository repository,
        IApplicationTransaction transaction,
        TimeProvider timeProvider,
        IAuditTrail auditTrail)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(auditTrail);

        _repository = repository;
        _transaction = transaction;
        _timeProvider = timeProvider;
        _auditTrail = auditTrail;
    }

    public async Task<Guid> ScheduleReturnAsync(
        CsobVerifiedPaymentReturn verifiedReturn,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(verifiedReturn);

        _ = CsobPayId.RequireCanonical(
            verifiedReturn.PayId,
            nameof(verifiedReturn));

        var observedAt = _timeProvider.GetUtcNow();
        return await _transaction.ExecuteAsync(
            async ct =>
            {
                var observation =
                    await _repository.ScheduleFromReturnAsync(
                        verifiedReturn,
                        observedAt,
                        ct);

                if (observation is null)
                {
                    throw new PaymentProviderReferenceNotFoundException(
                        PaymentProvider.Csob,
                        verifiedReturn.PayId);
                }

                if (observation.IsFirstObservation)
                {
                    await _auditTrail.WriteAsync(
                        AuditEntry.ForProcess(
                            "payment-provider",
                            "payment.reconciliation.return-observed",
                            "payment",
                            observation.PaymentId.ToString(),
                            $"Pro platbu {observation.PaymentId} byl poprvé " +
                            "zaznamenán návrat z ČSOB; finanční stav se ověří " +
                            "serverovou reconciliation.",
                            observedAt),
                        ct);
                }

                if (observation.IsFirstVerifiedExpiryObservation)
                {
                    await _auditTrail.WriteAsync(
                        AuditEntry.ForProcess(
                            "payment-provider",
                            "payment.reconciliation.verified-expiry-return-observed",
                            "payment",
                            observation.PaymentId.ToString(),
                            $"Pro platbu {observation.PaymentId} byl " +
                            "durabilně zaznamenán validně podepsaný návrat " +
                            "ČSOB 130/6; finanční stav nadále určí až " +
                            "serverová reconciliation.",
                            observedAt),
                        ct);
                }

                return observation.PaymentId;
            },
            cancellationToken);
    }
}
