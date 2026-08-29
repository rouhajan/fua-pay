namespace FuaPay.Web.Modules.Payments.Application;

public sealed class SettlementReturnProviderAttemptAlreadyExistsException :
    InvalidOperationException
{
    public SettlementReturnProviderAttemptAlreadyExistsException(
        Guid attemptId,
        Exception? innerException = null)
        : base(
            $"Settlement return provider attempt '{attemptId}' already " +
            "exists.",
            innerException)
    {
        AttemptId = attemptId;
    }

    public Guid AttemptId { get; }
}

public sealed class SettlementReturnProviderAttemptAlreadyActiveException :
    InvalidOperationException
{
    public SettlementReturnProviderAttemptAlreadyActiveException(
        Guid settlementReturnId,
        Guid? activeAttemptId = null,
        Exception? innerException = null)
        : base(
            activeAttemptId.HasValue
                ? $"Settlement return '{settlementReturnId}' already has " +
                  $"active provider attempt '{activeAttemptId.Value}'."
                : $"Settlement return '{settlementReturnId}' already has " +
                  "an active provider attempt.",
            innerException)
    {
        SettlementReturnId = settlementReturnId;
        ActiveAttemptId = activeAttemptId;
    }

    public Guid SettlementReturnId { get; }

    public Guid? ActiveAttemptId { get; }
}

public sealed class SettlementReturnProviderAttemptConflictException :
    InvalidOperationException
{
    public SettlementReturnProviderAttemptConflictException(Guid attemptId)
        : base(
            $"Settlement return provider attempt ID '{attemptId}' was " +
            "already used with different immutable data.")
    {
        AttemptId = attemptId;
    }

    public Guid AttemptId { get; }
}

public sealed class SettlementReturnProviderAttemptNotFoundException :
    InvalidOperationException
{
    public SettlementReturnProviderAttemptNotFoundException(Guid attemptId)
        : base(
            $"Settlement return provider attempt '{attemptId}' was not " +
            "found.")
    {
        AttemptId = attemptId;
    }

    public Guid AttemptId { get; }
}

public sealed class SettlementReturnProviderAttemptNotAllowedException :
    InvalidOperationException
{
    public SettlementReturnProviderAttemptNotAllowedException(
        Guid settlementReturnId,
        string reason)
        : base(
            $"Settlement return '{settlementReturnId}' cannot start a " +
            $"provider attempt: {reason}.")
    {
        SettlementReturnId = settlementReturnId;
    }

    public Guid SettlementReturnId { get; }
}

public sealed class SettlementReturnProviderAttemptConcurrencyException :
    InvalidOperationException
{
    public SettlementReturnProviderAttemptConcurrencyException(
        Guid attemptId,
        Exception? innerException = null)
        : base(
            $"Settlement return provider attempt '{attemptId}' was " +
            "changed concurrently.",
            innerException)
    {
        AttemptId = attemptId;
    }

    public Guid AttemptId { get; }
}
