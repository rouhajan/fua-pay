using FuaPay.Web.Modules.Access.Web;
using FuaPay.Web.Modules.Reporting.Application;
using FuaPay.Web.Modules.ServiceUnits.Application;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FuaPay.Web.Pages.Admin.Exports;

[Authorize(Roles = "Admin")]
public sealed class IndexModel : PageModel
{
    private readonly AdministrationCsvExportService _exports;
    private readonly IServiceUnitQueries _serviceUnitQueries;

    public IndexModel(
        AdministrationCsvExportService exports,
        IServiceUnitQueries serviceUnitQueries)
    {
        ArgumentNullException.ThrowIfNull(exports);
        ArgumentNullException.ThrowIfNull(serviceUnitQueries);
        _exports = exports;
        _serviceUnitQueries = serviceUnitQueries;
    }

    public IReadOnlyList<ServiceUnitAdministrationListItem>
        ServiceUnits
    { get; private set; } = [];

    public async Task OnGetAsync(
        CancellationToken cancellationToken = default)
    {
        ServiceUnits = await _serviceUnitQueries.ListAllAsync(
            cancellationToken);
    }

    public async Task<FileContentResult> OnPostJobsAsync(
        DateOnly? from,
        DateOnly? to,
        Guid? serviceUnitId,
        CancellationToken cancellationToken = default)
    {
        var file = await _exports.ExportJobsAsync(
            RequireAdministratorUserId(),
            serviceUnitId,
            from,
            to,
            cancellationToken);
        return Csv(file);
    }

    public async Task<FileContentResult> OnPostCreditAsync(
        DateOnly? from,
        DateOnly? to,
        CancellationToken cancellationToken = default)
    {
        var file = await _exports.ExportCreditMovementsAsync(
            RequireAdministratorUserId(),
            from,
            to,
            cancellationToken);
        return Csv(file);
    }

    public async Task<FileContentResult> OnPostPaymentsAsync(
        DateOnly? from,
        DateOnly? to,
        CancellationToken cancellationToken = default)
    {
        var file = await _exports.ExportPaymentsAsync(
            RequireAdministratorUserId(),
            from,
            to,
            cancellationToken);
        return Csv(file);
    }

    private static FileContentResult Csv(CsvExportFile file)
    {
        return new FileContentResult(
            file.Content,
            "text/csv; charset=utf-8")
        {
            FileDownloadName = file.FileName
        };
    }

    private Guid RequireAdministratorUserId()
    {
        return User.FindAccessUserId()
            ?? throw new InvalidOperationException(
                "Přihlášený administrátor nemá interní ID.");
    }
}
