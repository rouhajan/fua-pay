namespace FuaPay.Web.Modules.Payments.Domain;

public sealed class InvalidPaymentStateTransitionException :
    InvalidOperationException
{
    public InvalidPaymentStateTransitionException(
        PaymentStatus current,
        PaymentStatus target)
        : base($"Platbu nelze změnit ze stavu '{current}' do stavu '{target}'.")
    {
        Current = current;
        Target = target;
    }

    public PaymentStatus Current { get; }

    public PaymentStatus Target { get; }
}
