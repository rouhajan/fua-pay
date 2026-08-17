namespace FuaPay.Web.Modules.Payments.Domain;

public static class JobPaymentBlockingPolicy
{
    public static readonly PaymentStatus[] Statuses =
    [
        PaymentStatus.Created,
        PaymentStatus.Pending,
        PaymentStatus.Succeeded
    ];

    public static bool IsBlocking(PaymentStatus status) =>
        Array.IndexOf(Statuses, status) >= 0;
}
