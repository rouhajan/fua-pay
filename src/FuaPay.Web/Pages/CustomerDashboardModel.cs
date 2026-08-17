using FuaPay.Web.Modules.Credits.Application;
using FuaPay.Web.Modules.Jobs.Application;

namespace FuaPay.Web.Pages;

public sealed record CustomerDashboardModel
{
    public CustomerDashboardModel(
        long balanceMinorUnits,
        long awaitingPaymentCount,
        long totalJobCount,
        IReadOnlyList<CreditMovementListItem> recentCreditMovements,
        IReadOnlyList<JobListItem> recentJobs)
    {
        ArgumentNullException.ThrowIfNull(recentCreditMovements);
        ArgumentNullException.ThrowIfNull(recentJobs);

        if (balanceMinorUnits < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(balanceMinorUnits));
        }

        if (awaitingPaymentCount < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(awaitingPaymentCount));
        }

        if (totalJobCount < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(totalJobCount));
        }

        BalanceMinorUnits = balanceMinorUnits;
        AwaitingPaymentCount = awaitingPaymentCount;
        TotalJobCount = totalJobCount;
        RecentCreditMovements = recentCreditMovements;
        RecentJobs = recentJobs;
    }

    public long BalanceMinorUnits { get; }

    public long AwaitingPaymentCount { get; }

    public long TotalJobCount { get; }

    public IReadOnlyList<CreditMovementListItem>
        RecentCreditMovements
    { get; }

    public IReadOnlyList<JobListItem> RecentJobs { get; }
}
