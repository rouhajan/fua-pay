using FuaPay.Web.Pages.Admin.Audit;

namespace FuaPay.Web.Tests.Pages;

public sealed class AdminAuditTimeDisplayTests
{
    [Fact]
    public void ToPragueTime_WinterUtcUsesCetOffset()
    {
        var utc = new DateTimeOffset(
            2026,
            1,
            15,
            12,
            0,
            0,
            TimeSpan.Zero);

        var prague = AuditTimeDisplay.ToPragueTime(utc);

        Assert.Equal(TimeSpan.FromHours(1), prague.Offset);
        Assert.Equal(13, prague.Hour);
        Assert.Equal(
            "15. 01. 2026 13:00:00",
            AuditTimeDisplay.Format(utc));
    }

    [Fact]
    public void ToPragueTime_SummerUtcUsesCestOffset()
    {
        var utc = new DateTimeOffset(
            2026,
            7,
            15,
            12,
            0,
            0,
            TimeSpan.Zero);

        var prague = AuditTimeDisplay.ToPragueTime(utc);

        Assert.Equal(TimeSpan.FromHours(2), prague.Offset);
        Assert.Equal(14, prague.Hour);
        Assert.Equal(
            "15. 07. 2026 14:00:00",
            AuditTimeDisplay.Format(utc));
    }
}
