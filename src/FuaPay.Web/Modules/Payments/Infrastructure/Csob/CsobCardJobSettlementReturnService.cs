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
    internal const string ReverseAmbiguousDiagnostic =
        "CSOB reverse outcome requires signed status recovery.";
    internal const string ReverseStatusAmbiguousDiagnostic =
        "CSOB status does not yet prove the reverse outcome.";
    internal const string ReverseSettledDiagnostic =
        "Signed CSOB evidence proved paymentStatus 8; full refund started.";
    internal const string ReverseFoundRefundProcessingDiagnostic =
        CardJobSettlementReturnDiagnostics.PreExistingRefundProcessing;
    internal const string ReverseFoundReturnedDiagnostic =
        CardJobSettlementReturnDiagnostics.PreExistingReturned;
    internal const string RefundAmbiguousDiagnostic =
        "CSOB full-refund outcome requires signed status recovery.";
    internal const string RefundStatusAmbiguousDiagnostic =
        "CSOB status does not yet prove the full-refund outcome.";
    internal const string RefundProcessingDiagnostic =
        CardJobSettlementReturnDiagnostics.RefundProcessing;
    internal const string RefundReturnedWithoutFullProofDiagnostic =
        "Signed CSOB status 10 lacks documented machine-readable full-refund proof.";
    internal const string PartialRefundAmbiguousDiagnostic =
        "CSOB partial-refund outcome requires signed status recovery.";
    internal const string PartialRefundStatusAmbiguousDiagnostic =
        "CSOB status cannot prove the outcome of this partial-refund amount.";
    internal const string PartialRefundReturnedWithoutProofDiagnostic =
        "Signed CSOB status 10 cannot prove this specific partial-refund amount.";

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
            preparation = await PrepareTransactionAsync(
                command,
                cancellationToken);
        }
        catch (Exception exception) when (
            exception is SettlementReturnConcurrencyException or
                SettlementReturnProviderAttemptConcurrencyException)
        {
            preparation = await PrepareTransactionAsync(
                command,
                cancellationToken);
        }

        return await ContinueAsync(
            command.OperationId,
            preparation,
            cancellationToken);
    }

    public async Task<CardJobSettlementReturnResult> PartialRefundAsync(
        CardJobPartialRefundCommand command,
        CancellationToken cancellationToken = default)
    {
        ValidatePartialCommand(command);

        Preparation preparation;
        try
        {
            preparation = await _transaction.ExecuteAsync(
                ct => PreparePartialAsync(command, ct),
                cancellationToken);
        }
        catch (Exception exception) when (
            exception is SettlementReturnConcurrencyException or
                SettlementReturnProviderAttemptConcurrencyException)
        {
            preparation = await _transaction.ExecuteAsync(
                ct => PreparePartialAsync(command, ct),
                cancellationToken);
        }

        return await ContinueAsync(
            command.OperationId,
            preparation,
            cancellationToken);
    }

    private async Task<Preparation> PreparePartialAsync(
        CardJobPartialRefundCommand command,
        CancellationToken cancellationToken)
    {
        var payment = await _paymentRepository.FindByIdAsync(
            command.OriginalPaymentId,
            cancellationToken)
            ?? throw NotAllowed(
                command.OriginalPaymentId,
                "the authoritative original payment does not exist");
        ValidatePayment(payment);

        var jobId = payment.JobId!.Value;
        await LockJobOrThrowAsync(
            command.OperationId,
            jobId,
            cancellationToken);
        var job = await _jobRepository.FindByIdAsync(
            jobId,
            cancellationToken)
            ?? throw NotAllowed(
                payment.Id,
                "the authoritative job does not exist");
        ValidateJob(payment, job);

        var candidate = new SettlementReturn(
            Guid.NewGuid(),
            command.OperationId,
            SettlementReturnKind.CardJob,
            payment.Id,
            job.Id,
            payment.CustomerUserId,
            command.AdministratorUserId,
            new Money(command.AmountMinorUnits),
            command.Reason,
            _timeProvider.GetUtcNow());
        var existing = await _returnRepository.FindByRequestIdAsync(
            command.OperationId,
            cancellationToken);

        SettlementReturn settlementReturn;
        if (existing is null)
        {
            var returns = await _returnRepository
                .ListByOriginalPaymentIdAsync(
                    payment.Id,
                    cancellationToken);
            var reservedMinorUnits = SumReservedMinorUnits(returns);
            var remainingMinorUnits = checked(
                payment.Amount.MinorUnits - reservedMinorUnits);

            if (remainingMinorUnits <= 0 ||
                command.AmountMinorUnits >= remainingMinorUnits)
            {
                throw new CardJobPartialRefundAmountException(
                    payment.Id,
                    command.AmountMinorUnits,
                    Math.Max(0, remainingMinorUnits),
                    command.AmountMinorUnits == remainingMinorUnits
                        ? "CSOB documents amount equal to the remaining " +
                          "balance as a full refund, which is not safely " +
                          "available after partial refunds"
                        : "the amount is not below the safely available " +
                          "partial-refund ceiling");
            }

            settlementReturn = (await _registrationService.RegisterAsync(
                candidate,
                cancellationToken)).SettlementReturn;
        }
        else
        {
            settlementReturn = (await _registrationService.RegisterAsync(
                candidate,
                cancellationToken)).SettlementReturn;
        }

        ValidatePartialReturn(payment, job, settlementReturn);
        var history = await _attemptRepository.ListBySettlementReturnIdAsync(
            settlementReturn.Id,
            cancellationToken);

        if (history.Count == 0)
        {
            if (settlementReturn.State != SettlementReturnState.Requested)
            {
                throw Inconsistent(
                    command.OperationId,
                    "an unresolved partial refund has no provider attempt");
            }

            var created = await _attemptService.CreateAsync(
                new CreateSettlementReturnProviderAttemptCommand(
                    command.OperationId,
                    settlementReturn.Id,
                    SettlementReturnProviderOperation.Refund),
                cancellationToken);
            history = [created.Attempt];
        }

        ValidatePartialHistory(settlementReturn, payment, history);
        var attempt = history.Single();

        if (settlementReturn.State == SettlementReturnState.Completed)
        {
            return Preparation.For(
                settlementReturn,
                attempt,
                command.AdministratorUserId,
                Disposition.RefundCompleted,
                refundAmountMinorUnits: settlementReturn.Amount.MinorUnits);
        }

        if (settlementReturn.State == SettlementReturnState.Rejected)
        {
            return Preparation.For(
                settlementReturn,
                attempt,
                command.AdministratorUserId,
                Disposition.RefundRejected,
                refundAmountMinorUnits: settlementReturn.Amount.MinorUnits);
        }

        return await ResolveActiveAsync(
            settlementReturn,
            attempt,
            command.AdministratorUserId,
            cancellationToken,
            settlementReturn.Amount.MinorUnits);
    }

    private Task<Preparation> PrepareTransactionAsync(
        CardJobSettlementReturnCommand command,
        CancellationToken cancellationToken) =>
        _transaction.ExecuteAsync(
            ct => PrepareAsync(command, ct),
            cancellationToken);

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
        await LockJobOrThrowAsync(
            command.OperationId,
            jobId,
            cancellationToken);
        var job = await _jobRepository.FindByIdAsync(
            jobId,
            cancellationToken)
            ?? throw NotAllowed(
                payment.Id,
                "the authoritative job does not exist");
        ValidateJob(payment, job);

        existingReturn = await _returnRepository.FindByRequestIdAsync(
            command.OperationId,
            cancellationToken);
        if (
            existingReturn is not null &&
            existingReturn.OriginalPaymentId != command.OriginalPaymentId)
        {
            throw new SettlementReturnRequestConflictException(
                command.OperationId);
        }

        var otherReservedReturns = await _returnRepository
            .ListByOriginalPaymentIdAsync(payment.Id, cancellationToken);
        if (otherReservedReturns.Any(item =>
                item.Id != existingReturn?.Id &&
                item.Kind == SettlementReturnKind.CardJob &&
                item.State != SettlementReturnState.Rejected))
        {
            throw NotAllowed(
                payment.Id,
                "a full refund cannot be sent after another CardJob refund " +
                "has reserved or returned part of the payment");
        }

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
            settlementReturn = (await _registrationService.RegisterAsync(
                candidate,
                cancellationToken)).SettlementReturn;
        }
        else
        {
            settlementReturn = existingReturn;
        }

        ValidateReturn(payment, job, settlementReturn);
        var history = await _attemptRepository.ListBySettlementReturnIdAsync(
            settlementReturn.Id,
            cancellationToken);

        if (history.Count == 0)
        {
            if (settlementReturn.State != SettlementReturnState.Requested)
            {
                throw Inconsistent(
                    command.OperationId,
                    "an unresolved return has no provider-attempt history");
            }

            var created = await _attemptService.CreateAsync(
                new CreateSettlementReturnProviderAttemptCommand(
                    command.OperationId,
                    settlementReturn.Id,
                    SettlementReturnProviderOperation.Reverse),
                cancellationToken);
            history = [created.Attempt];
        }

        ValidateHistory(settlementReturn, payment, history);

        var confirmed = history.SingleOrDefault(
            attempt => attempt.State ==
                SettlementReturnProviderAttemptState.Confirmed);
        if (confirmed is not null)
        {
            if (settlementReturn.State != SettlementReturnState.Completed)
            {
                throw Inconsistent(
                    command.OperationId,
                    "a confirmed attempt has not completed the return");
            }

            return Preparation.For(
                settlementReturn,
                confirmed,
                command.AdministratorUserId,
                confirmed.Operation ==
                    SettlementReturnProviderOperation.Reverse
                    ? Disposition.ReverseCompleted
                    : Disposition.RefundCompleted);
        }

        var active = history.SingleOrDefault(attempt => attempt.IsActive);
        if (active is not null)
        {
            return await ResolveActiveAsync(
                settlementReturn,
                active,
                command.AdministratorUserId,
                cancellationToken);
        }

        var rejected = history
            .Where(attempt => attempt.State ==
                SettlementReturnProviderAttemptState.Rejected)
            .OrderByDescending(attempt => attempt.CreatedAt)
            .ThenByDescending(attempt => attempt.Id)
            .FirstOrDefault();

        if (settlementReturn.State == SettlementReturnState.RequiresAttention)
        {
            return Preparation.For(
                settlementReturn,
                rejected ?? history[^1],
                command.AdministratorUserId,
                IsPreExistingRefund(rejected?.Diagnostic)
                    ? Disposition.PreExistingProviderRefund
                    : Disposition.RequiresAttention);
        }

        throw Inconsistent(
            command.OperationId,
            "the return has no resumable provider attempt");
    }

    private async Task<Preparation> ResolveActiveAsync(
        SettlementReturn settlementReturn,
        SettlementReturnProviderAttempt attempt,
        Guid actorId,
        CancellationToken cancellationToken,
        long? refundAmountMinorUnits = null)
    {
        if (
            attempt.State == SettlementReturnProviderAttemptState.Prepared &&
            settlementReturn.State is
                SettlementReturnState.Requested or
                SettlementReturnState.InProgress or
                SettlementReturnState.RequiresAttention)
        {
            var changedAt = _timeProvider.GetUtcNow();
            if (settlementReturn.State == SettlementReturnState.Requested)
            {
                settlementReturn.Begin(changedAt);
            }
            else if (
                settlementReturn.State ==
                    SettlementReturnState.RequiresAttention)
            {
                settlementReturn.Resume(changedAt);
            }

            _auditTrail.Stage(CreateAudit(
                settlementReturn,
                actorId,
                Action(attempt.Operation, "started"),
                attempt.Operation ==
                    SettlementReturnProviderOperation.Reverse
                    ? "CSOB reverse became eligible after durable InProgress persistence."
                    : refundAmountMinorUnits.HasValue
                        ? "CSOB partial refund became eligible after durable InProgress persistence."
                        : "CSOB full refund became eligible after durable InProgress persistence.",
                changedAt));
            attempt = await _attemptService.BeginAsync(
                attempt.Id,
                cancellationToken);
            await _returnRepository.SaveAsync(
                settlementReturn,
                cancellationToken);

            return Preparation.For(
                settlementReturn,
                attempt,
                actorId,
                attempt.Operation ==
                    SettlementReturnProviderOperation.Reverse
                    ? Disposition.SendReverse
                    : Disposition.SendRefund,
                refundAmountMinorUnits: refundAmountMinorUnits);
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

            return Preparation.For(
                settlementReturn,
                attempt,
                actorId,
                attempt.Operation ==
                    SettlementReturnProviderOperation.Reverse
                    ? Disposition.RecoverReverse
                    : Disposition.RecoverRefund,
                refundAmountMinorUnits: refundAmountMinorUnits);
        }

        throw Inconsistent(
            settlementReturn.RequestId,
            $"return {settlementReturn.State} and {attempt.Operation} " +
            $"attempt {attempt.State} cannot be resumed");
    }

    private async Task<CardJobSettlementReturnResult> ContinueAsync(
        Guid operationId,
        Preparation preparation,
        CancellationToken cancellationToken) =>
        preparation.Next switch
        {
            Disposition.SendReverse => await SendReverseAsync(
                operationId,
                preparation,
                cancellationToken),
            Disposition.RecoverReverse => await RecoverReverseAsync(
                operationId,
                preparation,
                cancellationToken),
            Disposition.SendRefund => await SendRefundAsync(
                operationId,
                preparation,
                cancellationToken),
            Disposition.RecoverRefund => await RecoverRefundAsync(
                operationId,
                preparation,
                cancellationToken),
            Disposition.ReverseCompleted => Result(
                preparation,
                CardJobSettlementReturnOutcome.ReverseCompleted),
            Disposition.RefundCompleted => Result(
                preparation,
                preparation.IsPartialRefund
                    ? CardJobSettlementReturnOutcome.PartialRefundCompleted
                    : CardJobSettlementReturnOutcome.RefundCompleted),
            Disposition.RefundRejected => Result(
                preparation,
                CardJobSettlementReturnOutcome.PartialRefundRejected),
            Disposition.PreExistingProviderRefund => Result(
                preparation,
                CardJobSettlementReturnOutcome.PreExistingProviderRefund),
            Disposition.RequiresAttention => Result(
                preparation,
                CardJobSettlementReturnOutcome.RequiresAttention),
            _ => throw Inconsistent(
                operationId,
                "unsupported preparation disposition")
        };

    private async Task<CardJobSettlementReturnResult> SendReverseAsync(
        Guid operationId,
        Preparation preparation,
        CancellationToken cancellationToken)
    {
        CsobPaymentReverseResult response;
        try
        {
            response = await _gatewayClient.ReverseAsync(
                preparation.ProviderReference,
                cancellationToken);
        }
        catch (OperationCanceledException exception)
        {
            await PersistSafetyOrThrowAsync(
                operationId,
                preparation,
                ReverseAmbiguousDiagnostic,
                reverseSent: true,
                refundSent: false,
                exception);
            throw;
        }
        catch (Exception exception)
        {
            return await PersistSafetyOrThrowAsync(
                operationId,
                preparation,
                ReverseAmbiguousDiagnostic,
                reverseSent: true,
                refundSent: false,
                exception);
        }

        if (response.ResultCode == 0 && response.PaymentStatus == 5)
        {
            return await ResolveAfterMutationAsync(
                operationId,
                preparation,
                () => ConfirmAsync(
                    operationId,
                    preparation,
                    reverseSent: true,
                    refundSent: false,
                    cancellationToken),
                ReverseAmbiguousDiagnostic,
                reverseSent: true,
                refundSent: false);
        }

        if (response.ResultCode == 150 && response.PaymentStatus == 8)
        {
            Preparation refund;
            try
            {
                refund = await TransitionToRefundAsync(
                    operationId,
                    preparation,
                    reverseSent: true,
                    cancellationToken);
            }
            catch (Exception exception)
            {
                return await PersistSafetyOrThrowAsync(
                    operationId,
                    preparation,
                    ReverseAmbiguousDiagnostic,
                    reverseSent: true,
                    refundSent: false,
                    exception);
            }

            return await ContinueAsync(operationId, refund, cancellationToken);
        }

        if (
            response.ResultCode == 150 &&
            response.PaymentStatus is 9 or 10)
        {
            return await MarkPreExistingAsync(
                operationId,
                preparation,
                response.PaymentStatus,
                reverseSent: true,
                cancellationToken);
        }

        return await MarkUncertainAsync(
            operationId,
            preparation,
            ReverseAmbiguousDiagnostic,
            reverseSent: true,
            refundSent: false,
            cancellationToken);
    }

    private async Task<CardJobSettlementReturnResult> RecoverReverseAsync(
        Guid operationId,
        Preparation preparation,
        CancellationToken cancellationToken)
    {
        CsobPaymentStatusResult status;
        try
        {
            status = await _gatewayClient.GetStatusAsync(
                preparation.ProviderReference,
                cancellationToken);
        }
        catch (OperationCanceledException exception)
        {
            await PersistSafetyOrThrowAsync(
                operationId,
                preparation,
                ReverseStatusAmbiguousDiagnostic,
                reverseSent: false,
                refundSent: false,
                exception);
            throw;
        }
        catch (Exception exception)
        {
            return await PersistSafetyOrThrowAsync(
                operationId,
                preparation,
                ReverseStatusAmbiguousDiagnostic,
                reverseSent: false,
                refundSent: false,
                exception);
        }

        if (status.ResultCode == 0 && status.PaymentStatus == 5)
        {
            return await ConfirmAsync(
                operationId,
                preparation,
                reverseSent: false,
                refundSent: false,
                cancellationToken);
        }

        if (status.ResultCode == 0 && status.PaymentStatus == 8)
        {
            var refund = await TransitionToRefundAsync(
                operationId,
                preparation,
                reverseSent: false,
                cancellationToken);
            return await ContinueAsync(operationId, refund, cancellationToken);
        }

        if (
            status.ResultCode == 0 &&
            status.PaymentStatus is 9 or 10)
        {
            return await MarkPreExistingAsync(
                operationId,
                preparation,
                status.PaymentStatus,
                reverseSent: false,
                cancellationToken);
        }

        return await MarkUncertainAsync(
            operationId,
            preparation,
            ReverseStatusAmbiguousDiagnostic,
            reverseSent: false,
            refundSent: false,
            cancellationToken);
    }

    private async Task<CardJobSettlementReturnResult> SendRefundAsync(
        Guid operationId,
        Preparation preparation,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await _gatewayClient.RefundAsync(
                preparation.ProviderReference,
                preparation.RefundAmountMinorUnits,
                cancellationToken);

            if (response.ResultCode == 0 && response.PaymentStatus == 10)
            {
                return await ConfirmAsync(
                    operationId,
                    preparation,
                    preparation.ReverseSent,
                    refundSent: true,
                    cancellationToken);
            }

            if (response.ResultCode == 0 && response.PaymentStatus == 9)
            {
                return await ObserveRefundProcessingAsync(
                    operationId,
                    preparation,
                    preparation.ReverseSent,
                    refundSent: true,
                    cancellationToken);
            }

            return await MarkUncertainAsync(
                operationId,
                preparation,
                preparation.IsPartialRefund
                    ? PartialRefundAmbiguousDiagnostic
                    : RefundAmbiguousDiagnostic,
                preparation.ReverseSent,
                refundSent: true,
                cancellationToken);
        }
        catch (OperationCanceledException exception)
        {
            await PersistSafetyOrThrowAsync(
                operationId,
                preparation,
                preparation.IsPartialRefund
                    ? PartialRefundAmbiguousDiagnostic
                    : RefundAmbiguousDiagnostic,
                preparation.ReverseSent,
                refundSent: true,
                exception);
            throw;
        }
        catch (Exception exception)
        {
            return await PersistSafetyOrThrowAsync(
                operationId,
                preparation,
                preparation.IsPartialRefund
                    ? PartialRefundAmbiguousDiagnostic
                    : RefundAmbiguousDiagnostic,
                preparation.ReverseSent,
                refundSent: true,
                exception);
        }
    }

    private async Task<CardJobSettlementReturnResult> RecoverRefundAsync(
        Guid operationId,
        Preparation preparation,
        CancellationToken cancellationToken)
    {
        CsobPaymentStatusResult status;
        try
        {
            status = await _gatewayClient.GetStatusAsync(
                preparation.ProviderReference,
                cancellationToken);
        }
        catch (OperationCanceledException exception)
        {
            await PersistSafetyOrThrowAsync(
                operationId,
                preparation,
                RefundStatusAmbiguousDiagnostic,
                reverseSent: false,
                refundSent: false,
                exception);
            throw;
        }
        catch (Exception exception)
        {
            return await PersistSafetyOrThrowAsync(
                operationId,
                preparation,
                RefundStatusAmbiguousDiagnostic,
                reverseSent: false,
                refundSent: false,
                exception);
        }

        if (status.ResultCode == 0 && status.PaymentStatus == 9)
        {
            return await ObserveRefundProcessingAsync(
                operationId,
                preparation,
                reverseSent: false,
                refundSent: false,
                cancellationToken);
        }

        return await MarkUncertainAsync(
            operationId,
            preparation,
            status.ResultCode == 0 && status.PaymentStatus == 10
                ? preparation.IsPartialRefund
                    ? PartialRefundReturnedWithoutProofDiagnostic
                    : RefundReturnedWithoutFullProofDiagnostic
                : preparation.IsPartialRefund
                    ? PartialRefundStatusAmbiguousDiagnostic
                    : RefundStatusAmbiguousDiagnostic,
            reverseSent: false,
            refundSent: false,
            cancellationToken);
    }

    private Task<Preparation> TransitionToRefundAsync(
        Guid operationId,
        Preparation reverse,
        bool reverseSent,
        CancellationToken cancellationToken) =>
        _transaction.ExecuteAsync(
            ct => TransitionToRefundInsideAsync(
                operationId,
                reverse,
                reverseSent,
                ct),
            cancellationToken);

    private async Task<Preparation> TransitionToRefundInsideAsync(
        Guid operationId,
        Preparation reverse,
        bool reverseSent,
        CancellationToken cancellationToken)
    {
        await LockJobOrThrowAsync(
            operationId,
            reverse.JobId,
            cancellationToken);
        var settlementReturn = await RequireReturnAsync(
            operationId,
            reverse,
            cancellationToken);
        if (settlementReturn.State == SettlementReturnState.Completed)
        {
            return await ResolveCompletedPreparationAsync(
                operationId,
                reverse,
                settlementReturn,
                cancellationToken);
        }

        var reverseAttempt = await RequireAttemptAsync(
            operationId,
            reverse,
            cancellationToken);

        if (
            reverseAttempt.State ==
                SettlementReturnProviderAttemptState.Rejected)
        {
            var currentRefund = (await _attemptRepository
                .ListBySettlementReturnIdAsync(
                    settlementReturn.Id,
                    cancellationToken))
                .SingleOrDefault(attempt =>
                    attempt.Operation ==
                        SettlementReturnProviderOperation.Refund &&
                    (attempt.IsActive ||
                     attempt.State ==
                        SettlementReturnProviderAttemptState.Confirmed));
            if (
                currentRefund?.State ==
                    SettlementReturnProviderAttemptState.Confirmed &&
                settlementReturn.State == SettlementReturnState.Completed)
            {
                return Preparation.For(
                    settlementReturn,
                    currentRefund,
                    reverse.AdministratorActorUserId,
                    Disposition.RefundCompleted);
            }

            if (currentRefund is not null)
            {
                return Preparation.For(
                    settlementReturn,
                    currentRefund,
                    reverse.AdministratorActorUserId,
                    Disposition.RecoverRefund);
            }

            return Preparation.For(
                settlementReturn,
                reverseAttempt,
                reverse.AdministratorActorUserId,
                IsPreExistingRefund(reverseAttempt.Diagnostic)
                    ? Disposition.PreExistingProviderRefund
                    : Disposition.RequiresAttention);
        }

        EnsureResolvable(
            operationId,
            settlementReturn,
            reverseAttempt,
            SettlementReturnProviderOperation.Reverse);

        var changedAt = _timeProvider.GetUtcNow();
        await _attemptService.RejectAsync(
            reverseAttempt.Id,
            ReverseSettledDiagnostic,
            cancellationToken);
        if (
            settlementReturn.State ==
                SettlementReturnState.RequiresAttention)
        {
            settlementReturn.Resume(changedAt);
        }

        _auditTrail.Stage(CreateAudit(
            settlementReturn,
            reverse.AdministratorActorUserId,
            Action(SettlementReturnProviderOperation.Reverse, "rejected"),
            "Signed CSOB evidence proved paymentStatus 8; the settled payment continues as a full refund.",
            changedAt));

        var created = await _attemptService.CreateAsync(
            new CreateSettlementReturnProviderAttemptCommand(
                Guid.NewGuid(),
                settlementReturn.Id,
                SettlementReturnProviderOperation.Refund),
            cancellationToken);
        var refundAttempt = await _attemptService.BeginAsync(
            created.Attempt.Id,
            cancellationToken);
        _auditTrail.Stage(CreateAudit(
            settlementReturn,
            reverse.AdministratorActorUserId,
            Action(SettlementReturnProviderOperation.Refund, "started"),
            "A durable full-refund attempt was committed before the CSOB PUT.",
            changedAt));
        await _returnRepository.SaveAsync(
            settlementReturn,
            cancellationToken);

        return Preparation.For(
            settlementReturn,
            refundAttempt,
            reverse.AdministratorActorUserId,
            Disposition.SendRefund,
            reverseSent);
    }

    private Task<CardJobSettlementReturnResult> MarkPreExistingAsync(
        Guid operationId,
        Preparation preparation,
        int paymentStatus,
        bool reverseSent,
        CancellationToken cancellationToken) =>
        _transaction.ExecuteAsync(
            ct => MarkPreExistingInsideAsync(
                operationId,
                preparation,
                paymentStatus,
                reverseSent,
                ct),
            cancellationToken);

    private async Task<CardJobSettlementReturnResult>
        MarkPreExistingInsideAsync(
            Guid operationId,
            Preparation preparation,
            int paymentStatus,
            bool reverseSent,
            CancellationToken cancellationToken)
    {
        await LockJobOrThrowAsync(
            operationId,
            preparation.JobId,
            cancellationToken);
        var settlementReturn = await RequireReturnAsync(
            operationId,
            preparation,
            cancellationToken);
        if (settlementReturn.State == SettlementReturnState.Completed)
        {
            return await ResolveCompletedResultAsync(
                operationId,
                preparation,
                settlementReturn,
                reverseSent,
                refundSent: false,
                cancellationToken);
        }

        var attempt = await RequireAttemptAsync(
            operationId,
            preparation,
            cancellationToken);

        if (attempt.State == SettlementReturnProviderAttemptState.Rejected)
        {
            var currentRefund = (await _attemptRepository
                .ListBySettlementReturnIdAsync(
                    settlementReturn.Id,
                    cancellationToken))
                .SingleOrDefault(item =>
                    item.Operation ==
                        SettlementReturnProviderOperation.Refund &&
                    (item.IsActive ||
                     item.State ==
                        SettlementReturnProviderAttemptState.Confirmed));
            if (
                currentRefund?.State ==
                    SettlementReturnProviderAttemptState.Confirmed &&
                settlementReturn.State == SettlementReturnState.Completed)
            {
                return Result(
                    Preparation.For(
                        settlementReturn,
                        currentRefund,
                        preparation.AdministratorActorUserId,
                        Disposition.RefundCompleted),
                    CardJobSettlementReturnOutcome.RefundCompleted,
                    reverseSent,
                    refundSent: false);
            }

            if (currentRefund is not null)
            {
                return Result(
                    Preparation.For(
                        settlementReturn,
                        currentRefund,
                        preparation.AdministratorActorUserId,
                        Disposition.RecoverRefund),
                    ActiveRefundOutcome(currentRefund),
                    reverseSent,
                    refundSent: false);
            }

            if (IsPreExistingRefund(attempt.Diagnostic))
            {
                return Result(
                    preparation,
                    CardJobSettlementReturnOutcome.PreExistingProviderRefund,
                    reverseSent,
                    refundSent: false);
            }
        }

        EnsureResolvable(
            operationId,
            settlementReturn,
            attempt,
            SettlementReturnProviderOperation.Reverse);
        var changedAt = _timeProvider.GetUtcNow();
        await _attemptService.RejectAsync(
            attempt.Id,
            paymentStatus == 9
                ? ReverseFoundRefundProcessingDiagnostic
                : ReverseFoundReturnedDiagnostic,
            cancellationToken);
        if (settlementReturn.State == SettlementReturnState.InProgress)
        {
            settlementReturn.RequireAttention(changedAt);
        }

        _auditTrail.Stage(CreateAudit(
            settlementReturn,
            preparation.AdministratorActorUserId,
            "settlement-return.card-job.provider-refund-pre-existing",
            $"Signed CSOB evidence found paymentStatus {paymentStatus} before FUA Pay sent any refund; no refund PUT was issued.",
            changedAt));
        await _returnRepository.SaveAsync(
            settlementReturn,
            cancellationToken);

        return Result(
            preparation,
            CardJobSettlementReturnOutcome.PreExistingProviderRefund,
            reverseSent,
            refundSent: false);
    }

    private Task<CardJobSettlementReturnResult> ConfirmAsync(
        Guid operationId,
        Preparation preparation,
        bool reverseSent,
        bool refundSent,
        CancellationToken cancellationToken) =>
        _transaction.ExecuteAsync(
            ct => ConfirmInsideAsync(
                operationId,
                preparation,
                reverseSent,
                refundSent,
                ct),
            cancellationToken);

    private async Task<CardJobSettlementReturnResult> ConfirmInsideAsync(
        Guid operationId,
        Preparation preparation,
        bool reverseSent,
        bool refundSent,
        CancellationToken cancellationToken)
    {
        await LockJobOrThrowAsync(
            operationId,
            preparation.JobId,
            cancellationToken);
        var settlementReturn = await RequireReturnAsync(
            operationId,
            preparation,
            cancellationToken);
        if (settlementReturn.State == SettlementReturnState.Completed)
        {
            return await ResolveCompletedResultAsync(
                operationId,
                preparation,
                settlementReturn,
                reverseSent,
                refundSent,
                cancellationToken);
        }

        var attempt = await RequireAttemptAsync(
            operationId,
            preparation,
            cancellationToken);

        EnsureResolvable(
            operationId,
            settlementReturn,
            attempt,
            preparation.Operation);
        var changedAt = _timeProvider.GetUtcNow();
        settlementReturn.Complete(changedAt);
        _auditTrail.Stage(CreateAudit(
            settlementReturn,
            preparation.AdministratorActorUserId,
            Action(preparation.Operation, "confirmed"),
            preparation.Operation ==
                SettlementReturnProviderOperation.Reverse
                ? "Signed CSOB evidence confirmed resultCode 0 and paymentStatus 5."
                : preparation.IsPartialRefund
                    ? "The direct signed CSOB partial-refund response confirmed resultCode 0 and paymentStatus 10 for the requested amount."
                    : "The direct signed CSOB full-refund response confirmed resultCode 0 and paymentStatus 10.",
            changedAt));
        await _attemptService.ConfirmAsync(
            attempt.Id,
            cancellationToken);
        await _returnRepository.SaveAsync(
            settlementReturn,
            cancellationToken);

        return Result(
            preparation,
            CompletedOutcome(
                preparation.Operation,
                preparation.IsPartialRefund),
            reverseSent,
            refundSent);
    }

    private Task<CardJobSettlementReturnResult> ObserveRefundProcessingAsync(
        Guid operationId,
        Preparation preparation,
        bool reverseSent,
        bool refundSent,
        CancellationToken cancellationToken) =>
        _transaction.ExecuteAsync(
            ct => ObserveRefundProcessingInsideAsync(
                operationId,
                preparation,
                reverseSent,
                refundSent,
                ct),
            cancellationToken);

    private async Task<CardJobSettlementReturnResult>
        ObserveRefundProcessingInsideAsync(
            Guid operationId,
            Preparation preparation,
            bool reverseSent,
            bool refundSent,
            CancellationToken cancellationToken)
    {
        await LockJobOrThrowAsync(
            operationId,
            preparation.JobId,
            cancellationToken);
        var settlementReturn = await RequireReturnAsync(
            operationId,
            preparation,
            cancellationToken);
        if (settlementReturn.State == SettlementReturnState.Completed)
        {
            return await ResolveCompletedResultAsync(
                operationId,
                preparation,
                settlementReturn,
                reverseSent,
                refundSent,
                cancellationToken);
        }

        var attempt = await RequireAttemptAsync(
            operationId,
            preparation,
            cancellationToken);

        if (
            attempt.Operation != SettlementReturnProviderOperation.Refund ||
            attempt.State is not
                SettlementReturnProviderAttemptState.InProgress and not
                SettlementReturnProviderAttemptState.Uncertain ||
            settlementReturn.State is not
                SettlementReturnState.InProgress and not
                SettlementReturnState.RequiresAttention)
        {
            throw Inconsistent(
                operationId,
                "refund-processing evidence cannot resolve the current states");
        }

        if (
            attempt.State ==
                SettlementReturnProviderAttemptState.Uncertain)
        {
            await _attemptService.UpdateUncertainAsync(
                attempt.Id,
                preparation.IsPartialRefund
                    ? CardJobSettlementReturnDiagnostics
                        .PartialRefundProcessing
                    : RefundProcessingDiagnostic,
                cancellationToken);
        }

        await _auditTrail.WriteAsync(
            CreateAudit(
                settlementReturn,
                preparation.AdministratorActorUserId,
                Action(SettlementReturnProviderOperation.Refund, "processing"),
                "Signed CSOB evidence confirmed paymentStatus 9; no additional refund PUT will be sent.",
                _timeProvider.GetUtcNow()),
            cancellationToken);
        return Result(
            preparation,
            preparation.IsPartialRefund
                ? CardJobSettlementReturnOutcome.PartialRefundProcessing
                : CardJobSettlementReturnOutcome.RefundProcessing,
            reverseSent,
            refundSent);
    }

    private Task<CardJobSettlementReturnResult> MarkUncertainAsync(
        Guid operationId,
        Preparation preparation,
        string diagnostic,
        bool reverseSent,
        bool refundSent,
        CancellationToken cancellationToken) =>
        _transaction.ExecuteAsync(
            ct => MarkUncertainInsideAsync(
                operationId,
                preparation,
                diagnostic,
                reverseSent,
                refundSent,
                ct),
            cancellationToken);

    private async Task<CardJobSettlementReturnResult> MarkUncertainInsideAsync(
        Guid operationId,
        Preparation preparation,
        string diagnostic,
        bool reverseSent,
        bool refundSent,
        CancellationToken cancellationToken)
    {
        await LockJobOrThrowAsync(
            operationId,
            preparation.JobId,
            cancellationToken);
        var settlementReturn = await RequireReturnAsync(
            operationId,
            preparation,
            cancellationToken);
        if (settlementReturn.State == SettlementReturnState.Completed)
        {
            return await ResolveCompletedResultAsync(
                operationId,
                preparation,
                settlementReturn,
                reverseSent,
                refundSent,
                cancellationToken);
        }

        var attempt = await RequireAttemptAsync(
            operationId,
            preparation,
            cancellationToken);

        if (attempt.State == SettlementReturnProviderAttemptState.Rejected)
        {
            var currentRefund = (await _attemptRepository
                .ListBySettlementReturnIdAsync(
                    settlementReturn.Id,
                    cancellationToken))
                .SingleOrDefault(item =>
                    item.Operation ==
                        SettlementReturnProviderOperation.Refund &&
                    (item.IsActive ||
                     item.State ==
                        SettlementReturnProviderAttemptState.Confirmed));
            if (
                currentRefund?.State ==
                    SettlementReturnProviderAttemptState.Confirmed &&
                settlementReturn.State == SettlementReturnState.Completed)
            {
                return Result(
                    Preparation.For(
                        settlementReturn,
                        currentRefund,
                        preparation.AdministratorActorUserId,
                        Disposition.RefundCompleted),
                    CardJobSettlementReturnOutcome.RefundCompleted,
                    reverseSent,
                    refundSent);
            }

            if (currentRefund is not null)
            {
                return Result(
                    Preparation.For(
                        settlementReturn,
                        currentRefund,
                        preparation.AdministratorActorUserId,
                        Disposition.RecoverRefund),
                    ActiveRefundOutcome(currentRefund),
                    reverseSent,
                    refundSent);
            }
        }

        EnsureResolvable(
            operationId,
            settlementReturn,
            attempt,
            preparation.Operation);
        var changedAt = _timeProvider.GetUtcNow();
        if (settlementReturn.State == SettlementReturnState.InProgress)
        {
            settlementReturn.RequireAttention(changedAt);
            await _returnRepository.SaveAsync(
                settlementReturn,
                cancellationToken);
        }

        if (attempt.State == SettlementReturnProviderAttemptState.InProgress)
        {
            await _attemptService.MarkUncertainAsync(
                attempt.Id,
                diagnostic,
                cancellationToken);
        }
        else
        {
            await _attemptService.UpdateUncertainAsync(
                attempt.Id,
                diagnostic,
                cancellationToken);
        }

        await _auditTrail.WriteAsync(
            CreateAudit(
                settlementReturn,
                preparation.AdministratorActorUserId,
                Action(preparation.Operation, "requires-attention"),
                preparation.Operation ==
                    SettlementReturnProviderOperation.Reverse
                    ? "CSOB reverse outcome is ambiguous; all future automatic recovery is status-only."
                    : preparation.IsPartialRefund
                        ? "CSOB partial-refund outcome is unresolved; all future automatic recovery is status-only and no second refund PUT is allowed."
                        : "CSOB full-refund outcome is unresolved; all future automatic recovery is status-only and no second refund PUT is allowed.",
                changedAt),
            cancellationToken);
        return Result(
            preparation,
            CardJobSettlementReturnOutcome.RequiresAttention,
            reverseSent,
            refundSent);
    }

    private async Task<CardJobSettlementReturnResult>
        ResolveAfterMutationAsync(
            Guid operationId,
            Preparation preparation,
            Func<Task<CardJobSettlementReturnResult>> apply,
            string diagnostic,
            bool reverseSent,
            bool refundSent)
    {
        try
        {
            return await apply();
        }
        catch (Exception exception)
        {
            return await PersistSafetyOrThrowAsync(
                operationId,
                preparation,
                diagnostic,
                reverseSent,
                refundSent,
                exception);
        }
    }

    private async Task<CardJobSettlementReturnResult> PersistSafetyOrThrowAsync(
        Guid operationId,
        Preparation preparation,
        string diagnostic,
        bool reverseSent,
        bool refundSent,
        Exception originalException)
    {
        try
        {
            return await MarkUncertainAsync(
                operationId,
                preparation,
                diagnostic,
                reverseSent,
                refundSent,
                CancellationToken.None);
        }
        catch (Exception safetyException)
        {
            throw new CardJobSettlementReturnSafetyStateException(
                operationId,
                new AggregateException(originalException, safetyException));
        }
    }

    private async Task LockJobOrThrowAsync(
        Guid operationId,
        Guid jobId,
        CancellationToken cancellationToken)
    {
        if (!await _jobPaymentCoordination.LockJobAsync(
                jobId,
                cancellationToken))
        {
            throw Inconsistent(
                operationId,
                "the authoritative job disappeared while resolving the return");
        }
    }

    private async Task<SettlementReturn> RequireReturnAsync(
        Guid operationId,
        Preparation preparation,
        CancellationToken cancellationToken)
    {
        var settlementReturn = await _returnRepository.FindByIdAsync(
            preparation.SettlementReturnId,
            cancellationToken)
            ?? throw Inconsistent(
                operationId,
                "the SettlementReturn no longer exists");
        if (
            settlementReturn.RequestId != operationId ||
            settlementReturn.JobId != preparation.JobId ||
            settlementReturn.Kind != SettlementReturnKind.CardJob)
        {
            throw Inconsistent(
                operationId,
                "the persisted return identity changed");
        }

        return settlementReturn;
    }

    private async Task<Preparation> ResolveCompletedPreparationAsync(
        Guid operationId,
        Preparation stalePreparation,
        SettlementReturn settlementReturn,
        CancellationToken cancellationToken)
    {
        if (settlementReturn.State != SettlementReturnState.Completed)
        {
            throw Inconsistent(
                operationId,
                "completed-state convergence requires a completed return");
        }

        if (!settlementReturn.OriginalPaymentId.HasValue)
        {
            throw Inconsistent(
                operationId,
                "the completed CardJob return has no original payment");
        }

        var payment = await _paymentRepository.FindByIdAsync(
            settlementReturn.OriginalPaymentId.Value,
            cancellationToken)
            ?? throw Inconsistent(
                operationId,
                "the authoritative original payment disappeared");

        if (
            payment.Provider != PaymentProvider.Csob ||
            payment.PurposeType != PaymentPurposeType.Job ||
            payment.Status != PaymentStatus.Succeeded ||
            payment.ProviderReference is null ||
            payment.JobId != settlementReturn.JobId ||
            payment.CustomerUserId != settlementReturn.CustomerUserId ||
            (stalePreparation.IsPartialRefund
                ? settlementReturn.Amount.MinorUnits <= 0 ||
                  settlementReturn.Amount.MinorUnits >=
                    payment.Amount.MinorUnits
                : payment.Amount != settlementReturn.Amount) ||
            !string.Equals(
                payment.ProviderReference,
                stalePreparation.ProviderReference,
                StringComparison.Ordinal))
        {
            throw Inconsistent(
                operationId,
                "the completed return no longer matches its authoritative CSOB payment");
        }

        var history = await _attemptRepository.ListBySettlementReturnIdAsync(
            settlementReturn.Id,
            cancellationToken);
        if (stalePreparation.IsPartialRefund)
        {
            ValidatePartialHistory(settlementReturn, payment, history);
        }
        else
        {
            ValidateHistory(settlementReturn, payment, history);
        }

        var confirmed = history.SingleOrDefault(attempt =>
            attempt.State == SettlementReturnProviderAttemptState.Confirmed)
            ?? throw Inconsistent(
                operationId,
                "the completed return has no confirmed provider attempt");

        return Preparation.For(
            settlementReturn,
            confirmed,
            stalePreparation.AdministratorActorUserId,
            confirmed.Operation == SettlementReturnProviderOperation.Reverse
                ? Disposition.ReverseCompleted
                : Disposition.RefundCompleted,
            stalePreparation.ReverseSent,
            stalePreparation.RefundAmountMinorUnits);
    }

    private async Task<CardJobSettlementReturnResult>
        ResolveCompletedResultAsync(
            Guid operationId,
            Preparation stalePreparation,
            SettlementReturn settlementReturn,
            bool reverseSent,
            bool refundSent,
            CancellationToken cancellationToken)
    {
        var completed = await ResolveCompletedPreparationAsync(
            operationId,
            stalePreparation,
            settlementReturn,
            cancellationToken);

        return Result(
            completed,
            CompletedOutcome(
                completed.Operation,
                completed.IsPartialRefund),
            reverseSent,
            refundSent);
    }

    private async Task<SettlementReturnProviderAttempt> RequireAttemptAsync(
        Guid operationId,
        Preparation preparation,
        CancellationToken cancellationToken)
    {
        var attempt = await _attemptRepository.FindByIdAsync(
            preparation.ProviderAttemptId,
            cancellationToken)
            ?? throw Inconsistent(
                operationId,
                "the provider attempt no longer exists");
        if (
            attempt.SettlementReturnId != preparation.SettlementReturnId ||
            attempt.Provider != PaymentProvider.Csob ||
            attempt.Operation != preparation.Operation ||
            !string.Equals(
                attempt.ProviderReference,
                preparation.ProviderReference,
                StringComparison.Ordinal))
        {
            throw Inconsistent(
                operationId,
                "the persisted provider-attempt identity changed");
        }

        return attempt;
    }

    private static void EnsureResolvable(
        Guid operationId,
        SettlementReturn settlementReturn,
        SettlementReturnProviderAttempt attempt,
        SettlementReturnProviderOperation operation)
    {
        if (
            attempt.Operation != operation ||
            attempt.State is not
                SettlementReturnProviderAttemptState.InProgress and not
                SettlementReturnProviderAttemptState.Uncertain ||
            settlementReturn.State is not
                SettlementReturnState.InProgress and not
                SettlementReturnState.RequiresAttention)
        {
            throw Inconsistent(
                operationId,
                "signed gateway evidence cannot resolve the current states");
        }
    }

    private static long SumReservedMinorUnits(
        IEnumerable<SettlementReturn> settlementReturns)
    {
        long reserved = 0;
        foreach (var settlementReturn in settlementReturns)
        {
            if (
                settlementReturn.Kind == SettlementReturnKind.CardJob &&
                settlementReturn.State != SettlementReturnState.Rejected)
            {
                reserved = checked(
                    reserved + settlementReturn.Amount.MinorUnits);
            }
        }

        return reserved;
    }

    private static void ValidatePartialReturn(
        Payment payment,
        Job job,
        SettlementReturn settlementReturn)
    {
        if (
            settlementReturn.Kind != SettlementReturnKind.CardJob ||
            settlementReturn.OriginalPaymentId != payment.Id ||
            settlementReturn.JobId != job.Id ||
            settlementReturn.CustomerUserId != payment.CustomerUserId ||
            settlementReturn.Amount.MinorUnits <= 0 ||
            settlementReturn.Amount.MinorUnits >= payment.Amount.MinorUnits)
        {
            throw Inconsistent(
                settlementReturn.RequestId,
                "the partial SettlementReturn does not match its " +
                "authoritative payment, job, and partial amount ceiling");
        }
    }

    private static void ValidatePartialHistory(
        SettlementReturn settlementReturn,
        Payment payment,
        IReadOnlyList<SettlementReturnProviderAttempt> history)
    {
        if (history.Count != 1)
        {
            throw Inconsistent(
                settlementReturn.RequestId,
                "a partial CardJob refund must have exactly one provider attempt");
        }

        var attempt = history[0];
        if (
            attempt.Id != settlementReturn.RequestId ||
            attempt.SettlementReturnId != settlementReturn.Id ||
            attempt.Provider != PaymentProvider.Csob ||
            attempt.Operation != SettlementReturnProviderOperation.Refund ||
            !string.Equals(
                attempt.ProviderReference,
                payment.ProviderReference,
                StringComparison.Ordinal))
        {
            throw Inconsistent(
                settlementReturn.RequestId,
                "the partial-refund attempt does not match its request and payment");
        }

        var terminalShape = settlementReturn.State switch
        {
            SettlementReturnState.Completed =>
                attempt.State ==
                    SettlementReturnProviderAttemptState.Confirmed,
            SettlementReturnState.Rejected =>
                attempt.State ==
                    SettlementReturnProviderAttemptState.Rejected,
            _ => attempt.State is not
                SettlementReturnProviderAttemptState.Confirmed and not
                SettlementReturnProviderAttemptState.Rejected
        };
        if (!terminalShape)
        {
            throw Inconsistent(
                settlementReturn.RequestId,
                "the partial-refund return and attempt terminal states disagree");
        }
    }

    private static void ValidateHistory(
        SettlementReturn settlementReturn,
        Payment payment,
        IReadOnlyList<SettlementReturnProviderAttempt> history)
    {
        if (history.Count == 0)
        {
            throw Inconsistent(
                settlementReturn.RequestId,
                "the CardJob return has no provider-attempt history");
        }

        if (
            history.Count(attempt => attempt.IsActive) > 1 ||
            history.Count(attempt => attempt.State ==
                SettlementReturnProviderAttemptState.Confirmed) > 1)
        {
            throw Inconsistent(
                settlementReturn.RequestId,
                "provider-attempt history has multiple active or confirmed attempts");
        }

        foreach (var attempt in history)
        {
            if (
                attempt.SettlementReturnId != settlementReturn.Id ||
                attempt.Provider != PaymentProvider.Csob ||
                !string.Equals(
                    attempt.ProviderReference,
                    payment.ProviderReference,
                    StringComparison.Ordinal))
            {
                throw Inconsistent(
                    settlementReturn.RequestId,
                    "provider-attempt history does not match the authoritative CSOB payment");
            }
        }

        var reverseAttempts = history
            .Where(attempt =>
                attempt.Operation == SettlementReturnProviderOperation.Reverse)
            .ToArray();
        var refundAttempts = history
            .Where(attempt =>
                attempt.Operation == SettlementReturnProviderOperation.Refund)
            .ToArray();

        if (
            reverseAttempts.Length != 1 ||
            reverseAttempts[0].Id != settlementReturn.RequestId)
        {
            throw Inconsistent(
                settlementReturn.RequestId,
                "CardJob return history must contain exactly one request-bound Reverse attempt");
        }

        if (refundAttempts.Length > 1)
        {
            throw Inconsistent(
                settlementReturn.RequestId,
                "R2 CardJob return history must not contain multiple Refund attempts");
        }

        var reverse = reverseAttempts[0];
        var refund = refundAttempts.SingleOrDefault();

        if (
            refund is not null &&
            (reverse.State !=
                SettlementReturnProviderAttemptState.Rejected ||
             !reverse.StartedAt.HasValue ||
             !reverse.FinishedAt.HasValue ||
             !string.Equals(
                 reverse.Diagnostic,
                 ReverseSettledDiagnostic,
                 StringComparison.Ordinal) ||
             reverse.FinishedAt.Value > refund.CreatedAt))
        {
            throw Inconsistent(
                settlementReturn.RequestId,
                "Refund may exist only after signed paymentStatus 8 " +
                "provenance from a started and finished Reverse attempt");
        }

        if (
            refund?.State == SettlementReturnProviderAttemptState.Rejected)
        {
            throw Inconsistent(
                settlementReturn.RequestId,
                "R2 does not support a rejected Refund attempt history");
        }

        var confirmed = history
            .Where(attempt =>
                attempt.State == SettlementReturnProviderAttemptState.Confirmed)
            .ToArray();

        if (settlementReturn.State == SettlementReturnState.Completed)
        {
            if (confirmed.Length != 1)
            {
                throw Inconsistent(
                    settlementReturn.RequestId,
                    "a completed CardJob return must have exactly one confirmed provider attempt");
            }

            if (
                confirmed[0].Operation ==
                    SettlementReturnProviderOperation.Reverse &&
                refund is not null)
            {
                throw Inconsistent(
                    settlementReturn.RequestId,
                    "a Reverse-completed return must not also contain a Refund attempt");
            }

            if (
                confirmed[0].Operation ==
                    SettlementReturnProviderOperation.Refund &&
                refund?.Id != confirmed[0].Id)
            {
                throw Inconsistent(
                    settlementReturn.RequestId,
                    "a Refund-completed return does not match its confirmed Refund attempt");
            }
        }
        else if (confirmed.Length != 0)
        {
            throw Inconsistent(
                settlementReturn.RequestId,
                "an unresolved CardJob return must not have a confirmed provider attempt");
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

    private static bool IsPreExistingRefund(string? diagnostic) =>
        string.Equals(
            diagnostic,
            ReverseFoundRefundProcessingDiagnostic,
            StringComparison.Ordinal) ||
        string.Equals(
            diagnostic,
            ReverseFoundReturnedDiagnostic,
            StringComparison.Ordinal);

    private static CardJobSettlementReturnResult Result(
        Preparation preparation,
        CardJobSettlementReturnOutcome outcome,
        bool reverseSent = false,
        bool refundSent = false) =>
        new(
            preparation.SettlementReturnId,
            preparation.ProviderAttemptId,
            outcome,
            reverseSent || preparation.ReverseSent,
            refundSent);

    private static CardJobSettlementReturnOutcome CompletedOutcome(
        SettlementReturnProviderOperation operation,
        bool isPartialRefund = false) =>
        operation == SettlementReturnProviderOperation.Reverse
            ? CardJobSettlementReturnOutcome.ReverseCompleted
            : isPartialRefund
                ? CardJobSettlementReturnOutcome.PartialRefundCompleted
                : CardJobSettlementReturnOutcome.RefundCompleted;

    private static CardJobSettlementReturnOutcome ActiveRefundOutcome(
        SettlementReturnProviderAttempt attempt) =>
        attempt.State == SettlementReturnProviderAttemptState.Uncertain &&
         string.Equals(
             attempt.Diagnostic,
             RefundProcessingDiagnostic,
             StringComparison.Ordinal)
            ? CardJobSettlementReturnOutcome.RefundProcessing
            : CardJobSettlementReturnOutcome.RequiresAttention;

    private static string Action(
        SettlementReturnProviderOperation operation,
        string suffix) =>
        $"settlement-return.card-job.{operation.ToString().ToLowerInvariant()}-{suffix}";

    private static AuditEntry CreateAudit(
        SettlementReturn settlementReturn,
        Guid actorId,
        string action,
        string outcome,
        DateTimeOffset occurredAt) =>
        AuditEntry.ForUser(
            actorId,
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

    private static void ValidatePartialCommand(
        CardJobPartialRefundCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateId(command.OperationId, nameof(command.OperationId));
        ValidateId(
            command.OriginalPaymentId,
            nameof(command.OriginalPaymentId));
        ValidateId(
            command.AdministratorUserId,
            nameof(command.AdministratorUserId));

        if (command.AmountMinorUnits <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(command),
                "Partial refund amount must be positive.");
        }

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
        Guid paymentId,
        string reason) =>
        new(paymentId, reason);

    private static CardJobSettlementReturnStateInconsistentException
        Inconsistent(Guid operationId, string reason) =>
        new(operationId, reason);

    private enum Disposition
    {
        Unknown = 0,
        SendReverse = 1,
        RecoverReverse = 2,
        SendRefund = 3,
        RecoverRefund = 4,
        ReverseCompleted = 5,
        RefundCompleted = 6,
        PreExistingProviderRefund = 7,
        RequiresAttention = 8,
        RefundRejected = 9
    }

    private sealed record Preparation(
        Guid SettlementReturnId,
        Guid JobId,
        Guid ProviderAttemptId,
        SettlementReturnProviderOperation Operation,
        string ProviderReference,
        Guid AdministratorActorUserId,
        Disposition Next,
        bool ReverseSent,
        long? RefundAmountMinorUnits)
    {
        public bool IsPartialRefund => RefundAmountMinorUnits.HasValue;

        public static Preparation For(
            SettlementReturn settlementReturn,
            SettlementReturnProviderAttempt attempt,
            Guid actorId,
            Disposition next,
            bool reverseSent = false,
            long? refundAmountMinorUnits = null) =>
            new(
                settlementReturn.Id,
                settlementReturn.JobId!.Value,
                attempt.Id,
                attempt.Operation,
                attempt.ProviderReference,
                actorId,
                next,
                reverseSent,
                refundAmountMinorUnits);
    }
}
