using System.ComponentModel.DataAnnotations;

using FuaPay.Web.BuildingBlocks.Domain;

namespace FuaPay.Web.Pages.Shared;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter)]
public sealed class FinancialAmountRangeAttribute : ValidationAttribute
{
    private readonly FinancialAmountKind _kind;

    public FinancialAmountRangeAttribute(FinancialAmountKind kind)
    {
        _kind = kind;
    }

    public override bool IsValid(object? value)
    {
        if (value is null)
        {
            return true;
        }

        if (value is not decimal crowns)
        {
            return false;
        }

        try
        {
            var amount = Money.FromCrowns(crowns);
            var range = FinancialAmountPolicy.GetRange(_kind);

            if (_kind == FinancialAmountKind.CreditAdjustmentAbsolute)
            {
                return
                    amount.MinorUnits != 0 &&
                    amount.MinorUnits >= -range.MaximumMinorUnits &&
                    amount.MinorUnits <= range.MaximumMinorUnits;
            }

            return range.Contains(amount);
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (OverflowException)
        {
            return false;
        }
    }
}
