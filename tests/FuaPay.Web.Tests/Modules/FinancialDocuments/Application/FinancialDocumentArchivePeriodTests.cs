using FuaPay.Web.Modules.FinancialDocuments.Application;

namespace FuaPay.Web.Tests.Modules.FinancialDocuments.Application;

public sealed class FinancialDocumentArchivePeriodTests
{
    [Theory]
    [InlineData("2026-03-29", "2026-03-28T23:00:00Z", "2026-03-29T22:00:00Z")]
    [InlineData("2026-10-25", "2026-10-24T22:00:00Z", "2026-10-25T23:00:00Z")]
    [InlineData("2026-12-31", "2026-12-30T23:00:00Z", "2026-12-31T23:00:00Z")]
    public void PragueLocalDaysHaveIndependentUtcBoundaries(
        string day, string start, string end)
    {
        var period = Assert.IsType<FinancialDocumentArchivePeriod>(
            FinancialDocumentArchivePeriod.Parse(null, day, day));

        Assert.Equal(start, period.FromUtc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"));
        Assert.Equal(end, period.ToUtcExclusive.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"));
        Assert.Equal(TimeSpan.Zero, period.FromUtc.Offset);
        Assert.Equal(TimeSpan.Zero, period.ToUtcExclusive.Offset);
    }

    [Fact]
    public void MonthUsesFirstDayAndIncludesLeapDay()
    {
        var period = Assert.IsType<FinancialDocumentArchivePeriod>(
            FinancialDocumentArchivePeriod.Parse("2024-02", null, null));

        Assert.Equal("2024-01-31T23:00:00Z", period.FromUtc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"));
        Assert.Equal("2024-02-29T23:00:00Z", period.ToUtcExclusive.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"));
        Assert.Equal("doklady-o-uhrade-2024-02-01--2024-02-29.zip", period.FileName);
    }

    [Theory]
    [InlineData(null, null, null)]
    [InlineData("2026-03", "2026-03-01", "2026-03-04")]
    [InlineData(null, "2026-03-01", null)]
    [InlineData(null, null, "2026-03-31")]
    [InlineData("2026-3", null, null)]
    [InlineData("2026-13", null, null)]
    [InlineData(null, "2026-02-30", "2026-03-02")]
    [InlineData(null, "2026-03-04", "2026-03-03")]
    [InlineData(null, "2026-03-01", "2026-04-01")]
    [InlineData(null, "0001-01-01", "0001-01-01")]
    [InlineData(null, "9999-12-31", "9999-12-31")]
    [InlineData("9999-12", null, null)]
    public void InvalidOrUnrepresentablePeriodsAreRejected(
        string? month, string? from, string? to)
    {
        Assert.Null(FinancialDocumentArchivePeriod.Parse(month, from, to));
    }
}
