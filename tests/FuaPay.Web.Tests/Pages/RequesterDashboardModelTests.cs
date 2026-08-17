using FuaPay.Web.Modules.Jobs.Application;
using FuaPay.Web.Modules.Jobs.Domain;
using FuaPay.Web.Modules.ServiceUnits.Application;
using FuaPay.Web.Pages;

namespace FuaPay.Web.Tests.Pages;

public sealed class RequesterDashboardModelTests
{
    [Fact]
    public void Constructor_PreservesValidatedSummaryAndScope()
    {
        var serviceUnit = new ServiceUnitReadModel(
            Guid.NewGuid(),
            "3D",
            "3D tisk",
            ServiceType.ThreeDPrint);

        var dashboard = new RequesterDashboardModel(
            totalJobCount: 6,
            activeJobCount: 4,
            awaitingPaymentCount: 1,
            recentJobs: Array.Empty<JobListItem>(),
            serviceUnits: new[] { serviceUnit },
            selectedServiceUnitId: serviceUnit.Id);

        Assert.Equal(6L, dashboard.TotalJobCount);
        Assert.Equal(4L, dashboard.ActiveJobCount);
        Assert.Equal(1L, dashboard.AwaitingPaymentCount);
        Assert.Equal("3D tisk", dashboard.ScopeLabel);
    }

    [Fact]
    public void Constructor_RejectsAwaitingCountAboveActive()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new RequesterDashboardModel(
                totalJobCount: 2,
                activeJobCount: 1,
                awaitingPaymentCount: 2,
                recentJobs: Array.Empty<JobListItem>(),
                serviceUnits: Array.Empty<ServiceUnitReadModel>(),
                selectedServiceUnitId: null));
    }
}
