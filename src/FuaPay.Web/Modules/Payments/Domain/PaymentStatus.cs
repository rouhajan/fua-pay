namespace FuaPay.Web.Modules.Payments.Domain;

public enum PaymentStatus
{
    Unknown = 0,
    Created = 1,
    Pending = 2,
    Succeeded = 3,
    Failed = 4,
    Cancelled = 5,
    Expired = 6
}
