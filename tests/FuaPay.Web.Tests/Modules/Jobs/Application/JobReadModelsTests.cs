using FuaPay.Web.Modules.Jobs.Application;
using FuaPay.Web.Modules.Jobs.Domain;

namespace FuaPay.Web.Tests.Modules.Jobs.Application;

public sealed class JobReadModelsTests
{
    [Fact]
    public void PageRequest_UsesSafeDefaults()
    {
        var request = new JobPageRequest();

        Assert.Equal(0, request.Offset);
        Assert.Equal(
            JobPageRequest.DefaultLimit,
            request.Limit);
    }

    [Fact]
    public void PageRequest_RejectsNegativeOffset()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new JobPageRequest(offset: -1));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(JobPageRequest.MaximumLimit + 1)]
    public void PageRequest_RejectsUnsupportedLimit(int limit)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new JobPageRequest(limit: limit));
    }

    [Fact]
    public void Filter_RejectsUnknownProductionStatus()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new JobListFilter(
                productionStatus:
                    JobProductionStatus.Unknown));
    }

    [Fact]
    public void Filter_RejectsUnknownPaymentStatus()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new JobListFilter(
                paymentStatus:
                    JobPaymentStatus.Unknown));
    }


    [Fact]
    public void Filter_RejectsEmptyServiceUnitId()
    {
        Assert.Throws<ArgumentException>(
            () => new JobListFilter(
                serviceUnitId: Guid.Empty));
    }

    [Fact]
    public void ManagementSummary_RejectsActiveCountAboveTotal()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ManagementJobSummary(
                totalCount: 1,
                activeCount: 2,
                awaitingPaymentCount: 0));
    }

    [Fact]
    public void ManagementSummary_RejectsAwaitingCountAboveActive()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ManagementJobSummary(
                totalCount: 2,
                activeCount: 1,
                awaitingPaymentCount: 2));
    }
}
