using FuaPay.Web.Modules.Jobs.Application;
using FuaPay.Web.Modules.ServiceUnits.Application;

namespace FuaPay.Web.Pages;

public sealed record RequesterDashboardModel
{
    public RequesterDashboardModel(
        long totalJobCount,
        long activeJobCount,
        long awaitingPaymentCount,
        IReadOnlyList<JobListItem> recentJobs,
        IReadOnlyList<ServiceUnitReadModel> serviceUnits,
        Guid? selectedServiceUnitId)
    {
        ArgumentNullException.ThrowIfNull(recentJobs);
        ArgumentNullException.ThrowIfNull(serviceUnits);

        if (totalJobCount < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(totalJobCount));
        }

        if (
            activeJobCount < 0 ||
            activeJobCount > totalJobCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(activeJobCount));
        }

        if (
            awaitingPaymentCount < 0 ||
            awaitingPaymentCount > activeJobCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(awaitingPaymentCount));
        }

        if (
            selectedServiceUnitId.HasValue &&
            serviceUnits.All(
                item => item.Id != selectedServiceUnitId.Value)
        )
        {
            throw new ArgumentException(
                "Vybrané pracoviště není v dostupném rozsahu.",
                nameof(selectedServiceUnitId));
        }

        TotalJobCount = totalJobCount;
        ActiveJobCount = activeJobCount;
        AwaitingPaymentCount = awaitingPaymentCount;
        RecentJobs = recentJobs;
        ServiceUnits = serviceUnits;
        SelectedServiceUnitId = selectedServiceUnitId;
    }

    public long TotalJobCount { get; }

    public long ActiveJobCount { get; }

    public long AwaitingPaymentCount { get; }

    public IReadOnlyList<JobListItem> RecentJobs { get; }

    public IReadOnlyList<ServiceUnitReadModel> ServiceUnits { get; }

    public Guid? SelectedServiceUnitId { get; }

    public string ScopeLabel => SelectedServiceUnitId.HasValue
        ? ServiceUnits.Single(
            item => item.Id == SelectedServiceUnitId.Value)
            .DisplayName
        : "Všechna pracoviště";
}
