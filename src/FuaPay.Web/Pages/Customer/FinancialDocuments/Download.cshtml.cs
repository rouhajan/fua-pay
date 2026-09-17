using FuaPay.Web.Modules.Access.Web;
using FuaPay.Web.Modules.FinancialDocuments.Application;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FuaPay.Web.Pages.Customer.FinancialDocuments;

[Authorize(Roles = "Customer")]
public sealed class DownloadModel : PageModel
{
    private readonly FinancialDocumentDownloadService _downloads;

    public DownloadModel(FinancialDocumentDownloadService downloads)
    {
        ArgumentNullException.ThrowIfNull(downloads);
        _downloads = downloads;
    }

    public async Task<IActionResult> OnGetAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var pdf = await _downloads.GetForCustomerAsync(
                id,
                User.FindAccessUserId() ?? throw new InvalidOperationException(
                    "Přihlášený zákazník nemá interní ID."),
                cancellationToken);

            return pdf is null ? NotFound() : Pdf(pdf);
        }
        catch (FinancialDocumentRenderUnavailableException)
        {
            return StatusCode(StatusCodes.Status409Conflict);
        }
    }

    private FileContentResult Pdf(FinancialDocumentPdfFile pdf)
    {
        Response.Headers.CacheControl = "private, no-store";
        Response.Headers.Pragma = "no-cache";
        return File(pdf.Content, "application/pdf", pdf.FileName);
    }
}
