using FuaPay.Web.BuildingBlocks.Domain;

namespace FuaPay.Web.Tests.BuildingBlocks.Domain;

public sealed class MoneyTests
{
    [Fact]
    public void FromCrowns_ConvertsExactlyToMinorUnits()
    {
        var money = Money.FromCrowns(125.50m);

        Assert.Equal(12_550, money.MinorUnits);
    }

    [Fact]
    public void FromCrowns_RejectsMoreThanTwoDecimalPlaces()
    {
        Action action = () =>
        {
            _ = Money.FromCrowns(10.001m);
        };

        Assert.Throws<ArgumentException>(action);
    }

    [Fact]
    public void ToCrowns_ReturnsExactDecimalValue()
    {
        var money = new Money(12_345);

        Assert.Equal(123.45m, money.ToCrowns());
    }

    [Fact]
    public void CurrencyCode_IsCzk()
    {
        Assert.Equal("CZK", Money.CurrencyCode);
    }

    [Fact]
    public void Add_UsesCheckedArithmetic()
    {
        Assert.Throws<OverflowException>(
            () => new Money(long.MaxValue).Add(new Money(1)));
    }

    [Fact]
    public void Subtract_UsesCheckedArithmetic()
    {
        Assert.Throws<OverflowException>(
            () => new Money(long.MinValue).Subtract(new Money(1)));
    }

    [Fact]
    public void Negate_UsesCheckedArithmetic()
    {
        Assert.Throws<OverflowException>(
            () => new Money(long.MinValue).Negate());
    }
}
