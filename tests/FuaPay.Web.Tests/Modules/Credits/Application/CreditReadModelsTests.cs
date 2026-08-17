using FuaPay.Web.Modules.Credits.Application;

namespace FuaPay.Web.Tests.Modules.Credits.Application;

public sealed class CreditReadModelsTests
{
    [Fact]
    public void CreditMovementPageRequest_UsesStableDefaults()
    {
        var request = new CreditMovementPageRequest();

        Assert.Equal(0, request.Offset);
        Assert.Equal(
            CreditMovementPageRequest.DefaultLimit,
            request.Limit);
    }

    [Fact]
    public void CreditMovementPageRequest_RejectsInvalidValues()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new CreditMovementPageRequest(offset: -1));

        Assert.Throws<ArgumentOutOfRangeException>(
            () => new CreditMovementPageRequest(limit: 0));

        Assert.Throws<ArgumentOutOfRangeException>(
            () => new CreditMovementPageRequest(
                limit:
                    CreditMovementPageRequest.MaximumLimit + 1));
    }

    [Fact]
    public void CreditMovementPage_ReportsRemainingItems()
    {
        var page = new CreditMovementPage(
            [],
            offset: 5,
            limit: 5,
            totalCount: 11);

        Assert.True(page.HasMore);
    }
}
