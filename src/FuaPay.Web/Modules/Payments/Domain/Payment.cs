using FuaPay.Web.BuildingBlocks.Domain;

namespace FuaPay.Web.Modules.Payments.Domain;

public sealed class Payment
{
    public Payment(
        Guid id,
        Guid customerUserId,
        PaymentPurposeType purposeType,
        Guid? jobId,
        Money amount,
        PaymentProvider provider,
        DateTimeOffset createdAt,
        Guid? creationRequestId = null)
    {
        ValidateId(id, nameof(id), "ID platby nesmí být prázdné.");
        ValidateId(customerUserId, nameof(customerUserId), "ID zákazníka nesmí být prázdné.");
        ValidatePurpose(purposeType, jobId);
        ValidateProvider(provider);
        ValidateCreationRequest(purposeType, creationRequestId);

        var amountRange = purposeType == PaymentPurposeType.CreditTopUp
            ? FinancialAmountPolicy.CreditTopUp
            : FinancialAmountPolicy.JobPrice;

        if (!amountRange.Contains(amount))
        {
            throw new ArgumentOutOfRangeException(
                nameof(amount),
                "Částka platby je mimo povolený rozsah.");
        }

        if (createdAt == default)
        {
            throw new ArgumentException(
                "Čas vytvoření platby nesmí být prázdný.",
                nameof(createdAt));
        }

        Id = id;
        CustomerUserId = customerUserId;
        PurposeType = purposeType;
        JobId = jobId;
        Amount = amount;
        Provider = provider;
        CreationRequestId = creationRequestId;
        Status = PaymentStatus.Created;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
    }

    public Guid Id { get; }

    public Guid CustomerUserId { get; }

    public PaymentPurposeType PurposeType { get; }

    public Guid? JobId { get; }

    public Money Amount { get; }

    public PaymentProvider Provider { get; }

    public Guid? CreationRequestId { get; }

    public PaymentStatus Status { get; private set; }

    public string? ProviderReference { get; private set; }

    public string? FailureReason { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public void MarkPending(
        string providerReference,
        DateTimeOffset changedAt)
    {
        EnsureStatus(PaymentStatus.Created, PaymentStatus.Pending);

        ProviderReference = PaymentProviderReference.Normalize(
            providerReference,
            nameof(providerReference));
        Status = PaymentStatus.Pending;
        UpdatedAt = ValidateChangedAt(changedAt);
    }

    public bool Complete(DateTimeOffset completedAt)
    {
        if (Status == PaymentStatus.Succeeded)
        {
            return false;
        }

        EnsureStatus(PaymentStatus.Pending, PaymentStatus.Succeeded);
        Status = PaymentStatus.Succeeded;
        UpdatedAt = ValidateChangedAt(completedAt);
        CompletedAt = completedAt;
        FailureReason = null;
        return true;
    }

    public bool Fail(
        string reason,
        DateTimeOffset failedAt)
    {
        if (Status == PaymentStatus.Failed)
        {
            return false;
        }

        EnsureStatus(PaymentStatus.Pending, PaymentStatus.Failed);

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException(
                "Důvod neúspěšné platby nesmí být prázdný.",
                nameof(reason));
        }

        var normalizedReason = reason.Trim();

        if (normalizedReason.Length > 500)
        {
            throw new ArgumentException(
                "Důvod neúspěšné platby je příliš dlouhý.",
                nameof(reason));
        }

        Status = PaymentStatus.Failed;
        FailureReason = normalizedReason;
        UpdatedAt = ValidateChangedAt(failedAt);
        CompletedAt = failedAt;
        return true;
    }

    public bool Expire(DateTimeOffset expiredAt)
    {
        if (Status == PaymentStatus.Expired)
        {
            return false;
        }

        EnsureStatus(PaymentStatus.Pending, PaymentStatus.Expired);
        Status = PaymentStatus.Expired;
        UpdatedAt = ValidateChangedAt(expiredAt);
        CompletedAt = expiredAt;
        return true;
    }

    public bool Cancel(DateTimeOffset cancelledAt)
    {
        if (Status == PaymentStatus.Cancelled)
        {
            return false;
        }

        if (
            Status != PaymentStatus.Created &&
            Status != PaymentStatus.Pending)
        {
            throw new InvalidPaymentStateTransitionException(
                Status,
                PaymentStatus.Cancelled);
        }

        Status = PaymentStatus.Cancelled;
        UpdatedAt = ValidateChangedAt(cancelledAt);
        CompletedAt = cancelledAt;
        return true;
    }

    private void EnsureStatus(
        PaymentStatus required,
        PaymentStatus target)
    {
        if (Status != required)
        {
            throw new InvalidPaymentStateTransitionException(
                Status,
                target);
        }
    }

    private DateTimeOffset ValidateChangedAt(DateTimeOffset changedAt)
    {
        if (changedAt < UpdatedAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(changedAt),
                "Nový stav platby nesmí předcházet poslední změně.");
        }

        return changedAt;
    }

    private static void ValidatePurpose(
        PaymentPurposeType purposeType,
        Guid? jobId)
    {
        if (
            purposeType == PaymentPurposeType.Unknown ||
            !Enum.IsDefined(purposeType))
        {
            throw new ArgumentOutOfRangeException(nameof(purposeType));
        }

        if (
            (purposeType == PaymentPurposeType.Job &&
             (!jobId.HasValue || jobId.Value == Guid.Empty)) ||
            (purposeType == PaymentPurposeType.CreditTopUp &&
             jobId.HasValue))
        {
            throw new ArgumentException(
                "Vazba platby na zakázku neodpovídá jejímu účelu.",
                nameof(jobId));
        }
    }

    private static void ValidateProvider(PaymentProvider provider)
    {
        if (
            provider == PaymentProvider.Unknown ||
            !Enum.IsDefined(provider))
        {
            throw new ArgumentOutOfRangeException(nameof(provider));
        }
    }

    private static void ValidateCreationRequest(
        PaymentPurposeType purposeType,
        Guid? creationRequestId)
    {
        if (
            purposeType == PaymentPurposeType.CreditTopUp &&
            (!creationRequestId.HasValue ||
             creationRequestId.Value == Guid.Empty))
        {
            throw new ArgumentException(
                "Dobití kreditu musí mít platný creation request ID.",
                nameof(creationRequestId));
        }

        if (
            purposeType == PaymentPurposeType.Job &&
            creationRequestId.HasValue)
        {
            throw new ArgumentException(
                "Platba zakázky nesmí používat top-up creation request ID.",
                nameof(creationRequestId));
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
}
