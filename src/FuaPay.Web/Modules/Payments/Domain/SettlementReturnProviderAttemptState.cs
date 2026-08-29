namespace FuaPay.Web.Modules.Payments.Domain;

public enum SettlementReturnProviderAttemptState
{
    Unknown = 0,
    Prepared = 1,
    InProgress = 2,
    Confirmed = 3,
    Rejected = 4,
    Uncertain = 5
}
