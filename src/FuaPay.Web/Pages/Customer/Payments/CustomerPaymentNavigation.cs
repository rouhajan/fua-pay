using FuaPay.Web.Modules.Payments.Application;

using Microsoft.AspNetCore.Mvc;

namespace FuaPay.Web.Pages.Customer.Payments;

internal static class CustomerPaymentNavigation
{
    public static IActionResult AfterCreation(
        PaymentCreationOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);

        if (outcome.ShouldRedirectToProvider)
        {
            return new RedirectResult(outcome.ProcessUri!.AbsoluteUri);
        }

        return new RedirectToPageResult(
            "/Customer/Payments/Details",
            new
            {
                id = outcome.Payment.Id,
                view = "customer"
            });
    }
}
