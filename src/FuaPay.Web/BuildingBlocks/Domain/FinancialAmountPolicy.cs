using System.Globalization;

namespace FuaPay.Web.BuildingBlocks.Domain;

public enum FinancialAmountKind
{
    JobPrice = 1,
    CreditTopUp = 2,
    CreditAdjustmentAbsolute = 3
}

public readonly record struct FinancialAmountRange(
    long MinimumMinorUnits,
    long MaximumMinorUnits)
{
    public bool Contains(Money amount)
    {
        return amount.MinorUnits >= MinimumMinorUnits &&
               amount.MinorUnits <= MaximumMinorUnits;
    }

    public string MinimumCrownsInvariant =>
        (MinimumMinorUnits / 100m).ToString(
            "0.##",
            CultureInfo.InvariantCulture);

    public string MaximumCrownsInvariant =>
        (MaximumMinorUnits / 100m).ToString(
            "0.##",
            CultureInfo.InvariantCulture);
}

public static class FinancialAmountPolicy
{
    public static FinancialAmountRange JobPrice { get; } =
        new(1, 100_000_000);

    public static FinancialAmountRange CreditTopUp { get; } =
        new(1_000, 10_000_000);

    public static FinancialAmountRange CreditAdjustmentAbsolute { get; } =
        new(1, 10_000_000);

    public static FinancialAmountRange GetRange(FinancialAmountKind kind)
    {
        return kind switch
        {
            FinancialAmountKind.JobPrice => JobPrice,
            FinancialAmountKind.CreditTopUp => CreditTopUp,
            FinancialAmountKind.CreditAdjustmentAbsolute =>
                CreditAdjustmentAbsolute,
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
    }
}
