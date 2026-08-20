using FuaPay.Web.Modules.Access.Web;
using FuaPay.Web.Modules.Receipts.Application;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FuaPay.Web.Pages.Customer.Jobs;

[Authorize(Roles = "Customer")]
public sealed class ReceiptModel : PageModel
{
    private readonly JobPaymentReceiptService _receiptService;
    private readonly IReceiptPdfRenderer _pdfRenderer;

    public ReceiptModel(
        JobPaymentReceiptService receiptService,
        IReceiptPdfRenderer pdfRenderer)
    {
        ArgumentNullException.ThrowIfNull(receiptService);
        ArgumentNullException.ThrowIfNull(pdfRenderer);

        _receiptService = receiptService;
        _pdfRenderer = pdfRenderer;
    }

    public async Task<IActionResult> OnGetAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var receipt = await _receiptService.CreateForCustomerJobAsync(
            RequireCustomerUserId(),
            id,
            cancellationToken);

        if (receipt is null)
        {
            return NotFound();
        }

        var pdf = _pdfRenderer.Render(receipt);

        Response.Headers["Cache-Control"] = "private, no-store";
        Response.Headers["Pragma"] = "no-cache";

        return File(
            pdf.Content,
            "application/pdf",
            pdf.FileName);
    }

    private Guid RequireCustomerUserId()
    {
        return User.FindAccessUserId()
            ?? throw new InvalidOperationException(
                "Přihlášený zákazník nemá interní ID.");
    }
}
