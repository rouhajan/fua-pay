using FuaPay.Web.Modules.Payments.Domain;

namespace FuaPay.Web.Pages;

public static class PaymentDisplay
{
    public static string PurposeLabel(PaymentPurposeType purpose)
    {
        return purpose switch
        {
            PaymentPurposeType.CreditTopUp => "Dobití kreditu",
            PaymentPurposeType.Job => "Přímá úhrada zakázky",
            _ => "Neznámý účel"
        };
    }

    public static string StatusLabel(PaymentStatus status)
    {
        return status switch
        {
            PaymentStatus.Created => "Vytvořena",
            PaymentStatus.Pending => "Čeká na potvrzení",
            PaymentStatus.Succeeded => "Uhrazená",
            PaymentStatus.Failed => "Zamítnutá",
            PaymentStatus.Cancelled => "Zrušená",
            PaymentStatus.Expired => "Vypršela",
            _ => "Neznámý stav"
        };
    }

    public static bool IsUnsuccessfulTerminalStatus(
        PaymentStatus status)
    {
        return status is
            PaymentStatus.Failed or
            PaymentStatus.Cancelled or
            PaymentStatus.Expired;
    }

    public static string StatusCssClass(PaymentStatus status)
    {
        return status switch
        {
            PaymentStatus.Succeeded => "is-ready",
            PaymentStatus.Pending or PaymentStatus.Created => "is-awaiting",
            PaymentStatus.Failed => "is-failed",
            PaymentStatus.Cancelled or PaymentStatus.Expired => "is-cancelled",
            _ => string.Empty
        };
    }
}
