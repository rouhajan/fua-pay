using FuaPay.Web.BuildingBlocks.Application;
using FuaPay.Web.BuildingBlocks.Auditing;
using FuaPay.Web.Modules.Jobs.Application;
using FuaPay.Web.Modules.Jobs.Domain;
using FuaPay.Web.Modules.Payments.Application;
using FuaPay.Web.Modules.Payments.Domain;

namespace FuaPay.Web.Modules.Payments.Infrastructure.Csob;

public sealed class CsobCardJobSettlementReturnService :
    ICardJobSettlementReturnService
{
    private const string AmbiguousReverseDiagnostic =
        "CSOB reverse outcome requires signed status recovery.";

    private const string AmbiguousStatusDiagnostic =
        "CSOB status does not yet prove the reverse outcome.";

    private const string NonReversibleDiagnostic =
        "CSOB payment is already in a non-reversible lifecycle state.";

    private readonly IJobRepository _jobRepository;
    private readonly IJobPaymentCoordination _jobPaymentCoordination;
    private readonly IPaymentRepository _paymentRepository;
    private readonly ISettlementReturnRepository _returnRepository;
    private readonly ISettlementReturnProviderAttemptRepository
        _attemptRepository;
    private readonly SettlementReturnRegistrationService
        _registrationService;
    private readonly SettlementReturnProviderAttemptService _attemptService;
    private readonly IApplicationTransaction _transaction;
    private readonly IAuditTrail _auditTrail;
    private readonly ICsobGatewayClient _gatewayClient;
    private readonly TimeProvider _timeProvider;

    public CsobCardJobSettlementReturnService(
        IJobRepository jobRepository,
        IJobPaymentCoordination jobPaymentCoordination,
        IPaymentRepository paymentRepository,
        ISettlementReturnRepository returnRepository,
        ISettlementReturnProviderAttemptRepository attemptRepository,
        SettlementReturnRegistrationService registrationService,
        SettlementReturnProviderAttemptService attemptService,
        IApplicationTransaction transaction,
        IAuditTrail auditTrail,
        ICsobGatewayClient gatewayClient,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(jobRepository);
        ArgumentNullException.ThrowIfNull(jobPaymentCoordination);
        ArgumentNullException.ThrowIfNull(paymentRepository);
        ArgumentNullException.ThrowIfNull(returnRepository);
        ArgumentNullException.ThrowIfNull(attemptRepository);
        ArgumentNullException.ThrowIfNull(registrationService);
        ArgumentNullException.ThrowIfNull(attemptService);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(auditTrail);
        ArgumentNullException.ThrowIfNull(gatewayClient);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _jobRepository = jobRepository;
        _jobPaymentCoordination = jobPaymentCoordination;
        _paymentRepository = paymentRepository;
        _returnRepository = returnRepository;
        _attemptRepository = attemptRepository;
        _registrationService = registrationService;
        _attemptService = attemptService;
        _transaction = transaction;
        _auditTrail = auditTrail;
        _gatewayClient = gatewayClient;
        _timeProvider = timeProvider;
    }

    public async Task<CardJobSettlementReturnResult> ReturnAsync(
        CardJobSettlementReturnCommand command,
        CancellationToken cancellationToken = default)
    {
        ValidateCommand(command);

        Preparation preparation;

        try
        {
            preparation = await _transaction.ExecuteAsync(
                transactionCancellationToken => PrepareAsync(
                    command,
                    transactionCancellationToken),
                cancellationToken);
        }
        catch (Exception exception) when (
            exception is SettlementReturnConcurrencyException or
                SettlementReturnProviderAttemptConcurrencyException)
        {
            preparation = await _transaction.ExecuteAsync(
                transactionCancellationToken =>
                    LoadAfterPreparationRaceAsync(
                        command.OperationId,
                        command.AdministratorUserId,
                        transactionCancellationToken),
                cancellationToken);
        }

        return preparation.Disposition switch
        {
            PreparationDisposition.SendReverse =>
                await SendReverseAsync(
                    command.OperationId,
                    preparation,
                    cancellationToken),
            PreparationDisposition.RecoverByStatus =>
                await RecoverByStatusAsync(
                    command.OperationId,
                    preparation,
                    cancellationToken),
            PreparationDisposition.Confirmed =>
                CreateResult(
                    preparation,
                    CardJobSettlementReturnOutcome.Confirmed,
                    reverseRequestSent: false),
            PreparationDisposition.Rejected =>
                CreateResult(
                    preparation,
                    CardJobSettlementReturnOutcome.ReverseRejected,
                    reverseRequestSent: false),
            _ => throw Inconsistent(
                command.OperationId,
                "unsupported preparation disposition")
        };
    }

    private async Task<Preparation> PrepareAsync(
        CardJobSettlementReturnCommand command,
        CancellationToken cancellationToken)
    {
        var existingReturn = await _returnRepository.FindByRequestIdAsync(
            command.OperationId,
            cancellationToken);
        var originalPaymentId = existingReturn?.OriginalPaymentId
            ?? command.OriginalPaymentId;

        if (
            existingReturn is not null &&
            existingReturn.OriginalPaymentId != command.OriginalPaymentId)
        {
            throw new SettlementReturnRequestConflictException(
                command.OperationId);
        }

        var payment = await _paymentRepository.FindByIdAsync(
            originalPaymentId,
            cancellationToken)
            ?? throw NotAllowed(
                originalPaymentId,
                "the authoritative original payment does not exist");

        ValidatePayment(payment);

        var jobId = payment.JobId!.Value;
        var wasLocked = await _jobPaymentCoordination.LockJobAsync(
            jobId,
            cancellationToken);

        if (!wasLocked)
        {
            throw NotAllowed(
                payment.Id,
                "the authoritative job does not exist");
        }

        var job = await _jobRepository.FindByIdAsync(
            jobId,
            cancellationToken)
            ?? throw NotAllowed(
                payment.Id,
                "the authoritative job does not exist");

        ValidateJob(payment, job);

        SettlementReturn settlementReturn;

        if (existingReturn is null)
        {
            var candidate = new SettlementReturn(
                Guid.NewGuid(),
                command.OperationId,
                SettlementReturnKind.CardJob,
                payment.Id,
                job.Id,
                payment.CustomerUserId,
                command.AdministratorUserId,
                payment.Amount,
                command.Reason,
                _timeProvider.GetUtcNow());
            var registration = await _registrationService.RegisterAsync(
                candidate,
                cancellationToken);
            settlementReturn = registration.SettlementReturn;
        }
        else
        {
            settlementReturn = existingReturn;
        }

        ValidateReturn(payment, job, settlementReturn);

        var creation = await _attemptService.CreateAsync(
            new CreateSettlementReturnProviderAttemptCommand(
                command.OperationId,
                settlementReturn.Id,
                SettlementReturnProviderOperation.Reverse),
            cancellationToken);
        var attempt = creation.Attempt;

        ValidateAttempt(settlementReturn, payment, attempt);

        return await ResolvePreparationAsync(
            settlementReturn,
            attempt,
            command.AdministratorUserId,
            cancellationToken);
    }

    private async Task<Preparation> ResolvePreparationAsync(
        SettlementReturn settlementReturn,
        SettlementReturnProviderAttempt attempt,
        Guid administratorActorUserId,
        CancellationToken cancellationToken)
    {
        if (
            attempt.State ==
                SettlementReturnProviderAttemptState.Confirmed &&
            settlementReturn.State == SettlementReturnState.Completed)
        {
            return CreatePreparation(
                settlementReturn,
                attempt,
                administratorActorUserId,
                PreparationDisposition.Confirmed);
        }

        if (
            attempt.State ==
                SettlementReturnProviderAttemptState.Rejected &&
            settlementReturn.State ==
                SettlementReturnState.RequiresAttention)
        {
            return CreatePreparation(
                settlementReturn,
                attempt,
                administratorActorUserId,
                PreparationDisposition.Rejected);
        }

        if (
            attempt.State ==
                SettlementReturnProviderAttemptState.Prepared &&
            settlementReturn.State is
                SettlementReturnState.Requested or
                SettlementReturnState.InProgress)
        {
            var changedAt = _timeProvider.GetUtcNow();

            if (settlementReturn.State == SettlementReturnState.Requested)
            {
                settlementReturn.Begin(changedAt);
            }

            _auditTrail.Stage(CreateAudit(
                settlementReturn,
                administratorActorUserId,
                "settlement-return.card-job.reverse-started",
                "CSOB reverse became eligible after durable InProgress " +
                "persistence.",
                changedAt));

            attempt = await _attemptService.BeginAsync(
                attempt.Id,
                cancellationToken);

            if (settlementReturn.State == SettlementReturnState.InProgress)
            {
                await _returnRepository.SaveAsync(
                    settlementReturn,
                    cancellationToken);
            }

            return CreatePreparation(
                settlementReturn,
                attempt,
                administratorActorUserId,
                PreparationDisposition.SendReverse);
        }

        if (
            attempt.State is
                SettlementReturnProviderAttemptState.InProgress or
                SettlementReturnProviderAttemptState.Uncertain &&
            settlementReturn.State is
                SettlementReturnState.InProgress or
                SettlementReturnState.RequiresAttention)
        {
            if (
                attempt.State ==
                    SettlementReturnProviderAttemptState.Uncertain &&
                settlementReturn.State == SettlementReturnState.InProgress)
            {
                settlementReturn.RequireAttention(_timeProvider.GetUtcNow());
                await _returnRepository.SaveAsync(
                    settlementReturn,
                    cancellationToken);
            }

            return CreatePreparation(
                settlementReturn,
                attempt,
                administratorActorUserId,
                PreparationDisposition.RecoverByStatus);
        }

        throw Inconsistent(
            settlementReturn.RequestId,
            $"return {settlementReturn.State} and Reverse attempt " +
            $"{attempt.State} cannot be resumed");
    }

    private async Task<Preparation> LoadAfterPreparationRaceAsync(
        Guid operationId,
        Guid administratorActorUserId,
        CancellationToken cancellationToken)
    {
        var settlementReturn = await _returnRepository.FindByRequestIdAsync(
            operationId,
            cancellationToken)
            ?? throw Inconsistent(
                operationId,
                "the operation disappeared after a concurrency conflict");
        var attempt = await _attemptRepository.FindByIdAsync(
            operationId,
            cancellationToken)
            ?? throw Inconsistent(
                operationId,
                "the Reverse attempt disappeared after a concurrency conflict");

        return await ResolvePreparationAsync(
            settlementReturn,
            attempt,
            administratorActorUserId,
            cancellationToken);
    }

    private async Task<CardJobSettlementReturnResult> SendReverseAsync(
        Guid operationId,
        Preparation preparation,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await _gatewayClient.ReverseAsync(
                preparation.ProviderReference,
                cancellationToken);

            return await ApplyReverseResponseAsync(
                operationId,
                preparation,
                response,
                cancellationToken);
        }
        catch (OperationCanceledException exception)
        {
            await PersistSafetyStateOrThrowAsync(
                operationId,
                preparation,
                AmbiguousReverseDiagnostic,
                reverseRequestSent: true,
                exception);
            throw;
        }
        catch (Exception exception)
        {
            return await PersistSafetyStateOrThrowAsync(
                operationId,
                preparation,
                AmbiguousReverseDiagnostic,
                reverseRequestSent: true,
                exception);
        }
    }

    private async Task<CardJobSettlementReturnResult> RecoverByStatusAsync(
        Guid operationId,
        Preparation preparation,
        CancellationToken cancellationToken)
    {
        try
        {
            var status = await _gatewayClient.GetStatusAsync(
                preparation.ProviderReference,
                cancellationToken);

            if (status.ResultCode == 0 && status.PaymentStatus == 5)
            {
                return await ConfirmAsync(
                    operationId,
                    preparation,
                    reverseRequestSent: false,
                    cancellationToken);
            }

            if (
                status.ResultCode == 0 &&
                IsDefinitivelyNonReversible(status.PaymentStatus))
            {
                return await RejectAsync(
                    operationId,
                    preparation,
                    reverseRequestSent: false,
                    cancellationToken);
            }

            return await MarkUncertainAsync(
                operationId,
                preparation,
                AmbiguousStatusDiagnostic,
                reverseRequestSent: false,
                cancellationToken);
        }
        catch (OperationCanceledException exception)
        {
            await PersistSafetyStateOrThrowAsync(
                operationId,
                preparation,
                AmbiguousStatusDiagnostic,
                reverseRequestSent: false,
                exception);
            throw;
        }
        catch (Exception exception)
        {
            return await PersistSafetyStateOrThrowAsync(
                operationId,
                preparation,
                AmbiguousStatusDiagnostic,
                reverseRequestSent: false,
                exception);
        }
    }

    private Task<CardJobSettlementReturnResult> ApplyReverseResponseAsync(
        Guid operationId,
        Preparation preparation,
        CsobPaymentReverseResult response,
        CancellationToken cancellationToken)
    {
        if (response.ResultCode == 0 && response.PaymentStatus == 5)
        {
            return ConfirmAsync(
                operationId,
                preparation,
                reverseRequestSent: true,
                cancellationToken);
        }

        if (IsDefinitivelyNonReversible(response.PaymentStatus))
        {
            return RejectAsync(
                operationId,
                preparation,
                reverseRequestSent: true,
                cancellationToken);
        }

        return MarkUncertainAsync(
            operationId,
            preparation,
            AmbiguousReverseDiagnostic,
            reverseRequestSent: true,
            cancellationToken);
    }

    private Task<CardJobSettlementReturnResult> ConfirmAsync(
        Guid operationId,
        Preparation preparation,
        bool reverseRequestSent,
        CancellationToken cancellationToken)
    {
        return _transaction.ExecuteAsync(
            transactionCancellationToken => ChangeStateAsync(
                operationId,
                preparation,
                StateResolution.Confirm,
                diagnostic: null,
                reverseRequestSent,
                transactionCancellationToken),
            cancellationToken);
    }

    private Task<CardJobSettlementReturnResult> RejectAsync(
        Guid operationId,
        Preparation preparation,
        bool reverseRequestSent,
        CancellationToken cancellationToken)
    {
        return _transaction.ExecuteAsync(
            transactionCancellationToken => ChangeStateAsync(
                operationId,
                preparation,
                StateResolution.Reject,
                NonReversibleDiagnostic,
                reverseRequestSent,
                transactionCancellationToken),
            cancellationToken);
    }

    private Task<CardJobSettlementReturnResult> MarkUncertainAsync(
        Guid operationId,
        Preparation preparation,
        string diagnostic,
        bool reverseRequestSent,
        CancellationToken cancellationToken)
    {
        return _transaction.ExecuteAsync(
            transactionCancellationToken => ChangeStateAsync(
                operationId,
                preparation,
                StateResolution.MarkUncertain,
                diagnostic,
                reverseRequestSent,
                transactionCancellationToken),
            cancellationToken);
    }

    private async Task<CardJobSettlementReturnResult> ChangeStateAsync(
        Guid operationId,
        Preparation preparation,
        StateResolution resolution,
        string? diagnostic,
        bool reverseRequestSent,
        CancellationToken cancellationToken)
    {
        var settlementReturn = await _returnRepository.FindByIdAsync(
            preparation.SettlementReturnId,
            cancellationToken)
            ?? throw Inconsistent(
                operationId,
                "the SettlementReturn no longer exists");
        var attempt = await _attemptRepository.FindByIdAsync(
            preparation.ProviderAttemptId,
            cancellationToken)
            ?? throw Inconsistent(
                operationId,
                "the Reverse attempt no longer exists");

        ValidateAttemptForChange(
            operationId,
            preparation,
            settlementReturn,
            attempt);

        if (
            attempt.State ==
                SettlementReturnProviderAttemptState.Confirmed &&
            settlementReturn.State == SettlementReturnState.Completed)
        {
            return CreateResult(
                preparation,
                CardJobSettlementReturnOutcome.Confirmed,
                reverseRequestSent);
        }

        if (
            attempt.State ==
                SettlementReturnProviderAttemptState.Rejected &&
            settlementReturn.State ==
                SettlementReturnState.RequiresAttention)
        {
            return CreateResult(
                preparation,
                CardJobSettlementReturnOutcome.ReverseRejected,
                reverseRequestSent);
        }

        return resolution switch
        {
            StateResolution.Confirm => await ConfirmInsideTransactionAsync(
                operationId,
                preparation,
                settlementReturn,
                attempt,
                reverseRequestSent,
                cancellationToken),
            StateResolution.Reject => await RejectInsideTransactionAsync(
                operationId,
                preparation,
                settlementReturn,
                attempt,
                diagnostic!,
                reverseRequestSent,
                cancellationToken),
            StateResolution.MarkUncertain =>
                await MarkUncertainInsideTransactionAsync(
                    operationId,
                    preparation,
                    settlementReturn,
                    attempt,
                    diagnostic!,
                    reverseRequestSent,
                    cancellationToken),
            _ => throw Inconsistent(
                operationId,
                "unsupported state resolution")
        };
    }

    private async Task<CardJobSettlementReturnResult>
        ConfirmInsideTransactionAsync(
            Guid operationId,
            Preparation preparation,
            SettlementReturn settlementReturn,
            SettlementReturnProviderAttempt attempt,
            bool reverseRequestSent,
            CancellationToken cancellationToken)
    {
        if (
            attempt.State is not
                SettlementReturnProviderAttemptState.InProgress and not
                SettlementReturnProviderAttemptState.Uncertain ||
            settlementReturn.State is not
                SettlementReturnState.InProgress and not
                SettlementReturnState.RequiresAttention)
        {
            throw Inconsistent(
                operationId,
                "confirmed gateway evidence cannot resolve the current states");
        }

        var changedAt = _timeProvider.GetUtcNow();
        settlementReturn.Complete(changedAt);
        _auditTrail.Stage(CreateAudit(
            settlementReturn,
            preparation.AdministratorActorUserId,
            "settlement-return.card-job.reverse-confirmed",
            "Signed CSOB evidence confirmed resultCode 0 and paymentStatus 5.",
            changedAt));
        await _attemptService.ConfirmAsync(
            attempt.Id,
            cancellationToken);
        await _returnRepository.SaveAsync(
            settlementReturn,
            cancellationToken);

        return CreateResult(
            preparation,
            CardJobSettlementReturnOutcome.Confirmed,
            reverseRequestSent);
    }

    private async Task<CardJobSettlementReturnResult>
        RejectInsideTransactionAsync(
            Guid operationId,
            Preparation preparation,
            SettlementReturn settlementReturn,
            SettlementReturnProviderAttempt attempt,
            string diagnostic,
            bool reverseRequestSent,
            CancellationToken cancellationToken)
    {
        if (
            attempt.State is not
                SettlementReturnProviderAttemptState.InProgress and not
                SettlementReturnProviderAttemptState.Uncertain ||
            settlementReturn.State is not
                SettlementReturnState.InProgress and not
                SettlementReturnState.RequiresAttention)
        {
            throw Inconsistent(
                operationId,
                "non-reversible gateway evidence cannot resolve the current states");
        }

        var changedAt = _timeProvider.GetUtcNow();

        if (settlementReturn.State == SettlementReturnState.InProgress)
        {
            settlementReturn.RequireAttention(changedAt);
        }

        _auditTrail.Stage(CreateAudit(
            settlementReturn,
            preparation.AdministratorActorUserId,
            "settlement-return.card-job.reverse-rejected",
            "Signed CSOB evidence proved a non-reversible state; Refund " +
            "remains a separate operator decision.",
            changedAt));
        await _attemptService.RejectAsync(
            attempt.Id,
            diagnostic,
            cancellationToken);
        await _returnRepository.SaveAsync(
            settlementReturn,
            cancellationToken);

        return CreateResult(
            preparation,
            CardJobSettlementReturnOutcome.ReverseRejected,
            reverseRequestSent);
    }

    private async Task<CardJobSettlementReturnResult>
        MarkUncertainInsideTransactionAsync(
            Guid operationId,
            Preparation preparation,
            SettlementReturn settlementReturn,
            SettlementReturnProviderAttempt attempt,
            string diagnostic,
            bool reverseRequestSent,
            CancellationToken cancellationToken)
    {
        if (
            attempt.State ==
                SettlementReturnProviderAttemptState.Uncertain &&
            settlementReturn.State ==
                SettlementReturnState.RequiresAttention)
        {
            return CreateResult(
                preparation,
                CardJobSettlementReturnOutcome.RequiresAttention,
                reverseRequestSent);
        }

        if (
            attempt.State !=
                SettlementReturnProviderAttemptState.InProgress ||
            settlementReturn.State is not
                SettlementReturnState.InProgress and not
                SettlementReturnState.RequiresAttention)
        {
            throw Inconsistent(
                operationId,
                "an ambiguous provider outcome cannot resolve the current states");
        }

        var changedAt = _timeProvider.GetUtcNow();

        if (settlementReturn.State == SettlementReturnState.InProgress)
        {
            settlementReturn.RequireAttention(changedAt);
        }

        _auditTrail.Stage(CreateAudit(
            settlementReturn,
            preparation.AdministratorActorUserId,
            "settlement-return.card-job.reverse-requires-attention",
            "CSOB reverse outcome is ambiguous; all future automatic " +
            "recovery is status-only.",
            changedAt));
        await _attemptService.MarkUncertainAsync(
            attempt.Id,
            diagnostic,
            cancellationToken);
        await _returnRepository.SaveAsync(
            settlementReturn,
            cancellationToken);

        return CreateResult(
            preparation,
            CardJobSettlementReturnOutcome.RequiresAttention,
            reverseRequestSent);
    }

    private async Task<CardJobSettlementReturnResult>
        PersistSafetyStateOrThrowAsync(
            Guid operationId,
            Preparation preparation,
            string diagnostic,
            bool reverseRequestSent,
            Exception providerOrPersistenceException)
    {
        try
        {
            return await MarkUncertainAsync(
                operationId,
                preparation,
                diagnostic,
                reverseRequestSent,
                CancellationToken.None);
        }
        catch (Exception safetyException)
        {
            throw new CardJobSettlementReturnSafetyStateException(
                operationId,
                new AggregateException(
                    providerOrPersistenceException,
                    safetyException));
        }
    }

    private static void ValidatePayment(Payment payment)
    {
        if (
            payment.Provider != PaymentProvider.Csob ||
            payment.PurposeType != PaymentPurposeType.Job ||
            !payment.JobId.HasValue ||
            payment.Status != PaymentStatus.Succeeded ||
            payment.ProviderReference is null)
        {
            throw NotAllowed(
                payment.Id,
                "only a successfully settled CSOB CardJob payment with a " +
                "provider reference is supported");
        }
    }

    private static void ValidateJob(Payment payment, Job job)
    {
        if (
            job.Id != payment.JobId ||
            job.PaymentStatus != JobPaymentStatus.Paid ||
            job.SettlementType != JobSettlementType.DirectPayment ||
            job.SettlementReferenceId != payment.Id ||
            !job.SettledAt.HasValue ||
            job.CustomerUserId != payment.CustomerUserId ||
            job.Price != payment.Amount)
        {
            throw NotAllowed(
                payment.Id,
                "the authoritative paid job does not match the original " +
                "CardJob payment");
        }
    }

    private static void ValidateReturn(
        Payment payment,
        Job job,
        SettlementReturn settlementReturn)
    {
        if (
            settlementReturn.Kind != SettlementReturnKind.CardJob ||
            settlementReturn.OriginalPaymentId != payment.Id ||
            settlementReturn.JobId != job.Id ||
            settlementReturn.CustomerUserId != payment.CustomerUserId ||
            settlementReturn.Amount != payment.Amount)
        {
            throw Inconsistent(
                settlementReturn.RequestId,
                "the SettlementReturn does not match its authoritative " +
                "payment and job");
        }
    }

    private static void ValidateAttempt(
        SettlementReturn settlementReturn,
        Payment payment,
        SettlementReturnProviderAttempt attempt)
    {
        if (
            attempt.SettlementReturnId != settlementReturn.Id ||
            attempt.Provider != PaymentProvider.Csob ||
            attempt.Operation != SettlementReturnProviderOperation.Reverse ||
            !string.Equals(
                attempt.ProviderReference,
                payment.ProviderReference,
                StringComparison.Ordinal))
        {
            throw Inconsistent(
                settlementReturn.RequestId,
                "the provider attempt does not match the authoritative " +
                "CSOB payment");
        }
    }

    private static void ValidateAttemptForChange(
        Guid operationId,
        Preparation preparation,
        SettlementReturn settlementReturn,
        SettlementReturnProviderAttempt attempt)
    {
        if (
            settlementReturn.RequestId != operationId ||
            settlementReturn.Id != preparation.SettlementReturnId ||
            settlementReturn.Kind != SettlementReturnKind.CardJob ||
            attempt.Id != preparation.ProviderAttemptId ||
            attempt.SettlementReturnId != settlementReturn.Id ||
            attempt.Provider != PaymentProvider.Csob ||
            attempt.Operation != SettlementReturnProviderOperation.Reverse ||
            !string.Equals(
                attempt.ProviderReference,
                preparation.ProviderReference,
                StringComparison.Ordinal))
        {
            throw Inconsistent(
                operationId,
                "the persisted operation identity changed");
        }
    }

    private static bool IsDefinitivelyNonReversible(int paymentStatus) =>
        paymentStatus is 8 or 9 or 10;

    private static Preparation CreatePreparation(
        SettlementReturn settlementReturn,
        SettlementReturnProviderAttempt attempt,
        Guid administratorActorUserId,
        PreparationDisposition disposition) =>
        new(
            settlementReturn.Id,
            attempt.Id,
            attempt.ProviderReference,
            administratorActorUserId,
            disposition);

    private static CardJobSettlementReturnResult CreateResult(
        Preparation preparation,
        CardJobSettlementReturnOutcome outcome,
        bool reverseRequestSent) =>
        new(
            preparation.SettlementReturnId,
            preparation.ProviderAttemptId,
            outcome,
            reverseRequestSent);

    private static AuditEntry CreateAudit(
        SettlementReturn settlementReturn,
        Guid administratorActorUserId,
        string action,
        string outcome,
        DateTimeOffset occurredAt) =>
        AuditEntry.ForUser(
            administratorActorUserId,
            action,
            "settlement-return",
            settlementReturn.Id.ToString(),
            $"CardJob {settlementReturn.JobId}; payment " +
            $"{settlementReturn.OriginalPaymentId}; customer " +
            $"{settlementReturn.CustomerUserId}; " +
            $"{settlementReturn.Amount.MinorUnits} CZK minor units. " +
            outcome,
            occurredAt);

    private static void ValidateCommand(
        CardJobSettlementReturnCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateId(command.OperationId, nameof(command.OperationId));
        ValidateId(
            command.OriginalPaymentId,
            nameof(command.OriginalPaymentId));
        ValidateId(
            command.AdministratorUserId,
            nameof(command.AdministratorUserId));

        if (string.IsNullOrWhiteSpace(command.Reason))
        {
            throw new ArgumentException(
                "Settlement return reason must not be blank.",
                nameof(command));
        }

        if (command.Reason.Trim().Length > SettlementReturn.MaximumReasonLength)
        {
            throw new ArgumentException(
                "Settlement return reason is too long.",
                nameof(command));
        }
    }

    private static void ValidateId(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException(
                "Card-job return operation ID must not be empty.",
                parameterName);
        }
    }

    private static CardJobSettlementReturnNotAllowedException NotAllowed(
        Guid originalPaymentId,
        string reason) =>
        new(originalPaymentId, reason);

    private static CardJobSettlementReturnStateInconsistentException
        Inconsistent(
            Guid operationId,
            string reason) =>
        new(operationId, reason);

    private enum PreparationDisposition
    {
        Unknown = 0,
        SendReverse = 1,
        RecoverByStatus = 2,
        Confirmed = 3,
        Rejected = 4
    }

    private enum StateResolution
    {
        Unknown = 0,
        Confirm = 1,
        Reject = 2,
        MarkUncertain = 3
    }

    private sealed record Preparation(
        Guid SettlementReturnId,
        Guid ProviderAttemptId,
        string ProviderReference,
        Guid AdministratorActorUserId,
        PreparationDisposition Disposition);
}
