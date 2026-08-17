using System.Globalization;

using FuaPay.Web.Modules.Credits.Application;
using FuaPay.Web.Modules.Credits.Domain;
using FuaPay.Web.Modules.Jobs.Application;
using FuaPay.Web.Modules.Jobs.Domain;

namespace FuaPay.Web.Pages;

public static class DashboardDisplay
{
    private static readonly CultureInfo CzechCulture =
        CreateCzechCulture();

    public static string FormatMoney(long minorUnits)
    {
        var crowns = minorUnits / 100m;
        var format = crowns == decimal.Truncate(crowns)
            ? "N0"
            : "N2";

        return $"{crowns.ToString(format, CzechCulture)} Kč";
    }

    public static string FormatMovementAmount(
        CreditMovementListItem movement)
    {
        ArgumentNullException.ThrowIfNull(movement);

        var sign = movement.Type switch
        {
            CreditMovementType.Credit => "+",
            CreditMovementType.Debit => "−",
            _ => string.Empty
        };

        return $"{sign}{FormatMoney(movement.AmountMinorUnits)}";
    }

    public static string MovementTitle(
        CreditMovementListItem movement)
    {
        ArgumentNullException.ThrowIfNull(movement);

        return movement.Type == CreditMovementType.Debit
            ? "Úhrada zakázky"
            : movement.Description;
    }

    public static string MovementCssClass(
        CreditMovementType type)
    {
        return type switch
        {
            CreditMovementType.Credit => "is-credit",
            CreditMovementType.Debit => "is-debit",
            _ => string.Empty
        };
    }

    public static string FormatDate(DateTimeOffset value)
    {
        return value.ToLocalTime().ToString(
            "d. M. yyyy",
            CzechCulture);
    }

    public static string JobUnitCode(JobListItem job)
    {
        ArgumentNullException.ThrowIfNull(job);

        var separatorIndex = job.Number.IndexOf(
            '-',
            StringComparison.Ordinal);

        return separatorIndex > 0
            ? job.Number[..separatorIndex]
            : job.Number;
    }

    public static string FormatServiceType(ServiceType type)
    {
        return type switch
        {
            ServiceType.ThreeDPrint =>
                "3D tisk",
            ServiceType.LargeFormatPrint =>
                "Velkoformátový tisk",
            ServiceType.Workshop =>
                "Dílna",
            ServiceType.Other =>
                "Ostatní",
            _ => "Služba"
        };
    }

    public static string FormatJobProductionStatus(
        JobProductionStatus status)
    {
        return status switch
        {
            JobProductionStatus.Published =>
                "Zveřejněno",
            JobProductionStatus.InProduction =>
                "Ve výrobě",
            JobProductionStatus.ReadyForPickup =>
                "Připraveno k vyzvednutí",
            JobProductionStatus.Completed =>
                "Dokončeno",
            JobProductionStatus.Cancelled =>
                "Zrušeno",
            JobProductionStatus.Draft =>
                "Návrh",
            _ => "Neznámý stav"
        };
    }

    public static string FormatJobStatus(
        JobProductionStatus productionStatus,
        JobPaymentStatus paymentStatus)
    {
        if (productionStatus == JobProductionStatus.Published)
        {
            return paymentStatus == JobPaymentStatus.Paid
                ? "Uhrazeno"
                : "Čeká na úhradu";
        }

        return FormatJobProductionStatus(productionStatus);
    }

    public static string FormatJobPaymentStatus(
        JobProductionStatus productionStatus,
        JobPaymentStatus paymentStatus)
    {
        if (paymentStatus == JobPaymentStatus.Paid)
        {
            return "Uhrazeno";
        }

        return productionStatus switch
        {
            JobProductionStatus.Draft => "Zatím se nehradí",
            JobProductionStatus.Cancelled => "Neuhrazeno",
            _ => "Čeká na úhradu"
        };
    }

    public static string FormatJobSettlementType(
        JobSettlementType? settlementType)
    {
        return settlementType switch
        {
            JobSettlementType.Credit => "Kredit",
            JobSettlementType.DirectPayment => "Přímá platba",
            _ => "—"
        };
    }

    public static string JobStatusCssClass(
        JobProductionStatus status)
    {
        return status switch
        {
            JobProductionStatus.InProduction =>
                "is-progress",
            JobProductionStatus.ReadyForPickup =>
                "is-ready",
            JobProductionStatus.Completed =>
                "is-completed",
            JobProductionStatus.Cancelled =>
                "is-cancelled",
            JobProductionStatus.Published =>
                "is-awaiting",
            _ => string.Empty
        };
    }

    public static string JobStatusCssClass(
        JobProductionStatus productionStatus,
        JobPaymentStatus paymentStatus)
    {
        if (
            productionStatus == JobProductionStatus.Published &&
            paymentStatus == JobPaymentStatus.Paid)
        {
            return "is-ready";
        }

        return JobStatusCssClass(productionStatus);
    }

    private static CultureInfo CreateCzechCulture()
    {
        var culture =
            (CultureInfo)CultureInfo
                .GetCultureInfo("cs-CZ")
                .Clone();

        culture.NumberFormat.NumberGroupSeparator = "\u00A0";

        return CultureInfo.ReadOnly(culture);
    }
}
