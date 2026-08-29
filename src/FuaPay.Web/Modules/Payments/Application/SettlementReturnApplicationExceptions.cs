namespace FuaPay.Web.Modules.Payments.Application;

public sealed class SettlementReturnRequestAlreadyExistsException :
    InvalidOperationException
{
    public SettlementReturnRequestAlreadyExistsException(
        Guid requestId,
        Exception? innerException = null)
        : base(
            $"Settlement return request '{requestId}' already exists.",
            innerException)
    {
        RequestId = requestId;
    }

    public Guid RequestId { get; }
}

public sealed class SettlementReturnOriginalPaymentAlreadyExistsException :
    InvalidOperationException
{
    public SettlementReturnOriginalPaymentAlreadyExistsException(
        Guid originalPaymentId,
        Exception? innerException = null)
        : base(
            $"Original payment '{originalPaymentId}' already has a " +
            "settlement return.",
            innerException)
    {
        OriginalPaymentId = originalPaymentId;
    }

    public Guid OriginalPaymentId { get; }
}

public sealed class SettlementReturnJobAlreadyExistsException :
    InvalidOperationException
{
    public SettlementReturnJobAlreadyExistsException(
        Guid jobId,
        Exception? innerException = null)
        : base(
            $"Job '{jobId}' already has a settlement return.",
            innerException)
    {
        JobId = jobId;
    }

    public Guid JobId { get; }
}

public sealed class SettlementReturnRequestConflictException :
    InvalidOperationException
{
    public SettlementReturnRequestConflictException(Guid requestId)
        : base(
            $"Settlement return request '{requestId}' was already used " +
            "with different data.")
    {
        RequestId = requestId;
    }

    public Guid RequestId { get; }
}

public sealed class SettlementReturnSourceConflictException :
    InvalidOperationException
{
    public SettlementReturnSourceConflictException(
        Guid requestId,
        Guid existingSettlementReturnId)
        : base(
            $"Settlement return request '{requestId}' conflicts with " +
            $"existing settlement return '{existingSettlementReturnId}'.")
    {
        RequestId = requestId;
        ExistingSettlementReturnId = existingSettlementReturnId;
    }

    public Guid RequestId { get; }

    public Guid ExistingSettlementReturnId { get; }
}

public sealed class SettlementReturnConcurrencyException :
    InvalidOperationException
{
    public SettlementReturnConcurrencyException(
        Guid settlementReturnId,
        Exception? innerException = null)
        : base(
            $"Settlement return '{settlementReturnId}' was changed " +
            "concurrently.",
            innerException)
    {
        SettlementReturnId = settlementReturnId;
    }

    public Guid SettlementReturnId { get; }
}
