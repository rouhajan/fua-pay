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
    private readonly IPaymentSettlementService _settlementService;
    private readonly IApplicationTransaction _transaction;
    private readonly TimeProvider _timeProvider;
    private readonly IAuditTrail _auditTrail;

    public CsobPaymentReconciliationService(
        ICsobGatewayClient gatewayClient,
        IPaymentRepository paymentRepository,
        IPaymentInitiationRepository initiationRepository,
        IPaymentSettlementService settlementService,
        IApplicationTransaction transaction,
        TimeProvider timeProvider,
        IAuditTrail auditTrail)
    {
        ArgumentNullException.ThrowIfNull(gatewayClient);
        ArgumentNullException.ThrowIfNull(paymentRepository);
        ArgumentNullException.ThrowIfNull(initiationRepository);
        ArgumentNullException.ThrowIfNull(settlementService);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(auditTrail);

        _gatewayClient = gatewayClient;
        _paymentRepository = paymentRepository;
        _initiationRepository = initiationRepository;
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

        if (payment.Provider != PaymentProvider.Csob)
        {
            throw CreateProviderReferenceConflictException(
                payment,
                normalizedPayId);
        }

        if (payment.Status == PaymentStatus.Created)
        {
            var initiation =
                await _initiationRepository.FindByPaymentIdAsync(
                    payment.Id,
                    cancellationToken);

            if (
                initiation is null ||
                initiation.Provider != PaymentProvider.Csob ||
                initiation.State != PaymentInitiationState.Uncertain ||
                !string.Equals(
                    initiation.ObservedProviderReference,
                    normalizedPayId,
                    StringComparison.Ordinal))
            {
                throw CreateProviderReferenceConflictException(
                    payment,
                    normalizedPayId);
            }
        }

        var gatewayStatus = await _gatewayClient.GetStatusAsync(
            normalizedPayId,
            cancellationToken);

        if (gatewayStatus.ResultCode != 0)
        {
            throw new CsobPaymentRequiresAttentionException(
                "Podepsaná odpověď payment/status nevrátila úspěšný výsledek; lokální finanční stav zůstává beze změny.",
                gatewayStatus.PaymentStatus,
                gatewayStatus.ResultCode);
        }

        if (payment.Status == PaymentStatus.Created)
        {
            return await RecoverInitializationAsync(
                payment,
                normalizedPayId,
                gatewayStatus.PaymentStatus,
                cancellationToken);
        }

        if (!string.Equals(
                payment.ProviderReference,
                normalizedPayId,
                StringComparison.Ordinal))
        {
            throw CreateProviderReferenceConflictException(
                payment,
                normalizedPayId);
        }

        return gatewayStatus.PaymentStatus switch
        {
            1 or 2 or 4 => RequirePending(
                payment,
                gatewayStatus.PaymentStatus),
            3 => await ChangeTerminalStateAsync(
                payment.Id,
                normalizedPayId,
                PaymentStatus.Cancelled,
                gatewayStatus.PaymentStatus,
                cancellationToken),
            6 => await ChangeTerminalStateAsync(
                payment.Id,
                normalizedPayId,
                PaymentStatus.Failed,
                gatewayStatus.PaymentStatus,
                cancellationToken),
            7 or 8 => await SettleAsync(
                payment,
                normalizedPayId,
                gatewayStatus.PaymentStatus,
                cancellationToken),
            5 or 9 or 10 => throw CreateUnsupportedLifecycleException(
                payment,
                gatewayStatus.PaymentStatus),
            _ => throw new CsobPaymentRequiresAttentionException(
                $"Platební brána ČSOB vrátila neznámý stav " +
                $"'{gatewayStatus.PaymentStatus}' pro platbu " +
                $"'{payment.Id}'.",
                gatewayStatus.PaymentStatus,
                gatewayStatus.ResultCode)
        };
    }

    private async Task<CsobPaymentReconciliationResult>
        RecoverInitializationAsync(
            Payment payment,
            string payId,
            int gatewayPaymentStatus,
            CancellationToken cancellationToken)
    {
        var initiation = await _initiationRepository.FindByPaymentIdAsync(
            payment.Id,
            cancellationToken);

        if (
            initiation is null ||
            initiation.Provider != PaymentProvider.Csob ||
            initiation.State != PaymentInitiationState.Uncertain ||
            !string.Equals(
                initiation.ObservedProviderReference,
                payId,
                StringComparison.Ordinal))
        {
            throw CreateInitializationConflictException(
                payment,
                gatewayPaymentStatus);
        }

        if (gatewayPaymentStatus != 1)
        {
            throw new CsobPaymentRequiresAttentionException(
                $"Ověřený stav ČSOB '{gatewayPaymentStatus}' není správný " +
                $"pre-process stav 1 pro obnovu inicializace platby " +
                $"'{payment.Id}'.",
                gatewayPaymentStatus);
        }

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
                            gatewayPaymentStatus);

                    if (
                        currentPayment.Status == PaymentStatus.Pending &&
                        currentInitiation.State ==
                            PaymentInitiationState.Initialized &&
                        string.Equals(
                            currentPayment.ProviderReference,
                            payId,
                            StringComparison.Ordinal))
                    {
                        return new CsobPaymentReconciliationResult(
                            currentPayment.Id,
                            currentPayment.Status,
                            gatewayPaymentStatus,
                            StateChanged: false);
                    }

                    if (
                        currentPayment.Provider != PaymentProvider.Csob ||
                        currentPayment.Status != PaymentStatus.Created ||
                        currentInitiation.Provider != PaymentProvider.Csob ||
                        currentInitiation.State !=
                            PaymentInitiationState.Uncertain ||
                        !string.Equals(
                            currentInitiation.ObservedProviderReference,
                            payId,
                            StringComparison.Ordinal))
                    {
                        throw CreateInitializationConflictException(
                            currentPayment,
                            gatewayPaymentStatus);
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
                persistedPayment?.Status == PaymentStatus.Pending &&
                persistedInitiation?.State ==
                    PaymentInitiationState.Initialized &&
                string.Equals(
                    persistedPayment.ProviderReference,
                    payId,
                    StringComparison.Ordinal))
            {
                return new CsobPaymentReconciliationResult(
                    payment.Id,
                    PaymentStatus.Pending,
                    gatewayPaymentStatus,
                    StateChanged: false);
            }

            throw;
        }
    }

    private async Task<CsobPaymentReconciliationResult> SettleAsync(
        Payment payment,
        string payId,
        int gatewayPaymentStatus,
        CancellationToken cancellationToken)
    {
        if (
            payment.Status != PaymentStatus.Pending &&
            payment.Status != PaymentStatus.Succeeded)
        {
            throw CreateLifecycleConflictException(
                payment,
                gatewayPaymentStatus);
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
            changed);
    }

    private async Task<CsobPaymentReconciliationResult>
        ChangeTerminalStateAsync(
            Guid paymentId,
            string payId,
            PaymentStatus targetStatus,
            int gatewayPaymentStatus,
            CancellationToken cancellationToken)
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
                            StateChanged: false);
                    }

                    if (payment.Status != PaymentStatus.Pending)
                    {
                        throw CreateLifecycleConflictException(
                            payment,
                            gatewayPaymentStatus);
                    }

                    var occurredAt = _timeProvider.GetUtcNow();
                    var changed = targetStatus switch
                    {
                        PaymentStatus.Cancelled =>
                            payment.Cancel(occurredAt),
                        PaymentStatus.Failed =>
                            payment.Fail(
                                "Platba byla platební bránou ČSOB zamítnuta.",
                                occurredAt),
                        _ => throw new InvalidOperationException(
                            "Nepodporovaný cílový stav rekonciliace platby.")
                    };

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
                    StateChanged: false);
            }

            throw;
        }
    }

    private static CsobPaymentReconciliationResult RequirePending(
        Payment payment,
        int gatewayPaymentStatus)
    {
        if (payment.Status != PaymentStatus.Pending)
        {
            throw CreateLifecycleConflictException(
                payment,
                gatewayPaymentStatus);
        }

        return new CsobPaymentReconciliationResult(
            payment.Id,
            payment.Status,
            gatewayPaymentStatus,
            StateChanged: false);
    }

    private void StageTerminalAudit(
        Payment payment,
        PaymentStatus targetStatus,
        int gatewayPaymentStatus,
        DateTimeOffset occurredAt)
    {
        var action = targetStatus == PaymentStatus.Cancelled
            ? "payment.cancelled"
            : "payment.failed";
        var description = targetStatus == PaymentStatus.Cancelled
            ? $"Platba {payment.Id} byla podle ověřeného stavu " +
              $"ČSOB {gatewayPaymentStatus} zrušena."
            : $"Platba {payment.Id} byla podle ověřeného stavu " +
              $"ČSOB {gatewayPaymentStatus} označena jako neúspěšná.";

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
        int gatewayPaymentStatus) =>
        new CsobPaymentRequiresAttentionException(
            $"Ověřený stav ČSOB '{gatewayPaymentStatus}' je v rozporu " +
            $"s lokálním stavem '{payment.Status}' platby '{payment.Id}'.",
            gatewayPaymentStatus);

    private static CsobGatewayException CreateInitializationConflictException(
        Payment payment,
        int gatewayPaymentStatus) =>
        new CsobPaymentRequiresAttentionException(
            $"Ověřený stav ČSOB '{gatewayPaymentStatus}' nelze bezpečně " +
            $"aplikovat na nejasnou inicializaci platby '{payment.Id}'.",
            gatewayPaymentStatus);

    private static CsobGatewayException CreateProviderReferenceConflictException(
        Payment payment,
        string payId) =>
        new CsobPaymentRequiresAttentionException(
            $"ČSOB payId '{payId}' neodpovídá lokální provider vazbě " +
            $"platby '{payment.Id}'.");

    private static CsobGatewayException CreateUnsupportedLifecycleException(
        Payment payment,
        int gatewayPaymentStatus) =>
        new CsobPaymentRequiresAttentionException(
            $"Ověřený stav ČSOB '{gatewayPaymentStatus}' pro platbu " +
            $"'{payment.Id}' vyžaduje samostatnou reverse/refund " +
            "rekonciliaci a nebude automaticky měnit finanční stav.",
            gatewayPaymentStatus);
}
