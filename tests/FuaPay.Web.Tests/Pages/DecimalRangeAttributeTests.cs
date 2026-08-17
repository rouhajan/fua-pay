using System.Globalization;
using System.Reflection;

using FuaPay.Web.Pages.Customer.Payments;
using FuaPay.Web.Pages.Management.Jobs;
using FuaPay.Web.Pages.Shared;

using CreditIndexModel = FuaPay.Web.Pages.Admin.Credit.IndexModel;

namespace FuaPay.Web.Tests.Pages;

public sealed class FinancialAmountRangeAttributeTests
{
    [Theory]
    [InlineData("cs-CZ")]
    [InlineData("en-US")]
    public void DecimalRanges_ParseLimitsIndependentlyOfCurrentCulture(
        string cultureName)
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;

        try
        {
            var culture = CultureInfo.GetCultureInfo(cultureName);
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;

            var jobPrice = GetRange<JobInputModel>(
                nameof(JobInputModel.PriceCrowns));
            var topUp = GetRange<CreateTopUpModel>(
                nameof(CreateTopUpModel.AmountCrowns));
            var correction = GetRange<CreditIndexModel>(
                nameof(CreditIndexModel.SignedAmountCrowns));

            Assert.True(jobPrice.IsValid(0.01m));
            Assert.False(jobPrice.IsValid(0m));
            Assert.True(topUp.IsValid(10m));
            Assert.False(topUp.IsValid(9.99m));
            Assert.True(correction.IsValid(-100000m));
            Assert.False(correction.IsValid(-100000.01m));
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }

    private static FinancialAmountRangeAttribute GetRange<TModel>(string propertyName)
    {
        return typeof(TModel)
            .GetProperty(
                propertyName,
                BindingFlags.Instance | BindingFlags.Public)
            ?.GetCustomAttribute<FinancialAmountRangeAttribute>()
            ?? throw new InvalidOperationException(
                $"Property {typeof(TModel).Name}.{propertyName} " +
                "does not define FinancialAmountRangeAttribute.");
    }
}
