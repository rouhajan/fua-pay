namespace FuaPay.Web.Modules.Payments.Domain;

public enum PaymentReconciliationState
{
    Unknown = 0,
    Scheduled = 1,
    Leased = 2,
    RequiresAttention = 3,
    Completed = 4
}
