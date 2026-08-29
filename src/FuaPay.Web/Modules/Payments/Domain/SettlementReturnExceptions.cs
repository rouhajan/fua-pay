namespace FuaPay.Web.Modules.Payments.Domain;

public sealed class InvalidSettlementReturnStateTransitionException :
    InvalidOperationException
{
    public InvalidSettlementReturnStateTransitionException(
        SettlementReturnState current,
        SettlementReturnState target)
        : base(
            $"Settlement return cannot transition from '{current}' " +
            $"to '{target}'.")
    {
        Current = current;
        Target = target;
    }

    public SettlementReturnState Current { get; }

    public SettlementReturnState Target { get; }
}
