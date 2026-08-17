using FuaPay.Web.BuildingBlocks.Auditing;
using FuaPay.Web.Modules.Payments.Domain;

namespace FuaPay.Web.Modules.Payments.Application;

public sealed class DevelopmentPaymentService
{
    private readonly IPaymentRepository _repository;
    private readonly IPaymentSettlementService _settlementService;
    private readonly TimeProvider _timeProvider;
    private readonly IAuditTrail _auditTrail;
    private readonly DevelopmentPaymentAvailability _availability;

    public DevelopmentPaymentService(
        IPaymentRepository repository,
        IPaymentSettlementService settlementService,
        TimeProvider timeProvider,
        IAuditTrail auditTrail,
        DevelopmentPaymentAvailability availability)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(settlementService);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(auditTrail);
        ArgumentNullException.ThrowIfNull(availability);

        _repository = repository;
        _settlementService = settlementService;
        _timeProvider = timeProvider;
        _auditTrail = auditTrail;
        _availability = availability;
    }

    public async Task<bool> CompleteAsync(
        Guid customerUserId,
        Guid paymentId,
        CancellationToken cancellationToken = default)
    {
        _availability.EnsureEnabled();

        var payment = await RequireOwnedDevelopmentPaymentAsync(
            customerUserId,
            paymentId,
            cancellationToken);

        var providerReference = payment.ProviderReference
            ?? throw new InvalidDataException(
                $"Vývojová platba '{payment.Id}' nemá " +
                "referenci poskytovatele.");

        return await _settlementService.CompleteAsync(
            new VerifiedPaymentConfirmation(
                payment.Provider,
                providerReference,
                payment.Amount),
            cancellationToken);
    }

    public async Task<bool> FailAsync(
        Guid customerUserId,
        Guid paymentId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        _availability.EnsureEnabled();

        var payment = await RequireOwnedDevelopmentPaymentAsync(
            customerUserId,
            paymentId,
            cancellationToken);

        var occurredAt = _timeProvider.GetUtcNow();
        var changed = payment.Fail(
            reason,
            occurredAt);

        if (changed)
        {
            _auditTrail.Stage(AuditEntry.ForUser(
                customerUserId,
                "payment.failed",
                "payment",
                payment.Id.ToString(),
                $"Vývojová platba {payment.Id} byla označena " +
                $"jako neúspěšná: {payment.FailureReason}.",
                occurredAt));

            await _repository.SaveAsync(
                payment,
                cancellationToken);
        }

        return changed;
    }

    public async Task<bool> CancelAsync(
        Guid customerUserId,
        Guid paymentId,
        CancellationToken cancellationToken = default)
    {
        _availability.EnsureEnabled();

        var payment = await RequireOwnedDevelopmentPaymentAsync(
            customerUserId,
            paymentId,
            cancellationToken);

        var occurredAt = _timeProvider.GetUtcNow();
        var changed = payment.Cancel(occurredAt);

        if (changed)
        {
            _auditTrail.Stage(AuditEntry.ForUser(
                customerUserId,
                "payment.cancelled",
                "payment",
                payment.Id.ToString(),
                $"Vývojová platba {payment.Id} byla zrušena.",
                occurredAt));

            await _repository.SaveAsync(
                payment,
                cancellationToken);
        }

        return changed;
    }

    private async Task<Payment> RequireOwnedDevelopmentPaymentAsync(
        Guid customerUserId,
        Guid paymentId,
        CancellationToken cancellationToken)
    {
        ValidateId(
            customerUserId,
            nameof(customerUserId),
            "ID zákazníka nesmí být prázdné.");
        ValidateId(
            paymentId,
            nameof(paymentId),
            "ID platby nesmí být prázdné.");

        var payment = await _repository.FindByIdAsync(
            paymentId,
            cancellationToken)
            ?? throw new PaymentNotFoundException(paymentId);

        if (payment.CustomerUserId != customerUserId)
        {
            throw new PaymentAccessDeniedException(
                paymentId,
                customerUserId);
        }

        if (payment.Provider != PaymentProvider.Development)
        {
            throw new DevelopmentPaymentProviderMismatchException(
                payment.Id,
                payment.Provider);
        }

        return payment;
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
