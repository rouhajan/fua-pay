using FuaPay.Web.Modules.FinancialDocuments.Application;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FuaPay.Web.Pages.Admin.FinancialDocuments;

[Authorize(Roles = "Admin")]
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
            var pdf = await _downloads.GetForAdminAsync(id, cancellationToken);
            if (pdf is null)
            {
                return NotFound();
            }

            Response.Headers.CacheControl = "private, no-store";
            Response.Headers.Pragma = "no-cache";
            return File(pdf.Content, "application/pdf", pdf.FileName);
        }
        catch (FinancialDocumentRenderUnavailableException)
        {
            return StatusCode(StatusCodes.Status409Conflict);
        }
    }
}
