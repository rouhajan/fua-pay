namespace FuaPay.Web.Modules.Credits.Domain;

public sealed class InvalidCreditReturnHoldStateTransitionException :
    InvalidOperationException
{
    public InvalidCreditReturnHoldStateTransitionException(
        CreditReturnHoldState currentState,
        CreditReturnHoldState targetState)
        : base(
            $"Credit return hold cannot transition from " +
            $"'{currentState}' to '{targetState}'.")
    {
        CurrentState = currentState;
        TargetState = targetState;
    }

    public CreditReturnHoldState CurrentState { get; }

    public CreditReturnHoldState TargetState { get; }
}
