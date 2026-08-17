namespace FuaPay.Web.Modules.Payments.Domain;

public enum PaymentInitiationState
{
    Unknown = 0,
    Prepared = 1,
    InProgress = 2,
    Initialized = 3,
    Uncertain = 4
}
