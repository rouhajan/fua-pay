using FuaPay.Web.BuildingBlocks.Application;
using FuaPay.Web.BuildingBlocks.Auditing;
using FuaPay.Web.BuildingBlocks.Domain;
using FuaPay.Web.Modules.Jobs.Application;
using FuaPay.Web.Modules.Jobs.Domain;
using FuaPay.Web.Modules.Payments.Domain;

namespace FuaPay.Web.Modules.Payments.Application;

public sealed class PaymentCreationService
{
    private readonly IPaymentRepository _repository;
    private readonly IJobQueries _jobQueries;
    private readonly IJobPaymentCoordination _jobPaymentCoordination;
    private readonly IApplicationTransaction _transaction;
    private readonly TimeProvider _timeProvider;
    private readonly IAuditTrail _auditTrail;
    private readonly IPaymentOrderNumberAllocator _orderNumberAllocator;
    private readonly IPaymentProviderInitiator _providerInitiator;
    private readonly PaymentInitiationService _initiationService;

    public PaymentCreationService(
        IPaymentRepository repository,
        IJobQueries jobQueries,
        IJobPaymentCoordination jobPaymentCoordination,
        IApplicationTransaction transaction,
        TimeProvider timeProvider,
        IAuditTrail auditTrail,
        IPaymentOrderNumberAllocator orderNumberAllocator,
        IPaymentProviderInitiator providerInitiator,
        PaymentInitiationService initiationService)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(jobQueries);
        ArgumentNullException.ThrowIfNull(jobPaymentCoordination);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(auditTrail);
        ArgumentNullException.ThrowIfNull(orderNumberAllocator);
        ArgumentNullException.ThrowIfNull(providerInitiator);
        ArgumentNullException.ThrowIfNull(initiationService);
        _repository = repository;
        _jobQueries = jobQueries;
        _jobPaymentCoordination = jobPaymentCoordination;
        _transaction = transaction;
        _timeProvider = timeProvider;
        _auditTrail = auditTrail;
        _orderNumberAllocator = orderNumberAllocator;
        _providerInitiator = providerInitiator;
        _initiationService = initiationService;
    }

    public async Task<Payment> CreateCreditTopUpAsync(
        Guid creationRequestId,
        Guid customerUserId,
        Money amount,
        CancellationToken cancellationToken = default)
    {
        ValidateId(
            creationRequestId,
            nameof(creationRequestId),
            "Creation request ID nesmí být prázdné.");
        ValidateCustomerUserId(customerUserId);

        var existing = await _repository.FindByCreationRequestIdAsync(
            creationRequestId,
            cancellationToken);

        if (existing is not null)
        {
            return await ResolveTopUpReplayAsync(
                creationRequestId,
                customerUserId,
                amount,
                existing,
                cancellationToken);
        }

        if (!FinancialAmountPolicy.CreditTopUp.Contains(amount))
        {
            throw new PaymentAmountNotAllowedException();
        }

        try
        {
            return await CreateAndInitializePreparedPaymentAsync(
                customerUserId,
                PaymentPurposeType.CreditTopUp,
                jobId: null,
                amount,
                creationRequestId,
                cancellationToken);
        }
        catch (PaymentCreationRequestAlreadyExistsException)
        {
            var concurrent =
                await _repository.FindByCreationRequestIdAsync(
                    creationRequestId,
                    cancellationToken);

            if (concurrent is null)
            {
                throw;
            }

            return await ResolveTopUpReplayAsync(
                creationRequestId,
                customerUserId,
                amount,
                concurrent,
                cancellationToken);
        }
    }

    public async Task<Payment> CreateJobPaymentAsync(
        Guid customerUserId,
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        ValidateCustomerUserId(customerUserId);

        if (jobId == Guid.Empty)
        {
            throw new ArgumentException(
                "ID zakázky nesmí být prázdné.",
                nameof(jobId));
        }

        Payment preparedPayment;
        try
        {
            preparedPayment = await _transaction.ExecuteAsync(
                transactionCancellationToken =>
                    PrepareJobPaymentInsideTransactionAsync(
                        customerUserId,
                        jobId,
                        transactionCancellationToken),
                cancellationToken);
        }
        catch (PaymentConcurrencyException)
        {
            var concurrentPayment =
                await _repository.FindBlockingForJobAsync(
                    jobId,
                    cancellationToken);

            if (
                concurrentPayment is not null &&
                concurrentPayment.CustomerUserId == customerUserId)
            {
                return (await _initiationService.InitializeIfPreparedAsync(
                    concurrentPayment,
                    cancellationToken)).Payment;
            }

            throw new BlockingJobPaymentAlreadyExistsException(jobId);
        }

        return (await _initiationService.InitializeIfPreparedAsync(
            preparedPayment,
            cancellationToken)).Payment;
    }

    private async Task<Payment> PrepareJobPaymentInsideTransactionAsync(
        Guid customerUserId,
        Guid jobId,
        CancellationToken cancellationToken)
    {
        var wasLocked = await _jobPaymentCoordination.LockJobAsync(
            jobId,
            cancellationToken);

        if (!wasLocked)
        {
            throw new JobNotFoundException(jobId);
        }

        var job = await _jobQueries.FindForCustomerAsync(
            customerUserId,
            jobId,
            cancellationToken)
            ?? throw new JobNotFoundException(jobId);

        if (
            job.ProductionStatus != JobProductionStatus.Published ||
            job.PaymentStatus != JobPaymentStatus.Unpaid)
        {
            throw new JobSettlementNotAllowedException(
                job.ProductionStatus);
        }

        var existing = await _repository.FindBlockingForJobAsync(
            jobId,
            cancellationToken);

        if (existing is not null)
        {
            if (existing.CustomerUserId != customerUserId)
            {
                throw new PaymentAccessDeniedException(
                    existing.Id,
                    customerUserId);
            }

            return existing;
        }

        return await AddPreparedPaymentAsync(
            customerUserId,
            PaymentPurposeType.Job,
            job.Id,
            new Money(job.PriceMinorUnits),
            creationRequestId: null,
            cancellationToken);
    }

    private async Task<Payment> CreateAndInitializePreparedPaymentAsync(
        Guid customerUserId,
        PaymentPurposeType purposeType,
        Guid? jobId,
        Money amount,
        Guid? creationRequestId,
        CancellationToken cancellationToken)
    {
        var payment = await AddPreparedPaymentAsync(
            customerUserId,
            purposeType,
            jobId,
            amount,
            creationRequestId,
            cancellationToken);

        return (await _initiationService.InitializeAsync(
            payment.Id,
            cancellationToken)).Payment;
    }

    private async Task<Payment> AddPreparedPaymentAsync(
        Guid customerUserId,
        PaymentPurposeType purposeType,
        Guid? jobId,
        Money amount,
        Guid? creationRequestId,
        CancellationToken cancellationToken)
    {
        _providerInitiator.EnsureAvailable();

        var orderNumber = await _orderNumberAllocator.AllocateAsync(
            cancellationToken);
        var now = _timeProvider.GetUtcNow();
        var payment = new Payment(
            Guid.NewGuid(),
            customerUserId,
            purposeType,
            jobId,
            amount,
            _providerInitiator.Provider,
            now,
            creationRequestId);
        var initiation = new PaymentInitiation(
            payment.Id,
            payment.Provider,
            orderNumber,
            PaymentProviderCorrelation.CreateCorrelationId(),
            now);

        _auditTrail.Stage(AuditEntry.ForUser(
            customerUserId,
            "payment.created",
            "payment",
            payment.Id.ToString(),
            $"Byla vytvořena platba {payment.Id} pro účel {purposeType} " +
            $"ve výši {amount.MinorUnits} haléřů s orderNo {orderNumber}" +
            (creationRequestId.HasValue
                ? $" z creation requestu {creationRequestId.Value}."
                : "."),
            now));

        await _repository.AddPreparedAsync(
            payment,
            initiation,
            cancellationToken);

        return payment;
    }

    private static void ValidateCustomerUserId(Guid customerUserId)
    {
        if (customerUserId == Guid.Empty)
        {
            throw new ArgumentException(
                "ID zákazníka nesmí být prázdné.",
                nameof(customerUserId));
        }
    }

    private static void ValidateId(
        Guid value,
        string parameterName,
        string message)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException(message, parameterName);
        }
    }

    private async Task<Payment> ResolveTopUpReplayAsync(
        Guid creationRequestId,
        Guid customerUserId,
        Money amount,
        Payment existing,
        CancellationToken cancellationToken)
    {
        if (
            existing.CreationRequestId != creationRequestId ||
            existing.CustomerUserId != customerUserId ||
            existing.PurposeType != PaymentPurposeType.CreditTopUp ||
            existing.JobId.HasValue ||
            existing.Amount != amount)
        {
            throw new PaymentCreationRequestConflictException(
                creationRequestId);
        }

        return (await _initiationService.InitializeIfPreparedAsync(
            existing,
            cancellationToken)).Payment;
    }
}
