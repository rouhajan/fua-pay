using FuaPay.Web.BuildingBlocks.Application;
using FuaPay.Web.BuildingBlocks.Auditing;
using FuaPay.Web.BuildingBlocks.Notifications;
using FuaPay.Web.Modules.Credits.Application;
using FuaPay.Web.Modules.Credits.Domain;
using FuaPay.Web.Modules.Jobs.Application;
using FuaPay.Web.Modules.Jobs.Domain;
using FuaPay.Web.Modules.Payments.Domain;

namespace FuaPay.Web.Modules.Payments.Application;

public sealed class PaymentSettlementService :
    IPaymentSettlementService
{
    private readonly IPaymentRepository _repository;
    private readonly CreditService _creditService;
    private readonly JobSettlementService _jobSettlementService;
    private readonly IApplicationTransaction _transaction;
    private readonly TimeProvider _timeProvider;
    private readonly IAuditTrail _auditTrail;
    private readonly INotificationOutbox _notificationOutbox;

    public PaymentSettlementService(
        IPaymentRepository repository,
        CreditService creditService,
        JobSettlementService jobSettlementService,
        IApplicationTransaction transaction,
        TimeProvider timeProvider,
        IAuditTrail auditTrail,
        INotificationOutbox notificationOutbox)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(creditService);
        ArgumentNullException.ThrowIfNull(jobSettlementService);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(auditTrail);
        ArgumentNullException.ThrowIfNull(notificationOutbox);

        _repository = repository;
        _creditService = creditService;
        _jobSettlementService = jobSettlementService;
        _transaction = transaction;
        _timeProvider = timeProvider;
        _auditTrail = auditTrail;
        _notificationOutbox = notificationOutbox;
    }

    public async Task<bool> CompleteAsync(
        VerifiedPaymentConfirmation confirmation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(confirmation);

        try
        {
            return await _transaction.ExecuteAsync(
                ct => CompleteInsideTransactionAsync(
                    confirmation,
                    ct),
                cancellationToken);
        }
        catch (Exception exception)
            when (IsConcurrentCompletion(exception))
        {
            var persisted =
                await _repository.FindByProviderReferenceAsync(
                    confirmation.Provider,
                    confirmation.ProviderReference,
                    cancellationToken);

            if (persisted is null)
            {
                throw;
            }

            EnsureConfirmationMatches(
                persisted,
                confirmation);

            if (persisted.Status == PaymentStatus.Succeeded)
            {
                return false;
            }

            throw;
        }
    }

    private async Task<bool> CompleteInsideTransactionAsync(
        VerifiedPaymentConfirmation confirmation,
        CancellationToken cancellationToken)
    {
        var payment =
            await _repository.FindByProviderReferenceAsync(
                confirmation.Provider,
                confirmation.ProviderReference,
                cancellationToken)
            ?? throw new PaymentProviderReferenceNotFoundException(
                confirmation.Provider,
                confirmation.ProviderReference);

        EnsureConfirmationMatches(payment, confirmation);

        if (payment.Status == PaymentStatus.Succeeded)
        {
            return false;
        }

        if (payment.Status != PaymentStatus.Pending)
        {
            throw new InvalidPaymentStateTransitionException(
                payment.Status,
                PaymentStatus.Succeeded);
        }

        if (payment.PurposeType == PaymentPurposeType.CreditTopUp)
        {
            await _creditService.CreditAsync(
                payment.CustomerUserId,
                payment.Id,
                payment.Amount,
                $"Dobití kreditu {payment.ProviderReference}",
                cancellationToken);
        }
        else
        {
            await SettleJobAsync(
                payment,
                cancellationToken);
        }

        var completedAt = _timeProvider.GetUtcNow();
        payment.Complete(completedAt);
        StageCompletedAudit(payment, completedAt);
        await _repository.SaveAsync(payment, cancellationToken);
        return true;
    }

    private Task SettleJobAsync(
        Payment payment,
        CancellationToken cancellationToken)
    {
        return _jobSettlementService.ConfirmAsync(
            payment.JobId!.Value,
            JobSettlementType.DirectPayment,
            payment.Id,
            cancellationToken);
    }

    private static void EnsureConfirmationMatches(
        Payment payment,
        VerifiedPaymentConfirmation confirmation)
    {
        if (
            payment.Provider != confirmation.Provider ||
            !string.Equals(
                payment.ProviderReference,
                confirmation.ProviderReference,
                StringComparison.Ordinal) ||
            payment.Amount != confirmation.Amount)
        {
            throw new PaymentConfirmationMismatchException(
                payment.Id);
        }
    }

    private void StageCompletedAudit(
        Payment payment,
        DateTimeOffset occurredAt)
    {
        _auditTrail.Stage(AuditEntry.ForProcess(
            "payment-provider",
            "payment.succeeded",
            "payment",
            payment.Id.ToString(),
            $"Platba {payment.Id} od poskytovatele " +
            $"{payment.Provider} byla úspěšně vypořádána.",
            occurredAt));

        _notificationOutbox.Stage(NotificationMessage.Create(
            payment.CustomerUserId,
            "payment.succeeded",
            payment.PurposeType == PaymentPurposeType.CreditTopUp
                ? "Kredit byl dobit"
                : "Platba zakázky byla potvrzena",
            $"Platba {payment.ProviderReference} ve výši " +
            $"{payment.Amount.ToCrowns():0.00} Kč " +
            "byla potvrzena.",
            occurredAt));
    }

    private static bool IsConcurrentCompletion(
        Exception exception)
    {
        return exception is
            PaymentConcurrencyException or
            CreditAccountConcurrencyException or
            DuplicateCreditOperationException or
            JobConcurrencyException or
            JobSettlementReferenceAlreadyUsedException;
    }
}
