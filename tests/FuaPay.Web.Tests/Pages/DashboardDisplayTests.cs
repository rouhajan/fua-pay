using FuaPay.Web.Modules.Credits.Application;
using FuaPay.Web.Modules.Credits.Domain;
using FuaPay.Web.Modules.Jobs.Application;
using FuaPay.Web.Modules.Jobs.Domain;
using FuaPay.Web.Pages;

namespace FuaPay.Web.Tests.Pages;

public sealed class DashboardDisplayTests
{
    [Fact]
    public void FormatMoney_UsesCzechCrownsWithoutFalseDecimals()
    {
        Assert.Equal(
            "1 210 Kč",
            DashboardDisplay.FormatMoney(121_000));

        Assert.Equal(
            "125,50 Kč",
            DashboardDisplay.FormatMoney(12_550));
    }

    [Fact]
    public void FormatMovementAmount_UsesAccountingDirection()
    {
        var credit = CreateMovement(
            CreditMovementType.Credit,
            200_000);

        var debit = CreateMovement(
            CreditMovementType.Debit,
            39_000);

        Assert.Equal(
            "+2 000 Kč",
            DashboardDisplay.FormatMovementAmount(credit));

        Assert.Equal(
            "−390 Kč",
            DashboardDisplay.FormatMovementAmount(debit));
    }

    [Fact]
    public void MovementTitle_DoesNotExposeTechnicalDebitReference()
    {
        var movement = CreateMovement(
            CreditMovementType.Debit,
            39_000);

        Assert.Equal(
            "Úhrada zakázky",
            DashboardDisplay.MovementTitle(movement));
    }

    [Fact]
    public void JobLabels_AreUserFacing()
    {
        Assert.Equal(
            "3D tisk",
            DashboardDisplay.FormatServiceType(
                ServiceType.ThreeDPrint));

        Assert.Equal(
            "Ostatní",
            DashboardDisplay.FormatServiceType(
                ServiceType.Other));

        Assert.Equal(
            "Připraveno k vyzvednutí",
            DashboardDisplay.FormatJobProductionStatus(
                JobProductionStatus.ReadyForPickup));

        Assert.Equal(
            "is-ready",
            DashboardDisplay.JobStatusCssClass(
                JobProductionStatus.ReadyForPickup));
    }


    [Fact]
    public void PublishedPaidJob_IsNeverPresentedAsAwaitingPayment()
    {
        Assert.Equal(
            "Uhrazeno",
            DashboardDisplay.FormatJobStatus(
                JobProductionStatus.Published,
                JobPaymentStatus.Paid));

        Assert.Equal(
            "is-ready",
            DashboardDisplay.JobStatusCssClass(
                JobProductionStatus.Published,
                JobPaymentStatus.Paid));

        Assert.Equal(
            "Uhrazeno",
            DashboardDisplay.FormatJobPaymentStatus(
                JobProductionStatus.Published,
                JobPaymentStatus.Paid));
    }

    [Fact]
    public void PaidJobInProduction_KeepsBothStatesClear()
    {
        Assert.Equal(
            "Ve výrobě",
            DashboardDisplay.FormatJobStatus(
                JobProductionStatus.InProduction,
                JobPaymentStatus.Paid));

        Assert.Equal(
            "Uhrazeno",
            DashboardDisplay.FormatJobPaymentStatus(
                JobProductionStatus.InProduction,
                JobPaymentStatus.Paid));
    }

    [Fact]
    public void PublishedUnpaidJob_IsPresentedAsAwaitingPayment()
    {
        Assert.Equal(
            "Čeká na úhradu",
            DashboardDisplay.FormatJobStatus(
                JobProductionStatus.Published,
                JobPaymentStatus.Unpaid));

        Assert.Equal(
            "Čeká na úhradu",
            DashboardDisplay.FormatJobPaymentStatus(
                JobProductionStatus.Published,
                JobPaymentStatus.Unpaid));
    }

    [Fact]
    public void SettlementType_IsLocalized()
    {
        Assert.Equal(
            "Kredit",
            DashboardDisplay.FormatJobSettlementType(
                JobSettlementType.Credit));

        Assert.Equal(
            "Přímá platba",
            DashboardDisplay.FormatJobSettlementType(
                JobSettlementType.DirectPayment));
    }

    [Fact]
    public void JobUnitCode_UsesImmutableJobNumberPrefix()
    {
        var job = new JobListItem(
            Guid.NewGuid(),
            "LAS-2026-000018",
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            ServiceType.Workshop,
            "Laserový výřez",
            26_000,
            JobProductionStatus.Completed,
            JobPaymentStatus.Paid,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);

        Assert.Equal("LAS", DashboardDisplay.JobUnitCode(job));
    }

    private static CreditMovementListItem CreateMovement(
        CreditMovementType type,
        long amountMinorUnits)
    {
        return new CreditMovementListItem(
            Guid.NewGuid(),
            type,
            amountMinorUnits,
            0,
            "Technický popis s ID operace",
            DateTimeOffset.UtcNow,
            1);
    }
}
