using FuaPay.Web.BuildingBlocks.Domain;

namespace FuaPay.Web.Modules.Credits.Domain;

public sealed class CreditReturnHold
{
    public CreditReturnHold(
        Guid settlementReturnId,
        Guid creditAccountId,
        Money amount,
        DateTimeOffset createdAt)
    {
        ValidateId(
            settlementReturnId,
            nameof(settlementReturnId));
        ValidateId(
            creditAccountId,
            nameof(creditAccountId));

        if (amount.MinorUnits <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(amount),
                "Credit return hold amount must be positive.");
        }

        if (createdAt == default)
        {
            throw new ArgumentException(
                "Credit return hold creation time must not be empty.",
                nameof(createdAt));
        }

        SettlementReturnId = settlementReturnId;
        CreditAccountId = creditAccountId;
        Amount = amount;
        State = CreditReturnHoldState.Active;
        CreatedAt = createdAt;
        StateChangedAt = createdAt;
    }

    public Guid SettlementReturnId { get; }

    public Guid CreditAccountId { get; }

    public Money Amount { get; }

    public CreditReturnHoldState State { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset StateChangedAt { get; private set; }

    public bool Consume(DateTimeOffset changedAt)
    {
        return TransitionTo(
            CreditReturnHoldState.Consumed,
            changedAt);
    }

    public bool Release(DateTimeOffset changedAt)
    {
        return TransitionTo(
            CreditReturnHoldState.Released,
            changedAt);
    }

    internal static CreditReturnHold Restore(
        Guid settlementReturnId,
        Guid creditAccountId,
        Money amount,
        CreditReturnHoldState state,
        DateTimeOffset createdAt,
        DateTimeOffset stateChangedAt)
    {
        var hold = new CreditReturnHold(
            settlementReturnId,
            creditAccountId,
            amount,
            createdAt)
        {
            State = state,
            StateChangedAt = stateChangedAt
        };

        hold.ValidatePersistedState();
        return hold;
    }

    private bool TransitionTo(
        CreditReturnHoldState targetState,
        DateTimeOffset changedAt)
    {
        ValidateTransitionTime(changedAt);

        if (State == targetState)
        {
            return false;
        }

        if (State != CreditReturnHoldState.Active)
        {
            throw new InvalidCreditReturnHoldStateTransitionException(
                State,
                targetState);
        }

        State = targetState;
        StateChangedAt = changedAt;
        return true;
    }

    private void ValidateTransitionTime(DateTimeOffset changedAt)
    {
        if (changedAt == default || changedAt < StateChangedAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(changedAt),
                "Credit return hold transition time must be monotonic.");
        }
    }

    private void ValidatePersistedState()
    {
        if (
            State is not CreditReturnHoldState.Active and
                not CreditReturnHoldState.Consumed and
                not CreditReturnHoldState.Released)
        {
            throw new InvalidDataException(
                $"Credit return hold '{SettlementReturnId}' has an invalid state.");
        }

        if (StateChangedAt < CreatedAt)
        {
            throw new InvalidDataException(
                $"Credit return hold '{SettlementReturnId}' has invalid timestamps.");
        }
    }

    private static void ValidateId(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException(
                "Credit return hold ID must not be empty.",
                parameterName);
        }
    }
}
