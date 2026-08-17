using FuaPay.Web.BuildingBlocks.Application;
using FuaPay.Web.BuildingBlocks.Auditing;
using FuaPay.Web.Modules.Payments.Domain;

namespace FuaPay.Web.Modules.Payments.Application;

public sealed class PaymentInitiationService
{
    private const string UncertainReason =
        "Výsledek zahájení platby u poskytovatele nebyl lokálně potvrzen.";
    private const string CandidatePendingVerificationReason =
        "Provider reference byla durabilně zachycena a čeká na serverové ověření stavu.";

    private readonly IPaymentRepository _paymentRepository;
    private readonly IPaymentInitiationRepository _initiationRepository;
    private readonly IPaymentProviderInitiator _providerInitiator;
    private readonly IApplicationTransaction _transaction;
    private readonly TimeProvider _timeProvider;
    private readonly IAuditTrail _auditTrail;

    public PaymentInitiationService(
        IPaymentRepository paymentRepository,
        IPaymentInitiationRepository initiationRepository,
        IPaymentProviderInitiator providerInitiator,
        IApplicationTransaction transaction,
        TimeProvider timeProvider,
        IAuditTrail auditTrail)
    {
        ArgumentNullException.ThrowIfNull(paymentRepository);
        ArgumentNullException.ThrowIfNull(initiationRepository);
        ArgumentNullException.ThrowIfNull(providerInitiator);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(auditTrail);

        _paymentRepository = paymentRepository;
        _initiationRepository = initiationRepository;
        _providerInitiator = providerInitiator;
        _transaction = transaction;
        _timeProvider = timeProvider;
        _auditTrail = auditTrail;
    }

    public async Task<PaymentInitializationOutcome> InitializeIfPreparedAsync(
        Payment payment,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(payment);

        var initiation = await _initiationRepository.FindByPaymentIdAsync(
            payment.Id,
            cancellationToken);

        if (
            initiation is null ||
            initiation.State != PaymentInitiationState.Prepared)
        {
            return new PaymentInitializationOutcome(
                payment,
                initiation?.ProcessUri);
        }

        return await InitializeAsync(
            payment.Id,
            cancellationToken);
    }

    public async Task<PaymentInitializationOutcome> InitializeAsync(
        Guid paymentId,
        CancellationToken cancellationToken = default)
    {
        if (paymentId == Guid.Empty)
        {
            throw new ArgumentException(
                "ID platby nesmí být prázdné.",
                nameof(paymentId));
        }

        _providerInitiator.EnsureAvailable();

        var payment = await _paymentRepository.FindByIdAsync(
            paymentId,
            cancellationToken)
            ?? throw new InvalidDataException(
                $"Platba '{paymentId}' pro inicializaci neexistuje.");
        var initiation = await _initiationRepository.FindByPaymentIdAsync(
            paymentId,
            cancellationToken)
            ?? throw new InvalidDataException(
                $"Platba '{paymentId}' nemá persistovanou inicializaci.");

        EnsureProviderMatches(payment, initiation);

        if (initiation.State != PaymentInitiationState.Prepared)
        {
            return new PaymentInitializationOutcome(
                payment,
                initiation.ProcessUri);
        }

        var startedAt = _timeProvider.GetUtcNow();
        initiation.Begin(startedAt);
        StageAudit(
            payment,
            "payment.provider-initiation.started",
            $"Zahájení platby {payment.Id} u poskytovatele " +
            $"{payment.Provider} bylo spuštěno s orderNo " +
            $"{initiation.OrderNumber}.",
            startedAt);
        try
        {
            await _initiationRepository.SaveAsync(
                initiation,
                cancellationToken);
        }
        catch (PaymentConcurrencyException)
        {
            return await ResolveConcurrentClaimAsync(
                payment.Id,
                cancellationToken);
        }

        var request = new PaymentProviderInitializationRequest(
            payment.Id,
            payment.Provider,
            initiation.OrderNumber,
            initiation.CorrelationId,
            payment.PurposeType,
            payment.JobId,
            payment.Amount);

        PaymentProviderInitializationResult providerResult;

        try
        {
            providerResult = await _providerInitiator.InitializeAsync(
                request,
                cancellationToken);
        }
        catch (PaymentProviderInitializationUncertainException exception)
        {
            if (exception.ObservedResult.Provider != payment.Provider)
            {
                await MarkUncertainAsync(
                    payment.Id,
                    observedProviderReference: null,
                    observedProcessUri: null,
                    cancellationToken: CancellationToken.None);
                throw new InvalidOperationException(
                    "Provider observation neodpovídá inicializované platbě.",
                    exception);
            }

            await PersistCandidateObservationAsync(
                payment.Id,
                exception.ObservedResult,
                cancellationToken: CancellationToken.None);
            throw;
        }
        catch
        {
            await MarkUncertainAsync(
                payment.Id,
                observedProviderReference: null,
                observedProcessUri: null,
                cancellationToken: CancellationToken.None);
            throw;
        }

        if (providerResult.Provider != payment.Provider)
        {
            await MarkUncertainAsync(
                payment.Id,
                observedProviderReference: null,
                observedProcessUri: null,
                cancellationToken: CancellationToken.None);

            throw new InvalidOperationException(
                "Poskytovatel v odpovědi inicializace neodpovídá platbě.");
        }

        await PersistCandidateObservationAsync(
            payment.Id,
            providerResult,
            CancellationToken.None);

        await _providerInitiator.VerifyAsync(
            providerResult,
            cancellationToken);

        try
        {
            return await _transaction.ExecuteAsync(
                async transactionCancellationToken =>
                {
                    var currentPayment = await _paymentRepository.FindByIdAsync(
                        payment.Id,
                        transactionCancellationToken)
                        ?? throw new InvalidDataException(
                            $"Platba '{payment.Id}' během inicializace zmizela.");
                    var currentInitiation =
                        await _initiationRepository.FindByPaymentIdAsync(
                            payment.Id,
                            transactionCancellationToken)
                        ?? throw new InvalidDataException(
                            $"Inicializace platby '{payment.Id}' během zpracování zmizela.");

                    EnsureProviderMatches(
                        currentPayment,
                        currentInitiation);

                    if (
                        currentInitiation.State ==
                            PaymentInitiationState.Initialized)
                    {
                        if (
                            !string.Equals(
                                currentPayment.ProviderReference,
                                providerResult.ProviderReference,
                                StringComparison.Ordinal))
                        {
                            throw new InvalidOperationException(
                                "Souběžně dokončená inicializace obsahuje konfliktní provider reference.");
                        }

                        return new PaymentInitializationOutcome(
                            currentPayment,
                            currentInitiation.ProcessUri);
                    }

                    if (currentPayment.Status != PaymentStatus.Created)
                    {
                        throw new InvalidOperationException(
                            "Inicializace platby změnila stav během volání poskytovatele; výsledek nelze bezpečně aplikovat.");
                    }

                    var completedAt = _timeProvider.GetUtcNow();

                    if (
                        currentInitiation.State ==
                            PaymentInitiationState.Uncertain)
                    {
                        currentInitiation.RecordObservedProviderResult(
                            providerResult.ProviderReference,
                            providerResult.ProcessUri,
                            completedAt);
                    }
                    else if (
                        currentInitiation.State !=
                            PaymentInitiationState.InProgress)
                    {
                        throw new InvalidOperationException(
                            "Inicializace platby změnila stav během volání poskytovatele; výsledek nelze bezpečně aplikovat.");
                    }

                    currentPayment.MarkPending(
                        providerResult.ProviderReference,
                        completedAt);

                    if (
                        currentInitiation.State ==
                            PaymentInitiationState.InProgress)
                    {
                        currentInitiation.Complete(
                            completedAt,
                            providerResult.ProcessUri);
                    }
                    else
                    {
                        currentInitiation.RecoverObservedInitialization(
                            completedAt);
                    }

                    StageAudit(
                        currentPayment,
                        "payment.provider-initialized",
                        $"Platba {currentPayment.Id} byla u poskytovatele " +
                        $"{currentPayment.Provider} inicializována s referencí " +
                        $"{providerResult.ProviderReference}.",
                        completedAt);

                    await _paymentRepository.SaveAsync(
                        currentPayment,
                        transactionCancellationToken);
                    await _initiationRepository.SaveAsync(
                        currentInitiation,
                        transactionCancellationToken);

                    return new PaymentInitializationOutcome(
                        currentPayment,
                        providerResult.ProcessUri);
                },
                cancellationToken);
        }
        catch
        {
            await MarkUncertainAsync(
                payment.Id,
                providerResult.ProviderReference,
                providerResult.ProcessUri,
                cancellationToken: CancellationToken.None);
            throw;
        }
    }

    private async Task<PaymentInitializationOutcome> ResolveConcurrentClaimAsync(
        Guid paymentId,
        CancellationToken cancellationToken)
    {
        var payment = await _paymentRepository.FindByIdAsync(
            paymentId,
            cancellationToken)
            ?? throw new PaymentConcurrencyException(paymentId);
        var initiation = await _initiationRepository.FindByPaymentIdAsync(
            paymentId,
            cancellationToken)
            ?? throw new PaymentConcurrencyException(paymentId);

        EnsureProviderMatches(payment, initiation);

        if (initiation.State == PaymentInitiationState.Prepared)
        {
            throw new PaymentConcurrencyException(paymentId);
        }

        return new PaymentInitializationOutcome(
            payment,
            initiation.ProcessUri);
    }

    private async Task MarkUncertainAsync(
        Guid paymentId,
        string? observedProviderReference,
        Uri? observedProcessUri,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var payment = await _paymentRepository.FindByIdAsync(
                paymentId,
                cancellationToken);
            var initiation = await _initiationRepository.FindByPaymentIdAsync(
                paymentId,
                cancellationToken);

            if (payment is null || initiation is null)
            {
                return;
            }

            var changedAt = _timeProvider.GetUtcNow();
            var transitionedToUncertain = false;

            if (initiation.State == PaymentInitiationState.InProgress)
            {
                initiation.MarkUncertain(
                    UncertainReason,
                    changedAt,
                    observedProviderReference,
                    observedProcessUri);
                transitionedToUncertain = true;
            }
            else if (
                initiation.State == PaymentInitiationState.Uncertain &&
                observedProviderReference is not null)
            {
                initiation.RecordObservedProviderResult(
                    observedProviderReference,
                    observedProcessUri,
                    changedAt);
            }
            else
            {
                return;
            }

            if (transitionedToUncertain)
            {
                StageAudit(
                    payment,
                    "payment.provider-initiation.uncertain",
                    $"Výsledek zahájení platby {payment.Id} u poskytovatele " +
                    $"{payment.Provider} je nejasný a vyžaduje reconciliation.",
                    changedAt);
            }

            try
            {
                await _initiationRepository.SaveAsync(
                    initiation,
                    cancellationToken);
                return;
            }
            catch (PaymentConcurrencyException)
                when (attempt < 2)
            {
            }
        }
    }

    private async Task PersistCandidateObservationAsync(
        Guid paymentId,
        PaymentProviderInitializationResult candidate,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var payment = await _paymentRepository.FindByIdAsync(
                paymentId,
                cancellationToken)
                ?? throw new InvalidDataException(
                    $"Platba '{paymentId}' pro uložení provider observation neexistuje.");
            var initiation = await _initiationRepository.FindByPaymentIdAsync(
                paymentId,
                cancellationToken)
                ?? throw new InvalidDataException(
                    $"Inicializace platby '{paymentId}' pro uložení provider observation neexistuje.");

            EnsureProviderMatches(payment, initiation);

            if (candidate.Provider != payment.Provider)
            {
                throw new InvalidOperationException(
                    "Provider observation neodpovídá inicializované platbě.");
            }

            if (initiation.State == PaymentInitiationState.Initialized)
            {
                if (!string.Equals(
                    payment.ProviderReference,
                    candidate.ProviderReference,
                    StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "Dokončená inicializace obsahuje konfliktní provider reference.");
                }

                return;
            }

            var observedAt = _timeProvider.GetUtcNow();

            if (initiation.State == PaymentInitiationState.InProgress)
            {
                initiation.MarkUncertain(
                    CandidatePendingVerificationReason,
                    observedAt,
                    candidate.ProviderReference,
                    candidate.ProcessUri);
            }
            else if (initiation.State == PaymentInitiationState.Uncertain)
            {
                initiation.RecordObservedProviderResult(
                    candidate.ProviderReference,
                    candidate.ProcessUri,
                    observedAt);
            }
            else
            {
                throw new InvalidOperationException(
                    "Provider observation lze uložit pouze k probíhající nebo nejasné inicializaci.");
            }

            StageAudit(
                payment,
                "payment.provider-initiation.candidate-observed",
                $"Pro platbu {payment.Id} byla před ověřením stavu " +
                $"durabilně zachycena provider reference {candidate.ProviderReference}.",
                observedAt);

            try
            {
                await _initiationRepository.SaveAsync(
                    initiation,
                    cancellationToken);
                return;
            }
            catch (PaymentConcurrencyException)
                when (attempt < 2)
            {
            }
        }

        throw new PaymentConcurrencyException(paymentId);
    }

    private void EnsureProviderMatches(
        Payment payment,
        PaymentInitiation initiation)
    {
        if (
            payment.Id != initiation.PaymentId ||
            payment.Provider != initiation.Provider ||
            payment.Provider != _providerInitiator.Provider)
        {
            throw new InvalidOperationException(
                "Persistovaná inicializace, platba a aktivní poskytovatel nejsou konzistentní.");
        }
    }

    private void StageAudit(
        Payment payment,
        string action,
        string description,
        DateTimeOffset occurredAt)
    {
        _auditTrail.Stage(AuditEntry.ForProcess(
            "payment-orchestration",
            action,
            "payment",
            payment.Id.ToString(),
            description,
            occurredAt));
    }
}
