using FuaPay.Web.BuildingBlocks.Application;
using FuaPay.Web.BuildingBlocks.Auditing;
using FuaPay.Web.Modules.Payments.Application;
using FuaPay.Web.Modules.Payments.Domain;

namespace FuaPay.Web.Modules.Payments.Infrastructure.Csob;

public sealed class CsobPaymentReconciliationService :
    ICsobPaymentReconciliationService
{
    private readonly ICsobGatewayClient _gatewayClient;
    private readonly IPaymentRepository _paymentRepository;
    private readonly IPaymentInitiationRepository _initiationRepository;
    private readonly ICsobVerifiedReturnEvidenceReader _returnEvidenceReader;
    private readonly ICsobExpiryCorrectionGuard _expiryCorrectionGuard;
    private readonly IPaymentSettlementService _settlementService;
    private readonly IApplicationTransaction _transaction;
    private readonly TimeProvider _timeProvider;
    private readonly IAuditTrail _auditTrail;

    public CsobPaymentReconciliationService(
        ICsobGatewayClient gatewayClient,
        IPaymentRepository paymentRepository,
        IPaymentInitiationRepository initiationRepository,
        ICsobVerifiedReturnEvidenceReader returnEvidenceReader,
        ICsobExpiryCorrectionGuard expiryCorrectionGuard,
        IPaymentSettlementService settlementService,
        IApplicationTransaction transaction,
        TimeProvider timeProvider,
        IAuditTrail auditTrail)
    {
        ArgumentNullException.ThrowIfNull(gatewayClient);
        ArgumentNullException.ThrowIfNull(paymentRepository);
        ArgumentNullException.ThrowIfNull(initiationRepository);
        ArgumentNullException.ThrowIfNull(returnEvidenceReader);
        ArgumentNullException.ThrowIfNull(expiryCorrectionGuard);
        ArgumentNullException.ThrowIfNull(settlementService);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(auditTrail);

        _gatewayClient = gatewayClient;
        _paymentRepository = paymentRepository;
        _initiationRepository = initiationRepository;
        _returnEvidenceReader = returnEvidenceReader;
        _expiryCorrectionGuard = expiryCorrectionGuard;
        _settlementService = settlementService;
        _transaction = transaction;
        _timeProvider = timeProvider;
        _auditTrail = auditTrail;
    }

    public async Task<CsobPaymentReconciliationResult> ReconcileAsync(
        Guid paymentId,
        string payId,
        CancellationToken cancellationToken = default)
    {
        if (paymentId == Guid.Empty)
        {
            throw new ArgumentException(
                "ID platby nesmí být prázdné.",
                nameof(paymentId));
        }

        var normalizedPayId = CsobPayId.RequireCanonical(
            payId,
            nameof(payId));
        var payment = await _paymentRepository.FindByIdAsync(
                paymentId,
                cancellationToken)
            ?? throw new PaymentProviderReferenceNotFoundException(
                PaymentProvider.Csob,
                normalizedPayId);

        if (payment.Id != paymentId || payment.Provider != PaymentProvider.Csob)
        {
            throw CreateProviderReferenceConflictException(
                payment,
                normalizedPayId);
        }

        if (payment.Status == PaymentStatus.Created)
        {
            var initiation =
                await _initiationRepository.FindByPaymentIdAsync(
                    paymentId,
                    cancellationToken);
            var currentPayment = await ResolveMixedInitializationReadAsync(
                payment, initiation, paymentId, cancellationToken);

            if (
                currentPayment is not { Status: PaymentStatus.Created or PaymentStatus.Pending } ||
                !HasInitializationBinding(currentPayment, initiation, paymentId, normalizedPayId))
            {
                throw CreateProviderReferenceConflictException(
                    payment,
                    normalizedPayId);
            }
        }
        else if (!string.Equals(
            payment.ProviderReference,
            normalizedPayId,
            StringComparison.Ordinal))
        {
            throw CreateProviderReferenceConflictException(
                payment,
                normalizedPayId);
        }

        var verifiedExpiry =
            await _returnEvidenceReader.FindVerifiedExpiryAsync(
                payment.Id,
                normalizedPayId,
                cancellationToken);
        ValidateVerifiedExpiryEvidence(
            verifiedExpiry,
            gatewayPaymentStatus: null,
            gatewayResultCode: null);

        var gatewayStatus = await _gatewayClient.GetStatusAsync(
            normalizedPayId,
            cancellationToken);

        if (
            gatewayStatus.ResultCode == 130 &&
            gatewayStatus.PaymentStatus == 6)
        {
            return payment.Status == PaymentStatus.Created
                ? await RecoverExpiredInitializationAsync(
                    payment,
                    normalizedPayId,
                    gatewayStatus.ResultCode,
                    gatewayStatus.PaymentStatus,
                    cancellationToken)
                : await ChangeTerminalStateAsync(
                    payment.Id,
                    normalizedPayId,
                    PaymentStatus.Expired,
                    gatewayStatus.ResultCode,
                    gatewayStatus.PaymentStatus,
                    cancellationToken,
                    verifiedExpiry);
        }

        if (gatewayStatus.ResultCode != 0)
        {
            throw new CsobPaymentRequiresAttentionException(
                "Podepsaná odpověď payment/status nevrátila úspěšný výsledek; lokální finanční stav zůstává beze změny.",
                gatewayStatus.PaymentStatus,
                gatewayStatus.ResultCode);
        }

        var isPaidStatus = gatewayStatus.PaymentStatus is 7 or 8;
        if (
            payment.Status == PaymentStatus.Created &&
            (isPaidStatus || gatewayStatus.PaymentStatus is 2 or 3 or 4 or 6))
        {
            var currentPayment = await _paymentRepository.FindByIdAsync(
                paymentId, cancellationToken);
            var initiation = await _initiationRepository.FindByPaymentIdAsync(
                paymentId, cancellationToken);
            currentPayment = await ResolveMixedInitializationReadAsync(
                currentPayment, initiation, paymentId, cancellationToken);

            if (
                currentPayment is null ||
                (currentPayment.Status != PaymentStatus.Pending &&
                    !(isPaidStatus && currentPayment.Status == PaymentStatus.Succeeded)) ||
                !HasInitializationBinding(currentPayment, initiation, paymentId, normalizedPayId))
            {
                throw CreateInitializationConflictException(
                    currentPayment ?? payment,
                    gatewayStatus.PaymentStatus,
                    gatewayStatus.ResultCode);
            }

            payment = currentPayment;
        }

        if (payment.Status == PaymentStatus.Created)
        {
            return await RecoverInitializationAsync(
                payment,
                normalizedPayId,
                gatewayStatus.ResultCode,
                gatewayStatus.PaymentStatus,
                cancellationToken);
        }

        if (gatewayStatus.PaymentStatus == 6)
        {
            if (verifiedExpiry is not null)
            {
                return await ChangeTerminalStateAsync(
                    payment.Id,
                    normalizedPayId,
                    PaymentStatus.Expired,
                    gatewayStatus.ResultCode,
                    gatewayStatus.PaymentStatus,
                    cancellationToken,
                    verifiedExpiry);
            }
        }

        return gatewayStatus.PaymentStatus switch
        {
            1 or 2 or 4 => RequirePending(
                payment,
                gatewayStatus.ResultCode,
                gatewayStatus.PaymentStatus),
            3 => await ChangeTerminalStateAsync(
                payment.Id,
                normalizedPayId,
                PaymentStatus.Cancelled,
                gatewayStatus.ResultCode,
                gatewayStatus.PaymentStatus,
                cancellationToken),
            6 => await ChangeTerminalStateAsync(
                payment.Id,
                normalizedPayId,
                PaymentStatus.Failed,
                gatewayStatus.ResultCode,
                gatewayStatus.PaymentStatus,
                cancellationToken),
            7 or 8 => await SettleAsync(
                payment,
                normalizedPayId,
                gatewayStatus.ResultCode,
                gatewayStatus.PaymentStatus,
                cancellationToken),
            5 or 9 or 10 => throw CreateUnsupportedLifecycleException(
                payment,
                gatewayStatus.PaymentStatus,
                gatewayStatus.ResultCode),
            _ => throw new CsobPaymentRequiresAttentionException(
                $"Platební brána ČSOB vrátila neznámý stav " +
                $"'{gatewayStatus.PaymentStatus}' pro platbu " +
                $"'{payment.Id}'.",
                gatewayStatus.PaymentStatus,
                gatewayStatus.ResultCode)
        };
    }

    private async Task<Payment?> ResolveMixedInitializationReadAsync(
        Payment? payment,
        PaymentInitiation? initiation,
        Guid paymentId,
        CancellationToken cancellationToken)
    {
        if (
            payment is { Status: PaymentStatus.Created, Provider: PaymentProvider.Csob } &&
            payment.Id == paymentId &&
            initiation is { State: PaymentInitiationState.Initialized, Provider: PaymentProvider.Csob } &&
            initiation.PaymentId == paymentId)
        {
            // At READ COMMITTED the web commit can land between the two reads.
            return await _paymentRepository.FindByIdAsync(paymentId, cancellationToken);
        }

        return payment;
    }

    private static bool HasInitializationBinding(
        Payment payment,
        PaymentInitiation? initiation,
        Guid paymentId,
        string payId)
    {
        if (
            payment.Id != paymentId ||
            payment.Provider != PaymentProvider.Csob ||
            initiation is null ||
            initiation.PaymentId != paymentId ||
            initiation.Provider != PaymentProvider.Csob)
        {
            return false;
        }

        return payment.Status switch
        {
            PaymentStatus.Created =>
                initiation.State == PaymentInitiationState.Uncertain &&
                string.Equals(initiation.ObservedProviderReference, payId, StringComparison.Ordinal),
            PaymentStatus.Pending or PaymentStatus.Succeeded or PaymentStatus.Expired =>
                initiation.State == PaymentInitiationState.Initialized &&
                string.Equals(payment.ProviderReference, payId, StringComparison.Ordinal),
            _ => false
        };
    }

    private async Task<CsobPaymentReconciliationResult>
        RecoverInitializationAsync(
            Payment payment,
            string payId,
            int gatewayResultCode,
            int gatewayPaymentStatus,
            CancellationToken cancellationToken)
    {
        if (gatewayPaymentStatus != 1)
        {
            throw new CsobPaymentRequiresAttentionException(
                $"Ověřený stav ČSOB '{gatewayPaymentStatus}' není správný " +
                $"pre-process stav 1 pro obnovu inicializace platby " +
                $"'{payment.Id}'.",
                gatewayPaymentStatus,
                gatewayResultCode);
        }

        try
        {
            // The web request may have completed initialization during payment/status.
            return await _transaction.ExecuteAsync(
                async ct =>
                {
                    var currentPayment =
                        await _paymentRepository.FindByIdAsync(
                            payment.Id,
                            ct)
                        ?? throw new PaymentProviderReferenceNotFoundException(
                            PaymentProvider.Csob,
                            payId);
                    var currentInitiation =
                        await _initiationRepository.FindByPaymentIdAsync(
                            payment.Id,
                            ct)
                        ?? throw CreateInitializationConflictException(
                            currentPayment,
                            gatewayPaymentStatus);

                    var resolvedPayment = await ResolveMixedInitializationReadAsync(
                        currentPayment, currentInitiation, payment.Id, ct);

                    if (
                        resolvedPayment is not { Status: PaymentStatus.Created or PaymentStatus.Pending } ||
                        !HasInitializationBinding(resolvedPayment, currentInitiation, payment.Id, payId))
                    {
                        throw CreateInitializationConflictException(
                            resolvedPayment ?? currentPayment,
                            gatewayPaymentStatus,
                            gatewayResultCode);
                    }

                    currentPayment = resolvedPayment;
                    if (currentPayment.Status == PaymentStatus.Pending)
                    {
                        return new CsobPaymentReconciliationResult(
                            currentPayment.Id,
                            currentPayment.Status,
                            gatewayPaymentStatus,
                            gatewayResultCode,
                            StateChanged: false);
                    }

                    var verifiedAt = _timeProvider.GetUtcNow();
                    currentPayment.MarkPending(payId, verifiedAt);
                    currentInitiation.RecoverObservedInitialization(
                        verifiedAt);

                    _auditTrail.Stage(AuditEntry.ForProcess(
                        "payment-reconciliation",
                        "payment.provider-initiation.status-verified",
                        "payment",
                        currentPayment.Id.ToString(),
                        $"Persistovaná ČSOB reference {payId} platby " +
                        $"{currentPayment.Id} byla ověřena payment/status " +
                        "ve stavu 1; teprve poté byla inicializace dokončena.",
                        verifiedAt));

                    await _paymentRepository.SaveAsync(
                        currentPayment,
                        ct);
                    await _initiationRepository.SaveAsync(
                        currentInitiation,
                        ct);

                    return new CsobPaymentReconciliationResult(
                        currentPayment.Id,
                        currentPayment.Status,
                        gatewayPaymentStatus,
                        gatewayResultCode,
                        StateChanged: true);
                },
                cancellationToken);
        }
        catch (PaymentConcurrencyException)
        {
            var persistedPayment =
                await _paymentRepository.FindByIdAsync(
                    payment.Id,
                    cancellationToken);
            var persistedInitiation =
                await _initiationRepository.FindByPaymentIdAsync(
                    payment.Id,
                    cancellationToken);

            if (
                persistedPayment is { Status: PaymentStatus.Pending } &&
                HasInitializationBinding(persistedPayment, persistedInitiation, payment.Id, payId))
            {
                return new CsobPaymentReconciliationResult(
                    payment.Id,
                    PaymentStatus.Pending,
                    gatewayPaymentStatus,
                    gatewayResultCode,
                    StateChanged: false);
            }

            throw;
        }
    }

    private async Task<CsobPaymentReconciliationResult>
        RecoverExpiredInitializationAsync(
            Payment payment,
            string payId,
            int gatewayResultCode,
            int gatewayPaymentStatus,
            CancellationToken cancellationToken)
    {
        try
        {
            return await _transaction.ExecuteAsync(
                async ct =>
                {
                    var currentPayment =
                        await _paymentRepository.FindByIdAsync(
                            payment.Id,
                            ct)
                        ?? throw new PaymentProviderReferenceNotFoundException(
                            PaymentProvider.Csob,
                            payId);
                    var currentInitiation =
                        await _initiationRepository.FindByPaymentIdAsync(
                            payment.Id,
                            ct)
                        ?? throw CreateInitializationConflictException(
                            currentPayment,
                            gatewayPaymentStatus,
                            gatewayResultCode);

                    var resolvedPayment = await ResolveMixedInitializationReadAsync(
                        currentPayment, currentInitiation, payment.Id, ct);

                    if (
                        resolvedPayment is not { Status: PaymentStatus.Created or PaymentStatus.Pending or PaymentStatus.Expired } ||
                        !HasInitializationBinding(resolvedPayment, currentInitiation, payment.Id, payId))
                    {
                        throw CreateInitializationConflictException(
                            resolvedPayment ?? currentPayment,
                            gatewayPaymentStatus,
                            gatewayResultCode);
                    }

                    currentPayment = resolvedPayment;
                    if (currentPayment.Status == PaymentStatus.Expired)
                    {
                        return new CsobPaymentReconciliationResult(
                            currentPayment.Id,
                            currentPayment.Status,
                            gatewayPaymentStatus,
                            gatewayResultCode,
                            StateChanged: false);
                    }

                    var expiredAt = _timeProvider.GetUtcNow();

                    if (currentPayment.Status == PaymentStatus.Pending)
                    {
                        var changed = currentPayment.Expire(expiredAt);

                        if (changed)
                        {
                            StageTerminalAudit(
                                currentPayment,
                                PaymentStatus.Expired,
                                gatewayPaymentStatus,
                                expiredAt);
                            await _paymentRepository.SaveAsync(
                                currentPayment,
                                ct);
                        }

                        return new CsobPaymentReconciliationResult(
                            currentPayment.Id,
                            currentPayment.Status,
                            gatewayPaymentStatus,
                            gatewayResultCode,
                            changed);
                    }

                    currentPayment.MarkPending(payId, expiredAt);
                    currentInitiation.RecoverObservedInitialization(
                        expiredAt);
                    currentPayment.Expire(expiredAt);

                    _auditTrail.Stage(AuditEntry.ForProcess(
                        "payment-reconciliation",
                        "payment.provider-initiation.expiry-verified",
                        "payment",
                        currentPayment.Id.ToString(),
                        $"Persistovaná ČSOB reference {payId} platby " +
                        $"{currentPayment.Id} byla ověřena podepsaným " +
                        $"payment/status {gatewayResultCode}/" +
                        $"{gatewayPaymentStatus}; inicializace byla " +
                        "bez finančního efektu uzavřena jako expirovaná.",
                        expiredAt));
                    StageTerminalAudit(
                        currentPayment,
                        PaymentStatus.Expired,
                        gatewayPaymentStatus,
                        expiredAt);

                    await _paymentRepository.SaveAsync(
                        currentPayment,
                        ct);
                    await _initiationRepository.SaveAsync(
                        currentInitiation,
                        ct);

                    return new CsobPaymentReconciliationResult(
                        currentPayment.Id,
                        currentPayment.Status,
                        gatewayPaymentStatus,
                        gatewayResultCode,
                        StateChanged: true);
                },
                cancellationToken);
        }
        catch (PaymentConcurrencyException)
        {
            var persistedPayment =
                await _paymentRepository.FindByIdAsync(
                    payment.Id,
                    cancellationToken);
            var persistedInitiation =
                await _initiationRepository.FindByPaymentIdAsync(
                    payment.Id,
                    cancellationToken);

            if (
                persistedPayment is { Status: PaymentStatus.Expired } &&
                HasInitializationBinding(persistedPayment, persistedInitiation, payment.Id, payId))
            {
                return new CsobPaymentReconciliationResult(
                    payment.Id,
                    PaymentStatus.Expired,
                    gatewayPaymentStatus,
                    gatewayResultCode,
                    StateChanged: false);
            }

            throw;
        }
    }

    private async Task<CsobPaymentReconciliationResult> SettleAsync(
        Payment payment,
        string payId,
        int gatewayResultCode,
        int gatewayPaymentStatus,
        CancellationToken cancellationToken)
    {
        if (
            payment.Status != PaymentStatus.Pending &&
            payment.Status != PaymentStatus.Succeeded)
        {
            throw CreateLifecycleConflictException(
                payment,
                gatewayPaymentStatus,
                gatewayResultCode);
        }

        var changed = await _settlementService.CompleteAsync(
            new VerifiedPaymentConfirmation(
                PaymentProvider.Csob,
                payId,
                payment.Amount),
            cancellationToken);

        return new CsobPaymentReconciliationResult(
            payment.Id,
            PaymentStatus.Succeeded,
            gatewayPaymentStatus,
            gatewayResultCode,
            changed);
    }

    private async Task<CsobPaymentReconciliationResult>
        ChangeTerminalStateAsync(
            Guid paymentId,
            string payId,
            PaymentStatus targetStatus,
            int gatewayResultCode,
            int gatewayPaymentStatus,
            CancellationToken cancellationToken,
            CsobVerifiedReturnEvidence? verifiedExpiry = null)
    {
        try
        {
            return await _transaction.ExecuteAsync(
                async ct =>
                {
                    var payment =
                        await _paymentRepository
                            .FindByProviderReferenceAsync(
                                PaymentProvider.Csob,
                                payId,
                                ct)
                        ?? throw new PaymentProviderReferenceNotFoundException(
                            PaymentProvider.Csob,
                            payId);

                    if (payment.Id != paymentId)
                    {
                        throw CreateProviderReferenceConflictException(
                            payment,
                            payId);
                    }

                    if (payment.Status == targetStatus)
                    {
                        return new CsobPaymentReconciliationResult(
                            payment.Id,
                            payment.Status,
                            gatewayPaymentStatus,
                            gatewayResultCode,
                            StateChanged: false);
                    }

                    var correctingEligibleFailure =
                        targetStatus == PaymentStatus.Expired &&
                        payment.Status == PaymentStatus.Failed &&
                        verifiedExpiry is not null;

                    if (
                        payment.Status != PaymentStatus.Pending &&
                        !correctingEligibleFailure)
                    {
                        throw CreateLifecycleConflictException(
                            payment,
                            gatewayPaymentStatus,
                            gatewayResultCode);
                    }

                    var occurredAt = _timeProvider.GetUtcNow();
                    bool changed;

                    if (correctingEligibleFailure)
                    {
                        ValidateVerifiedExpiryEvidence(
                            verifiedExpiry,
                            gatewayPaymentStatus,
                            gatewayResultCode);

                        if (
                            payment.Provider != PaymentProvider.Csob ||
                            payment.FailureProvenance !=
                                PaymentFailureProvenance.CsobResult0Status6 ||
                            !await _expiryCorrectionGuard
                            .IsFinanciallyUntouchedAsync(
                                payment.Id,
                                payment.JobId,
                                ct))
                        {
                            throw CreateLifecycleConflictException(
                                payment,
                                gatewayPaymentStatus,
                                gatewayResultCode);
                        }

                        changed = payment
                            .CorrectCsobResult0Status6FailureToExpired(
                            payId,
                            occurredAt);
                    }
                    else
                    {
                        changed = targetStatus switch
                        {
                            PaymentStatus.Cancelled =>
                                payment.Cancel(occurredAt),
                            PaymentStatus.Failed =>
                                payment.FailFromCsobResult0Status6(
                                    "Platba byla platební bránou ČSOB zamítnuta.",
                                    occurredAt),
                            PaymentStatus.Expired =>
                                payment.Expire(occurredAt),
                            _ => throw new InvalidOperationException(
                                "Nepodporovaný cílový stav rekonciliace platby.")
                        };
                    }

                    if (changed)
                    {
                        StageTerminalAudit(
                            payment,
                            targetStatus,
                            gatewayPaymentStatus,
                            occurredAt);
                        await _paymentRepository.SaveAsync(payment, ct);
                    }

                    return new CsobPaymentReconciliationResult(
                        payment.Id,
                        payment.Status,
                        gatewayPaymentStatus,
                        gatewayResultCode,
                        changed);
                },
                cancellationToken);
        }
        catch (PaymentConcurrencyException)
        {
            var persisted =
                await _paymentRepository.FindByProviderReferenceAsync(
                    PaymentProvider.Csob,
                    payId,
                    cancellationToken);

            if (
                persisted?.Id == paymentId &&
                persisted.Status == targetStatus)
            {
                return new CsobPaymentReconciliationResult(
                    persisted.Id,
                    persisted.Status,
                    gatewayPaymentStatus,
                    gatewayResultCode,
                    StateChanged: false);
            }

            throw;
        }
    }

    private static CsobPaymentReconciliationResult RequirePending(
        Payment payment,
        int gatewayResultCode,
        int gatewayPaymentStatus)
    {
        if (payment.Status != PaymentStatus.Pending)
        {
            throw CreateLifecycleConflictException(
                payment,
                gatewayPaymentStatus,
                gatewayResultCode);
        }

        return new CsobPaymentReconciliationResult(
            payment.Id,
            payment.Status,
            gatewayPaymentStatus,
            gatewayResultCode,
            StateChanged: false);
    }

    private static void ValidateVerifiedExpiryEvidence(
        CsobVerifiedReturnEvidence? verifiedExpiry,
        int? gatewayPaymentStatus,
        int? gatewayResultCode)
    {
        if (verifiedExpiry is null)
        {
            return;
        }

        if (
            verifiedExpiry.ResultCode != 130 ||
            verifiedExpiry.PaymentStatus != 6 ||
            string.IsNullOrWhiteSpace(verifiedExpiry.TextToSign) ||
            string.IsNullOrWhiteSpace(verifiedExpiry.Signature))
        {
            throw new CsobPaymentRequiresAttentionException(
                "Persistovaná evidence návratu ČSOB není konzistentní; " +
                "lokální finanční stav zůstává beze změny.",
                gatewayPaymentStatus,
                gatewayResultCode);
        }
    }

    private void StageTerminalAudit(
        Payment payment,
        PaymentStatus targetStatus,
        int gatewayPaymentStatus,
        DateTimeOffset occurredAt)
    {
        var (action, description) = targetStatus switch
        {
            PaymentStatus.Cancelled => (
                "payment.cancelled",
                $"Platba {payment.Id} byla podle ověřeného stavu " +
                $"ČSOB {gatewayPaymentStatus} zrušena."),
            PaymentStatus.Failed => (
                "payment.failed",
                $"Platba {payment.Id} byla podle ověřeného stavu " +
                $"ČSOB {gatewayPaymentStatus} označena jako neúspěšná."),
            PaymentStatus.Expired => (
                "payment.expired",
                $"Platba {payment.Id} byla podle ověřeného stavu " +
                $"ČSOB {gatewayPaymentStatus} označena jako expirovaná."),
            _ => throw new InvalidOperationException(
                "Audit nepodporuje cílový stav rekonciliace platby.")
        };

        _auditTrail.Stage(AuditEntry.ForProcess(
            "payment-provider",
            action,
            "payment",
            payment.Id.ToString(),
            description,
            occurredAt));
    }

    private static CsobGatewayException CreateLifecycleConflictException(
        Payment payment,
        int gatewayPaymentStatus,
        int? gatewayResultCode = null) =>
        new CsobPaymentRequiresAttentionException(
            $"Ověřený stav ČSOB '{gatewayPaymentStatus}' je v rozporu " +
            $"s lokálním stavem '{payment.Status}' platby '{payment.Id}'.",
            gatewayPaymentStatus,
            gatewayResultCode);

    private static CsobGatewayException CreateInitializationConflictException(
        Payment payment,
        int gatewayPaymentStatus,
        int? gatewayResultCode = null) =>
        new CsobPaymentRequiresAttentionException(
            $"Ověřený stav ČSOB '{gatewayPaymentStatus}' nelze bezpečně " +
            $"aplikovat na nejasnou inicializaci platby '{payment.Id}'.",
            gatewayPaymentStatus,
            gatewayResultCode);

    private static CsobGatewayException CreateProviderReferenceConflictException(
        Payment payment,
        string payId) =>
        new CsobPaymentRequiresAttentionException(
            $"ČSOB payId '{payId}' neodpovídá lokální provider vazbě " +
            $"platby '{payment.Id}'.");

    private static CsobGatewayException CreateUnsupportedLifecycleException(
        Payment payment,
        int gatewayPaymentStatus,
        int gatewayResultCode) =>
        new CsobPaymentRequiresAttentionException(
            $"Ověřený stav ČSOB '{gatewayPaymentStatus}' pro platbu " +
            $"'{payment.Id}' vyžaduje samostatnou reverse/refund " +
            "rekonciliaci a nebude automaticky měnit finanční stav.",
            gatewayPaymentStatus,
            gatewayResultCode);
}
