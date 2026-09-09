namespace FuaPay.Web.Modules.Payments.Application;

public sealed class CardJobSettlementReturnNotAvailableException :
    InvalidOperationException
{
    public CardJobSettlementReturnNotAvailableException()
        : base("Card-job provider reversal is not available.")
    {
    }
}

public sealed class CardJobSettlementReturnNotAllowedException :
    InvalidOperationException
{
    public CardJobSettlementReturnNotAllowedException(
        Guid originalPaymentId,
        string reason)
        : base(
            $"Payment '{originalPaymentId}' cannot be reversed: {reason}.")
    {
        OriginalPaymentId = originalPaymentId;
    }

    public Guid OriginalPaymentId { get; }
}

public sealed class CardJobSettlementReturnStateInconsistentException :
    InvalidOperationException
{
    public CardJobSettlementReturnStateInconsistentException(
        Guid operationId,
        string reason)
        : base(
            $"Card-job return operation '{operationId}' has inconsistent " +
            $"durable state: {reason}.")
    {
        OperationId = operationId;
    }

    public Guid OperationId { get; }
}

public sealed class CardJobSettlementReturnSafetyStateException :
    InvalidOperationException
{
    public CardJobSettlementReturnSafetyStateException(
        Guid operationId,
        Exception innerException)
        : base(
            $"Card-job return operation '{operationId}' could not persist " +
            "its safety state after an ambiguous provider outcome.",
            innerException)
    {
        OperationId = operationId;
    }

    public Guid OperationId { get; }
}
