namespace FuaPay.Web.Modules.Jobs.Domain;

public enum JobProductionStatus
{
    Unknown = 0,
    Draft = 1,
    Published = 2,
    InProduction = 3,
    ReadyForPickup = 4,
    Completed = 5,
    Cancelled = 6
}
