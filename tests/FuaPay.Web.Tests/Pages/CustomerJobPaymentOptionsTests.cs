using FuaPay.Web.Pages.Customer.Jobs;

namespace FuaPay.Web.Tests.Pages;

public sealed class CustomerJobPaymentOptionsTests
{
    [Fact]
    public void SufficientCredit_CalculatesRemainingBalance()
    {
        var options = new CustomerJobPaymentOptions(
            priceMinorUnits: 52_000,
            creditBalanceMinorUnits: 121_000);

        Assert.True(options.HasSufficientCredit);
        Assert.Equal(69_000, options.BalanceAfterPaymentMinorUnits);
        Assert.Equal(0, options.MissingCreditMinorUnits);
    }

    [Fact]
    public void InsufficientCredit_CalculatesMissingAmount()
    {
        var options = new CustomerJobPaymentOptions(
            priceMinorUnits: 52_000,
            creditBalanceMinorUnits: 20_000);

        Assert.False(options.HasSufficientCredit);
        Assert.Equal(20_000, options.BalanceAfterPaymentMinorUnits);
        Assert.Equal(32_000, options.MissingCreditMinorUnits);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-1, 0)]
    [InlineData(1, -1)]
    public void InvalidAmounts_AreRejected(
        long priceMinorUnits,
        long creditBalanceMinorUnits)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new CustomerJobPaymentOptions(
                priceMinorUnits,
                creditBalanceMinorUnits));
    }
}
