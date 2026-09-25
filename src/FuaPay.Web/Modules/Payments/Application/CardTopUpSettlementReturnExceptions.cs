namespace FuaPay.Web.Modules.Payments.Application;

public sealed class CardTopUpSettlementReturnNotAvailableException :
    InvalidOperationException
{
    public CardTopUpSettlementReturnNotAvailableException()
        : base("Card top-up returns are not available.")
    {
    }
}

public sealed class CardTopUpSettlementReturnNotAllowedException :
    InvalidOperationException
{
    public CardTopUpSettlementReturnNotAllowedException(
        Guid originalPaymentId,
        string reason)
        : base($"Card top-up payment '{originalPaymentId}' cannot be returned: {reason}.")
    {
        OriginalPaymentId = originalPaymentId;
    }

    public Guid OriginalPaymentId { get; }
}

public sealed class CardTopUpSettlementReturnStateInconsistentException :
    InvalidOperationException
{
    public CardTopUpSettlementReturnStateInconsistentException(
        Guid requestId,
        string reason)
        : base($"Card top-up return '{requestId}' has inconsistent durable state: {reason}.")
    {
        RequestId = requestId;
    }

    public Guid RequestId { get; }
}

public sealed class CardTopUpSettlementReturnSafetyStateException :
    InvalidOperationException
{
    public CardTopUpSettlementReturnSafetyStateException(
        Guid requestId,
        Exception innerException)
        : base(
            $"Card top-up return '{requestId}' could not persist its safety state after an ambiguous provider outcome.",
            innerException)
    {
        RequestId = requestId;
    }

    public Guid RequestId { get; }
}
