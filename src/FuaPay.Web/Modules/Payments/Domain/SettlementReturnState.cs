namespace FuaPay.Web.Modules.Payments.Domain;

public enum SettlementReturnState
{
    Unknown = 0,
    Requested = 1,
    InProgress = 2,
    Completed = 3,
    Rejected = 4,
    RequiresAttention = 5
}
