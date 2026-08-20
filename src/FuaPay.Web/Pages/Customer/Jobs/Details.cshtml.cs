using FuaPay.Web.Modules.Access.Web;
using FuaPay.Web.Modules.Credits.Application;
using FuaPay.Web.Modules.Jobs.Application;
using FuaPay.Web.Modules.Jobs.Domain;
using FuaPay.Web.Modules.Jobs.Web;
using FuaPay.Web.Modules.Payments.Application;
using FuaPay.Web.Modules.Receipts.Application;
using FuaPay.Web.Pages.Shared;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FuaPay.Web.Pages.Customer.Jobs;

[Authorize(Roles = "Customer")]
public sealed class DetailsModel : PageModel
{
    private readonly IJobQueries _jobQueries;
    private readonly ICreditQueries _creditQueries;
    private readonly CreditJobPaymentService _creditJobPaymentService;
    private readonly JobPresentationComposer _composer;
    private readonly PaymentCreationService _paymentCreationService;
    private readonly ReceiptConfiguration _receiptConfiguration;

    public DetailsModel(
        IJobQueries jobQueries,
        ICreditQueries creditQueries,
        CreditJobPaymentService creditJobPaymentService,
        JobPresentationComposer composer,
        PaymentCreationService paymentCreationService,
        ReceiptConfiguration receiptConfiguration)
    {
        ArgumentNullException.ThrowIfNull(jobQueries);
        ArgumentNullException.ThrowIfNull(creditQueries);
        ArgumentNullException.ThrowIfNull(creditJobPaymentService);
        ArgumentNullException.ThrowIfNull(composer);
        ArgumentNullException.ThrowIfNull(paymentCreationService);
        ArgumentNullException.ThrowIfNull(receiptConfiguration);

        _jobQueries = jobQueries;
        _creditQueries = creditQueries;
        _creditJobPaymentService = creditJobPaymentService;
        _composer = composer;
        _paymentCreationService = paymentCreationService;
        _receiptConfiguration = receiptConfiguration;
    }

    public JobDetailPresentation Presentation { get; private set; } = null!;

    public CustomerJobPaymentOptions? PaymentOptions { get; private set; }

    public bool CanDownloadReceipt =>
        _receiptConfiguration.Enabled &&
        Presentation.Job.PaymentStatus == JobPaymentStatus.Paid;

    public async Task<IActionResult> OnGetAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        return await LoadAsync(id, cancellationToken)
            ? Page()
            : NotFound();
    }

    public async Task<IActionResult> OnPostCreateDirectPaymentAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var payment = await _paymentCreationService.CreateJobPaymentAsync(
                RequireCustomerUserId(),
                id,
                cancellationToken);

            return RedirectToPage(
                "/Customer/Payments/Details",
                new { id = payment.Id, view = "customer" });
        }
        catch (Exception exception) when (
            PageOperationError.IsExpected(exception))
        {
            PageOperationError.Add(
                this,
                exception,
                "customer-job.payment",
                "Platbu zakázky se nepodařilo dokončit. Obnovte stránku a zkuste to znovu.");

            if (!await LoadAsync(id, cancellationToken))
            {
                return NotFound();
            }

            return Page();
        }
    }

    public async Task<IActionResult> OnPostPayCreditAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var customerUserId = RequireCustomerUserId();

        try
        {
            var applied = await _creditJobPaymentService.PayAsync(
                customerUserId,
                id,
                cancellationToken);

            TempData["StatusMessage"] = applied
                ? "Zakázka byla uhrazena kreditem."
                : "Zakázka již byla uhrazena.";

            return RedirectToPage(new { id, view = "customer" });
        }
        catch (Exception exception) when (
            PageOperationError.IsExpected(exception))
        {
            PageOperationError.Add(
                this,
                exception,
                "customer-job.payment",
                "Platbu zakázky se nepodařilo dokončit. Obnovte stránku a zkuste to znovu.");

            if (!await LoadAsync(id, cancellationToken))
            {
                return NotFound();
            }

            return Page();
        }
    }

    private async Task<bool> LoadAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        var job = await _jobQueries.FindForCustomerAsync(
            RequireCustomerUserId(),
            id,
            cancellationToken);

        if (job is null)
        {
            return false;
        }

        Presentation = await _composer.ComposeAsync(
            job,
            cancellationToken);

        if (
            job.ProductionStatus ==
                JobProductionStatus.Published &&
            job.PaymentStatus ==
                JobPaymentStatus.Unpaid
        )
        {
            var account =
                await _creditQueries.FindAccountForOwnerAsync(
                    job.CustomerUserId,
                    cancellationToken);

            PaymentOptions = new CustomerJobPaymentOptions(
                job.PriceMinorUnits,
                account?.BalanceMinorUnits ?? 0);
        }

        return true;
    }

    private Guid RequireCustomerUserId()
    {
        return User.FindAccessUserId()
            ?? throw new InvalidOperationException(
                "Přihlášený zákazník nemá interní ID.");
    }
}
