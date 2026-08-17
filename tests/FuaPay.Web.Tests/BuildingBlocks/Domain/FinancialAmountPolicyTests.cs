namespace FuaPay.Web.Tests.BuildingBlocks.Domain;

public sealed class FinancialAmountPolicyTests
{
    [Theory]
    [InlineData(FinancialAmountKind.JobPrice, 1, 100_000_000)]
    [InlineData(FinancialAmountKind.CreditTopUp, 1_000, 10_000_000)]
    [InlineData(FinancialAmountKind.CreditAdjustmentAbsolute, 1, 10_000_000)]
    public void GetRange_UsesM0Limits(
        FinancialAmountKind kind,
        long minimum,
        long maximum)
    {
        var range = FinancialAmountPolicy.GetRange(kind);

        Assert.Equal(minimum, range.MinimumMinorUnits);
        Assert.Equal(maximum, range.MaximumMinorUnits);
        Assert.True(range.Contains(new Money(minimum)));
        Assert.True(range.Contains(new Money(maximum)));
        Assert.False(range.Contains(new Money(minimum - 1)));
        Assert.False(range.Contains(new Money(maximum + 1)));
    }
}
