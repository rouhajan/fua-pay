using FuaPay.Web.BuildingBlocks.Application;
using FuaPay.Web.BuildingBlocks.Auditing;
using FuaPay.Web.BuildingBlocks.Notifications;
using FuaPay.Web.Modules.Credits.Application;
using FuaPay.Web.Modules.Jobs.Domain;

namespace FuaPay.Web.Modules.Jobs.Application;

public sealed class CreditJobPaymentService
{
    private readonly IJobRepository _jobRepository;
    private readonly CreditService _creditService;
    private readonly IApplicationTransaction _transaction;
    private readonly IAuditTrail _auditTrail;
    private readonly INotificationOutbox _notificationOutbox;

    public CreditJobPaymentService(
        IJobRepository jobRepository,
        CreditService creditService,
        IApplicationTransaction transaction,
        IAuditTrail auditTrail,
        INotificationOutbox notificationOutbox)
    {
        ArgumentNullException.ThrowIfNull(jobRepository);
        ArgumentNullException.ThrowIfNull(creditService);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(auditTrail);
        ArgumentNullException.ThrowIfNull(notificationOutbox);

        _jobRepository = jobRepository;
        _creditService = creditService;
        _transaction = transaction;
        _auditTrail = auditTrail;
        _notificationOutbox = notificationOutbox;
    }

    public Task<bool> PayAsync(
        Guid customerUserId,
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        ValidateId(
            customerUserId,
            nameof(customerUserId),
            "ID zákazníka nesmí být prázdné.");

        ValidateId(
            jobId,
            nameof(jobId),
            "ID zakázky nesmí být prázdné.");

        return _transaction.ExecuteAsync(
            transactionCancellationToken =>
                PayInsideTransactionAsync(
                    customerUserId,
                    jobId,
                    transactionCancellationToken),
            cancellationToken);
    }

    private async Task<bool> PayInsideTransactionAsync(
        Guid customerUserId,
        Guid jobId,
        CancellationToken cancellationToken)
    {
        var job = await _jobRepository.FindByIdAsync(
            jobId,
            cancellationToken);

        if (job is null)
        {
            throw new JobNotFoundException(jobId);
        }

        if (job.CustomerUserId != customerUserId)
        {
            throw new JobPaymentAccessDeniedException(
                job.Id,
                customerUserId);
        }

        if (job.PaymentStatus == JobPaymentStatus.Paid)
        {
            if (!job.SettledAt.HasValue)
            {
                throw new InvalidDataException(
                    $"Uhrazená zakázka '{job.Id}' nemá čas úhrady.");
            }

            return job.ConfirmSettlement(
                JobSettlementType.Credit,
                job.Id,
                job.SettledAt.Value);
        }

        if (job.ProductionStatus != JobProductionStatus.Published)
        {
            throw new JobSettlementNotAllowedException(
                job.ProductionStatus);
        }

        var movement =
            await _creditService.DebitAsync(
                customerUserId,
                job.Id,
                job.Price,
                $"Úhrada zakázky {job.Number}",
                cancellationToken);

        var wasApplied = job.ConfirmSettlement(
            JobSettlementType.Credit,
            movement.OperationId,
            movement.RecordedAt);

        if (!wasApplied)
        {
            throw new InvalidOperationException(
                "Nová kreditní operace nepotvrdila úhradu zakázky.");
        }

        _auditTrail.Stage(AuditEntry.ForUser(
            customerUserId,
            "job.settled-by-credit",
            "job",
            job.Id.ToString(),
            $"Zakázka {job.Number} byla uhrazena kreditem.",
            movement.RecordedAt));
        _notificationOutbox.Stage(NotificationMessage.Create(
            customerUserId,
            "job.paid",
            $"Zakázka {job.Number} byla uhrazena",
            $"Zakázka {job.Number} byla úspěšně uhrazena z kreditu.",
            movement.RecordedAt));

        await _jobRepository.SaveAsync(
            job,
            cancellationToken);

        return true;
    }

    private static void ValidateId(
        Guid value,
        string parameterName,
        string message)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException(
                message,
                parameterName);
        }
    }
}
