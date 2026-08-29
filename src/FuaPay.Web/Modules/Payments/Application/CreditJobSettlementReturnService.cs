using FuaPay.Web.BuildingBlocks.Application;
using FuaPay.Web.BuildingBlocks.Auditing;
using FuaPay.Web.Modules.Credits.Application;
using FuaPay.Web.Modules.Credits.Domain;
using FuaPay.Web.Modules.Jobs.Application;
using FuaPay.Web.Modules.Jobs.Domain;
using FuaPay.Web.Modules.Payments.Domain;

namespace FuaPay.Web.Modules.Payments.Application;

public sealed record CreditJobSettlementReturnCommand(
    Guid RequestId,
    Guid JobId,
    Guid AdministratorUserId,
    string Reason);

public sealed record CreditJobSettlementReturnResult(
    SettlementReturn SettlementReturn,
    bool Created);

public sealed class CreditJobSettlementReturnService
{
    private readonly IJobRepository _jobRepository;
    private readonly IJobPaymentCoordination _jobPaymentCoordination;
    private readonly ICreditQueries _creditQueries;
    private readonly CreditService _creditService;
    private readonly SettlementReturnRegistrationService
        _registrationService;
    private readonly ISettlementReturnRepository _returnRepository;
    private readonly IApplicationTransaction _transaction;
    private readonly IAuditTrail _auditTrail;
    private readonly TimeProvider _timeProvider;

    public CreditJobSettlementReturnService(
        IJobRepository jobRepository,
        IJobPaymentCoordination jobPaymentCoordination,
        ICreditQueries creditQueries,
        CreditService creditService,
        SettlementReturnRegistrationService registrationService,
        ISettlementReturnRepository returnRepository,
        IApplicationTransaction transaction,
        IAuditTrail auditTrail,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(jobRepository);
        ArgumentNullException.ThrowIfNull(jobPaymentCoordination);
        ArgumentNullException.ThrowIfNull(creditQueries);
        ArgumentNullException.ThrowIfNull(creditService);
        ArgumentNullException.ThrowIfNull(registrationService);
        ArgumentNullException.ThrowIfNull(returnRepository);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(auditTrail);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _jobRepository = jobRepository;
        _jobPaymentCoordination = jobPaymentCoordination;
        _creditQueries = creditQueries;
        _creditService = creditService;
        _registrationService = registrationService;
        _returnRepository = returnRepository;
        _transaction = transaction;
        _auditTrail = auditTrail;
        _timeProvider = timeProvider;
    }

    public Task<CreditJobSettlementReturnResult> ReturnAsync(
        CreditJobSettlementReturnCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        ValidateId(command.RequestId, nameof(command.RequestId));
        ValidateId(command.JobId, nameof(command.JobId));
        ValidateId(
            command.AdministratorUserId,
            nameof(command.AdministratorUserId));

        if (string.IsNullOrWhiteSpace(command.Reason))
        {
            throw new ArgumentException(
                "Settlement return reason must not be blank.",
                nameof(command));
        }

        var reason = command.Reason.Trim();

        if (reason.Length > SettlementReturn.MaximumReasonLength)
        {
            throw new ArgumentException(
                "Settlement return reason is too long.",
                nameof(command));
        }

        return _transaction.ExecuteAsync(
            transactionCancellationToken =>
                ReturnInsideTransactionAsync(
                    command,
                    reason,
                    transactionCancellationToken),
            cancellationToken);
    }

    private async Task<CreditJobSettlementReturnResult>
        ReturnInsideTransactionAsync(
            CreditJobSettlementReturnCommand command,
            string reason,
            CancellationToken cancellationToken)
    {
        var wasLocked = await _jobPaymentCoordination.LockJobAsync(
            command.JobId,
            cancellationToken);

        if (!wasLocked)
        {
            throw new JobNotFoundException(command.JobId);
        }

        var job = await _jobRepository.FindByIdAsync(
            command.JobId,
            cancellationToken)
            ?? throw new JobNotFoundException(command.JobId);

        if (
            job.PaymentStatus != JobPaymentStatus.Paid ||
            job.SettlementType != JobSettlementType.Credit)
        {
            throw new CreditJobSettlementReturnNotAllowedException(
                job.Id,
                job.PaymentStatus,
                job.SettlementType);
        }

        if (
            !job.SettlementReferenceId.HasValue ||
            !job.SettledAt.HasValue)
        {
            throw InconsistentJob(
                job,
                "paid credit settlement is incomplete");
        }

        if (job.SettlementReferenceId.Value != job.Id)
        {
            throw InconsistentJob(
                job,
                "settlement reference is not the Job credit operation");
        }

        var originalDebit =
            await _creditQueries.FindMovementForOwnerAsync(
                job.CustomerUserId,
                job.SettlementReferenceId.Value,
                cancellationToken);

        ValidateOriginalDebit(job, originalDebit);

        var candidate = new SettlementReturn(
            Guid.NewGuid(),
            command.RequestId,
            SettlementReturnKind.CreditJob,
            originalPaymentId: null,
            job.Id,
            job.CustomerUserId,
            command.AdministratorUserId,
            job.Price,
            reason,
            _timeProvider.GetUtcNow());

        var existingForJob =
            await _returnRepository.FindByJobIdAsync(
                job.Id,
                cancellationToken);

        if (
            existingForJob is not null &&
            existingForJob.RequestId != command.RequestId)
        {
            throw new SettlementReturnSourceConflictException(
                command.RequestId,
                existingForJob.Id);
        }

        var registration = await _registrationService.RegisterAsync(
            candidate,
            cancellationToken);

        if (!registration.Created)
        {
            await ValidateCompletedEffectAsync(
                registration.SettlementReturn,
                cancellationToken);

            return new CreditJobSettlementReturnResult(
                registration.SettlementReturn,
                Created: false);
        }

        var settlementReturn = registration.SettlementReturn;

        settlementReturn.Begin(_timeProvider.GetUtcNow());

        var compensation = await _creditService.CreditAsync(
            settlementReturn.CustomerUserId,
            settlementReturn.Id,
            settlementReturn.Amount,
            $"Return of credit payment for job {job.Number}",
            cancellationToken);

        ValidateNewCompensation(settlementReturn, compensation);

        var completedAt = _timeProvider.GetUtcNow();

        if (completedAt < compensation.RecordedAt)
        {
            throw EffectInconsistent(
                settlementReturn,
                "completion precedes the compensating Credit");
        }

        settlementReturn.Complete(completedAt);

        _auditTrail.Stage(AuditEntry.ForUser(
            settlementReturn.AdministratorUserId,
            "settlement-return.credit-job.completed",
            "settlement-return",
            settlementReturn.Id.ToString(),
            CreateAuditDescription(job, settlementReturn),
            completedAt));

        await _returnRepository.SaveAsync(
            settlementReturn,
            cancellationToken);

        return new CreditJobSettlementReturnResult(
            settlementReturn,
            Created: true);
    }

    private static void ValidateOriginalDebit(
        Job job,
        CreditMovementListItem? movement)
    {
        if (movement is null)
        {
            throw InconsistentJob(
                job,
                "original Debit is missing or belongs to another customer");
        }

        if (movement.OperationId != job.SettlementReferenceId)
        {
            throw InconsistentJob(
                job,
                "original Debit operation does not match the settlement");
        }

        if (movement.Type != CreditMovementType.Debit)
        {
            throw InconsistentJob(
                job,
                "original credit movement is not a Debit");
        }

        if (movement.AmountMinorUnits != job.Price.MinorUnits)
        {
            throw InconsistentJob(
                job,
                "original Debit amount does not match the Job price");
        }

        if (movement.RecordedAt != job.SettledAt)
        {
            throw InconsistentJob(
                job,
                "original Debit time does not match the settlement time");
        }
    }

    private async Task ValidateCompletedEffectAsync(
        SettlementReturn settlementReturn,
        CancellationToken cancellationToken)
    {
        if (settlementReturn.State != SettlementReturnState.Completed)
        {
            throw EffectInconsistent(
                settlementReturn,
                $"state is {settlementReturn.State}, not Completed");
        }

        var compensation =
            await _creditQueries.FindMovementForOwnerAsync(
                settlementReturn.CustomerUserId,
                settlementReturn.Id,
                cancellationToken);

        if (compensation is null)
        {
            throw EffectInconsistent(
                settlementReturn,
                "compensating Credit is missing or belongs to another customer");
        }

        if (
            compensation.OperationId != settlementReturn.Id ||
            compensation.Type != CreditMovementType.Credit ||
            compensation.AmountMinorUnits !=
                settlementReturn.Amount.MinorUnits ||
            !settlementReturn.StartedAt.HasValue ||
            !settlementReturn.CompletedAt.HasValue ||
            compensation.RecordedAt <
                settlementReturn.StartedAt.Value ||
            compensation.RecordedAt >
                settlementReturn.CompletedAt.Value)
        {
            throw EffectInconsistent(
                settlementReturn,
                "compensating Credit does not match the durable return");
        }
    }

    private static void ValidateNewCompensation(
        SettlementReturn settlementReturn,
        CreditMovement compensation)
    {
        if (
            compensation.OperationId != settlementReturn.Id ||
            compensation.Type != CreditMovementType.Credit ||
            compensation.Amount != settlementReturn.Amount)
        {
            throw EffectInconsistent(
                settlementReturn,
                "CreditService returned a mismatched compensation");
        }
    }

    private static string CreateAuditDescription(
        Job job,
        SettlementReturn settlementReturn)
    {
        return
            $"Job {job.Number} ({job.Id}); customer " +
            $"{settlementReturn.CustomerUserId}; " +
            $"{settlementReturn.Amount.MinorUnits} CZK minor units " +
            "returned to FUA Pay credit; reason: " +
            settlementReturn.Reason;
    }

    private static CreditJobSettlementHistoryInconsistentException
        InconsistentJob(
            Job job,
            string reason)
    {
        return new CreditJobSettlementHistoryInconsistentException(
            job.Id,
            reason);
    }

    private static CreditJobSettlementReturnEffectInconsistentException
        EffectInconsistent(
            SettlementReturn settlementReturn,
            string reason)
    {
        return new CreditJobSettlementReturnEffectInconsistentException(
            settlementReturn.Id,
            reason);
    }

    private static void ValidateId(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException(
                "Settlement return command ID must not be empty.",
                parameterName);
        }
    }
}
