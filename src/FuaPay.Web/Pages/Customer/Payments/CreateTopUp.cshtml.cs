using System.ComponentModel.DataAnnotations;

using FuaPay.Web.BuildingBlocks.Domain;
using FuaPay.Web.Modules.Access.Web;
using FuaPay.Web.Modules.Payments.Application;
using FuaPay.Web.Pages.Shared;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FuaPay.Web.Pages.Customer.Payments;

[Authorize(Roles = "Customer")]
public sealed class CreateTopUpModel : PageModel
{
    private readonly PaymentCreationService _paymentCreationService;

    public CreateTopUpModel(
        PaymentCreationService paymentCreationService)
    {
        ArgumentNullException.ThrowIfNull(paymentCreationService);
        _paymentCreationService = paymentCreationService;
    }

    [BindProperty]
    [FinancialAmountRange(
        FinancialAmountKind.CreditTopUp,
        ErrorMessage = "Částka musí být mezi 10 Kč a 100 000 Kč.")]
    public decimal AmountCrowns { get; set; } = 500m;

    [BindProperty]
    public Guid CreationRequestId { get; set; }

    public void OnGet()
    {
        CreationRequestId = Guid.NewGuid();
    }

    public async Task<IActionResult> OnPostAsync(
        CancellationToken cancellationToken = default)
    {
        if (CreationRequestId == Guid.Empty)
        {
            ModelState.AddModelError(
                nameof(CreationRequestId),
                "Identifikátor požadavku není platný.");
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        try
        {
            var payment = await _paymentCreationService.CreateCreditTopUpAsync(
                CreationRequestId,
                RequireCustomerUserId(),
                Money.FromCrowns(AmountCrowns),
                cancellationToken);

            return RedirectToPage(
                "./Details",
                new { id = payment.Id, view = "customer" });
        }
        catch (Exception exception) when (
            PageOperationError.IsExpected(exception))
        {
            PageOperationError.Add(
                this,
                exception,
                "payment.top-up.create",
                "Dobití kreditu se nepodařilo připravit. Obnovte stránku a zkuste to znovu.");
            return Page();
        }
    }

    private Guid RequireCustomerUserId()
    {
        return User.FindAccessUserId()
            ?? throw new InvalidOperationException(
                "Přihlášený zákazník nemá interní ID.");
    }
}
