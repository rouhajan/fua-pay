namespace FuaPay.Web.Modules.Payments.Domain;

public sealed class InvalidSettlementReturnProviderAttemptStateTransitionException :
    InvalidOperationException
{
    public InvalidSettlementReturnProviderAttemptStateTransitionException(
        SettlementReturnProviderAttemptState current,
        SettlementReturnProviderAttemptState target)
        : base(
            $"Settlement return provider attempt cannot transition from " +
            $"'{current}' to '{target}'.")
    {
        Current = current;
        Target = target;
    }

    public SettlementReturnProviderAttemptState Current { get; }

    public SettlementReturnProviderAttemptState Target { get; }
}
