using System.Globalization;

using FuaPay.Web.BuildingBlocks.Application;
using FuaPay.Web.BuildingBlocks.Auditing;
using FuaPay.Web.BuildingBlocks.Domain;
using FuaPay.Web.BuildingBlocks.Notifications;
using FuaPay.Web.Modules.Access.Application;
using FuaPay.Web.Modules.Credits.Application;
using FuaPay.Web.Modules.Credits.Domain;
using FuaPay.Web.Modules.FinancialDocuments.Application;
using FuaPay.Web.Modules.FinancialDocuments.Domain;
using FuaPay.Web.Modules.Jobs.Application;
using FuaPay.Web.Modules.Jobs.Domain;
using FuaPay.Web.Modules.Payments.Domain;
using FuaPay.Web.Modules.ServiceUnits.Application;

namespace FuaPay.Web.Modules.Payments.Application;

public sealed class PaymentSettlementService :
    IPaymentSettlementService
{
    private readonly IPaymentRepository _repository;
    private readonly IPaymentInitiationRepository _initiationRepository;
    private readonly CreditService _creditService;
    private readonly JobSettlementService _jobSettlementService;
    private readonly IAccessUserQueries _accessUserQueries;
    private readonly IServiceUnitRepository _serviceUnitRepository;
    private readonly IFinancialDocumentRepository _documentRepository;
    private readonly IFinancialDocumentNumberAllocator _documentNumbers;
    private readonly IFinancialDocumentIssuanceProfile _issuanceProfile;
    private readonly IApplicationTransaction _transaction;
    private readonly TimeProvider _timeProvider;
    private readonly IAuditTrail _auditTrail;
    private readonly INotificationOutbox _notificationOutbox;

    public PaymentSettlementService(
        IPaymentRepository repository,
        IPaymentInitiationRepository initiationRepository,
        CreditService creditService,
        JobSettlementService jobSettlementService,
        IAccessUserQueries accessUserQueries,
        IServiceUnitRepository serviceUnitRepository,
        IFinancialDocumentRepository documentRepository,
        IFinancialDocumentNumberAllocator documentNumbers,
        IFinancialDocumentIssuanceProfile issuanceProfile,
        IApplicationTransaction transaction,
        TimeProvider timeProvider,
        IAuditTrail auditTrail,
        INotificationOutbox notificationOutbox)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(initiationRepository);
        ArgumentNullException.ThrowIfNull(creditService);
        ArgumentNullException.ThrowIfNull(jobSettlementService);
        ArgumentNullException.ThrowIfNull(accessUserQueries);
        ArgumentNullException.ThrowIfNull(serviceUnitRepository);
        ArgumentNullException.ThrowIfNull(documentRepository);
        ArgumentNullException.ThrowIfNull(documentNumbers);
        ArgumentNullException.ThrowIfNull(issuanceProfile);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(auditTrail);
        ArgumentNullException.ThrowIfNull(notificationOutbox);

        _repository = repository;
        _initiationRepository = initiationRepository;
        _creditService = creditService;
        _jobSettlementService = jobSettlementService;
        _accessUserQueries = accessUserQueries;
        _serviceUnitRepository = serviceUnitRepository;
        _documentRepository = documentRepository;
        _documentNumbers = documentNumbers;
        _issuanceProfile = issuanceProfile;
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

        CreditMovement? movement = null;
        Job? settledJob = null;

        if (payment.PurposeType == PaymentPurposeType.CreditTopUp)
        {
            movement = await _creditService.CreditAsync(
                payment.CustomerUserId,
                payment.Id,
                payment.Amount,
                $"Dobití kreditu {payment.ProviderReference}",
                cancellationToken);
        }
        else
        {
            settledJob = await SettleJobAsync(
                payment,
                cancellationToken);
        }

        var documentInput = await CollectDocumentInputAsync(
            payment,
            movement,
            settledJob,
            cancellationToken);

        var completedAt = _timeProvider.GetUtcNow();
        payment.Complete(completedAt);
        StageCompletedAudit(payment, completedAt);

        if (documentInput is not null)
        {
            await IssueDocumentAsync(
                payment,
                documentInput,
                cancellationToken);
        }

        await _repository.SaveAsync(payment, cancellationToken);
        return true;
    }

    private async Task<Job> SettleJobAsync(
        Payment payment,
        CancellationToken cancellationToken)
    {
        var (job, _) = await _jobSettlementService.ConfirmAndGetAsync(
            payment.JobId!.Value,
            JobSettlementType.DirectPayment,
            payment.Id,
            cancellationToken);

        return job;
    }

    private async Task<FinancialDocumentIssuanceInput?>
        CollectDocumentInputAsync(
            Payment payment,
            CreditMovement? movement,
            Job? settledJob,
            CancellationToken cancellationToken)
    {
        if (payment.Provider == PaymentProvider.Development)
        {
            return null;
        }

        if (payment.Provider != PaymentProvider.Csob)
        {
            throw new InvalidOperationException(
                $"Provider '{payment.Provider}' does not have an approved financial-document mapping.");
        }

        var initiation = await _initiationRepository.FindByPaymentIdAsync(
            payment.Id,
            cancellationToken)
            ?? throw new InvalidDataException(
                $"Payment '{payment.Id}' has no persisted initiation for financial-document issuance.");

        if (
            initiation.PaymentId != payment.Id ||
            initiation.Provider != payment.Provider ||
            initiation.State != PaymentInitiationState.Initialized)
        {
            throw new InvalidDataException(
                $"Payment initiation for '{payment.Id}' is not initialized or does not match the settled payment.");
        }

        var users = await _accessUserQueries.FindOptionsAsync(
            [payment.CustomerUserId],
            cancellationToken);

        if (!users.TryGetValue(payment.CustomerUserId, out var user))
        {
            throw new InvalidDataException(
                $"Customer '{payment.CustomerUserId}' does not exist for financial-document issuance.");
        }

        var customer = new FinancialDocumentCustomerSnapshot(
            payment.CustomerUserId,
            user.DisplayName,
            user.Email);
        var provider = new FinancialDocumentProviderSnapshot(
            "Csob",
            payment.ProviderReference
                ?? throw new InvalidDataException(
                    $"Pending payment '{payment.Id}' has no provider reference."),
            initiation.OrderNumber.ToString(CultureInfo.InvariantCulture));

        var issuer = _issuanceProfile.CreateIssuerSnapshot();
        var tax = _issuanceProfile.CreateTaxSnapshot(
            payment.Amount.MinorUnits);

        if (payment.PurposeType == PaymentPurposeType.CreditTopUp)
        {
            ValidateCreditMovement(payment, movement);

            return new FinancialDocumentIssuanceInput(
                customer,
                provider,
                Job: null,
                movement!.RecordedAt,
                issuer,
                tax);
        }

        ValidateSettledJob(payment, settledJob);

        var serviceUnit = await _serviceUnitRepository.FindByIdAsync(
            settledJob!.ServiceUnitId,
            cancellationToken)
            ?? throw new InvalidDataException(
                $"Service unit '{settledJob.ServiceUnitId}' for job '{settledJob.Id}' does not exist.");

        return new FinancialDocumentIssuanceInput(
            customer,
            provider,
            new FinancialDocumentJobSnapshot(
                settledJob.Id,
                settledJob.Number,
                settledJob.Title,
                settledJob.Description,
                serviceUnit.DisplayName),
            settledJob.SettledAt!.Value,
            issuer,
            tax);
    }

    private async Task IssueDocumentAsync(
        Payment payment,
        FinancialDocumentIssuanceInput input,
        CancellationToken cancellationToken)
    {
        var issuedAt = _timeProvider.GetUtcNow();

        if (issuedAt < input.FinancialEventAt)
        {
            issuedAt = input.FinancialEventAt;
        }

        var allocation = await _documentNumbers.AllocateAsync(
            issuedAt,
            cancellationToken);

        var document = payment.PurposeType switch
        {
            PaymentPurposeType.CreditTopUp =>
                FinancialDocument.CreateCardWalletTopUp(
                    Guid.NewGuid(),
                    allocation.DocumentNumber,
                    payment.Id,
                    input.Customer,
                    payment.Amount.MinorUnits,
                    Money.CurrencyCode,
                    input.FinancialEventAt,
                    issuedAt,
                    input.Issuer,
                    input.Tax,
                    input.Provider),

            PaymentPurposeType.Job =>
                FinancialDocument.CreateDirectJobCardPayment(
                    Guid.NewGuid(),
                    allocation.DocumentNumber,
                    payment.Id,
                    input.Customer,
                    payment.Amount.MinorUnits,
                    Money.CurrencyCode,
                    input.FinancialEventAt,
                    issuedAt,
                    input.Issuer,
                    input.Tax,
                    input.Provider,
                    input.Job
                        ?? throw new InvalidOperationException(
                            "Direct job payment is missing its document job snapshot.")),

            _ => throw new InvalidOperationException(
                $"Payment purpose '{payment.PurposeType}' does not support financial-document issuance.")
        };

        _documentRepository.Stage(document);
        await _documentRepository.PersistStagedAsync(
            document,
            cancellationToken);
    }

    private static void ValidateCreditMovement(
        Payment payment,
        CreditMovement? movement)
    {
        if (
            movement is null ||
            movement.OperationId != payment.Id ||
            movement.Type != CreditMovementType.Credit ||
            movement.Amount != payment.Amount)
        {
            throw new InvalidDataException(
                $"Credit effect for payment '{payment.Id}' is not canonical.");
        }
    }

    private static void ValidateSettledJob(
        Payment payment,
        Job? job)
    {
        if (
            job is null ||
            payment.JobId != job.Id ||
            job.CustomerUserId != payment.CustomerUserId ||
            job.Price != payment.Amount ||
            job.PaymentStatus != JobPaymentStatus.Paid ||
            job.SettlementType != JobSettlementType.DirectPayment ||
            job.SettlementReferenceId != payment.Id ||
            !job.SettledAt.HasValue)
        {
            throw new InvalidDataException(
                $"Job effect for payment '{payment.Id}' is not canonical.");
        }
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
            JobSettlementReferenceAlreadyUsedException or
            FinancialDocumentSourceAlreadyExistsException;
    }

    private sealed record FinancialDocumentIssuanceInput(
        FinancialDocumentCustomerSnapshot Customer,
        FinancialDocumentProviderSnapshot Provider,
        FinancialDocumentJobSnapshot? Job,
        DateTimeOffset FinancialEventAt,
        FinancialDocumentIssuerSnapshot Issuer,
        FinancialDocumentTaxSnapshot Tax);
}
