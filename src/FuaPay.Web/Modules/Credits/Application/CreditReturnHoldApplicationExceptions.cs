using FuaPay.Web.BuildingBlocks.Domain;

namespace FuaPay.Web.Modules.Credits.Application;

public sealed class CreditReturnHoldAlreadyExistsException :
    InvalidOperationException
{
    public CreditReturnHoldAlreadyExistsException(
        Guid settlementReturnId,
        Exception? innerException = null)
        : base(
            $"Settlement return '{settlementReturnId}' already has a " +
            "credit hold.",
            innerException)
    {
        SettlementReturnId = settlementReturnId;
    }

    public Guid SettlementReturnId { get; }
}

public sealed class CreditReturnHoldConflictException :
    InvalidOperationException
{
    public CreditReturnHoldConflictException(Guid settlementReturnId)
        : base(
            $"Settlement return '{settlementReturnId}' already has a " +
            "credit hold with different data.")
    {
        SettlementReturnId = settlementReturnId;
    }

    public Guid SettlementReturnId { get; }
}

public sealed class InsufficientAvailableCreditForReturnHoldException :
    InvalidOperationException
{
    public InsufficientAvailableCreditForReturnHoldException(
        Guid ownerId,
        Money requestedAmount,
        Money availableAmount)
        : base(
            $"Credit owner '{ownerId}' does not have the full amount " +
            "available for a settlement return hold.")
    {
        OwnerId = ownerId;
        RequestedAmount = requestedAmount;
        AvailableAmount = availableAmount;
    }

    public Guid OwnerId { get; }

    public Money RequestedAmount { get; }

    public Money AvailableAmount { get; }
}

public sealed class CreditReturnHoldConcurrencyException :
    InvalidOperationException
{
    public CreditReturnHoldConcurrencyException(
        Guid settlementReturnId,
        Exception? innerException = null)
        : base(
            $"Credit return hold for settlement return " +
            $"'{settlementReturnId}' was changed concurrently.",
            innerException)
    {
        SettlementReturnId = settlementReturnId;
    }

    public Guid SettlementReturnId { get; }
}
