using FuaPay.Web.Modules.Access.Web;
using FuaPay.Web.Modules.FinancialDocuments.Application;
using FuaPay.Web.Modules.Reporting.Application;
using FuaPay.Web.Modules.ServiceUnits.Application;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FuaPay.Web.Pages.Admin.Exports;

[Authorize(Roles = "Admin")]
public sealed class IndexModel : PageModel
{
    private readonly AdministrationCsvExportService _exports;
    private readonly FinancialDocumentArchiveService _documentArchives;
    private readonly IServiceUnitQueries _serviceUnitQueries;
    private readonly TimeProvider _clock;

    public IndexModel(
        AdministrationCsvExportService exports,
        IServiceUnitQueries serviceUnitQueries,
        TimeProvider clock,
        FinancialDocumentArchiveService documentArchives)
    {
        ArgumentNullException.ThrowIfNull(exports);
        ArgumentNullException.ThrowIfNull(serviceUnitQueries);
        _exports = exports;
        _documentArchives = documentArchives;
        _serviceUnitQueries = serviceUnitQueries;
        _clock = clock;
    }

    public IReadOnlyList<ServiceUnitAdministrationListItem>
        ServiceUnits
    { get; private set; } = [];

    [BindProperty]
    public string? WorkbookMonth { get; set; }

    [BindProperty]
    public string? WorkbookFrom { get; set; }

    [BindProperty]
    public string? WorkbookTo { get; set; }

    public string? WorkbookMessage { get; private set; }

    [BindProperty]
    public string? ArchiveMonth { get; set; }

    [BindProperty]
    public string? ArchiveFrom { get; set; }

    [BindProperty]
    public string? ArchiveTo { get; set; }

    public string? ArchiveMessage { get; private set; }

    public async Task OnGetAsync(
        CancellationToken cancellationToken = default)
    {
        if (HttpMethods.IsGet(Request.Method))
        {
            WorkbookMonth = AccountingPeriod.CurrentMonth(_clock.GetUtcNow());
        }

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

    public async Task<FileContentResult> OnPostReconciliationAsync(
        DateOnly? from,
        DateOnly? to,
        CancellationToken cancellationToken = default)
    {
        var file = await _exports.ExportPaymentReconciliationAsync(
            RequireAdministratorUserId(),
            from,
            to,
            cancellationToken);
        return Csv(file);
    }

    public async Task<IActionResult> OnPostAccountingWorkbookAsync(
        [FromServices] AccountingWorkbookExportService workbook,
        CancellationToken cancellationToken)
    {
        var user = RequireAdministratorUserId();
        Response.Headers.CacheControl = "private, no-store";
        var period = ExportFieldsAreValid(nameof(WorkbookMonth), nameof(WorkbookFrom), nameof(WorkbookTo))
            ? AccountingPeriod.Parse(WorkbookMonth, WorkbookFrom, WorkbookTo)
            : null;
        if (period is null)
        {
            return await ShowWorkbookMessageAsync(StatusCodes.Status400BadRequest,
                "Vyberte měsíc nebo vyplňte obě data vlastního období.", cancellationToken);
        }

        AccountingWorkbookDownload download;
        try
        {
            download = await workbook.PrepareAsync(period, user, cancellationToken);
        }
        catch (AccountingWorkbookFuturePeriodException)
        {
            return await ShowWorkbookMessageAsync(StatusCodes.Status400BadRequest,
                "Zvolené období ještě nezačalo.", cancellationToken);
        }
        catch (AccountingWorkbookLimitException)
        {
            return await ShowWorkbookMessageAsync(StatusCodes.Status422UnprocessableEntity,
                "Výkaz překročil bezpečnostní limit. Zúžte období nebo kontaktujte správce.", cancellationToken);
        }
        catch (AccountingWorkbookBusyException)
        {
            return await ShowWorkbookMessageAsync(StatusCodes.Status429TooManyRequests,
                "Jiný účetní export právě probíhá. Zkuste to znovu.", cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return await ShowWorkbookMessageAsync(StatusCodes.Status503ServiceUnavailable,
                "Účetní výkaz se nepodařilo připravit.", cancellationToken);
        }

        try
        {
            var result = File(download.Content,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", download.Name);
            Response.RegisterForDispose(download);
            return result;
        }
        catch
        {
            download.Dispose();
            throw;
        }
    }

    private async Task<IActionResult> ShowWorkbookMessageAsync(int status, string message, CancellationToken cancellationToken)
    {
        WorkbookMessage = message;
        Response.StatusCode = status;
        await OnGetAsync(cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostFinancialDocumentsAsync(
        CancellationToken cancellationToken)
    {
        RequireAdministratorUserId();
        Response.Headers.CacheControl = "private, no-store";
        var period = ExportFieldsAreValid(nameof(ArchiveMonth), nameof(ArchiveFrom), nameof(ArchiveTo))
            ? FinancialDocumentArchivePeriod.Parse(ArchiveMonth, ArchiveFrom, ArchiveTo)
            : null;
        if (period is null)
        {
            return await ShowArchiveMessageAsync(StatusCodes.Status400BadRequest,
                "Vyberte měsíc nebo zadejte platný rozsah od–do v délce nejvýše 31 dní.",
                cancellationToken);
        }

        FinancialDocumentArchiveFile? archive;
        try
        {
            archive = await _documentArchives.PrepareAsync(period, cancellationToken);
        }
        catch (FinancialDocumentArchiveException exception)
        {
            var status = exception.Failure switch
            {
                FinancialDocumentArchiveFailure.Busy => StatusCodes.Status429TooManyRequests,
                FinancialDocumentArchiveFailure.Limit => StatusCodes.Status422UnprocessableEntity,
                FinancialDocumentArchiveFailure.Unavailable => StatusCodes.Status409Conflict,
                _ => StatusCodes.Status500InternalServerError
            };
            return await ShowArchiveMessageAsync(status, exception.Message, cancellationToken);
        }

        if (archive is null)
        {
            return await ShowArchiveMessageAsync(StatusCodes.Status200OK,
                "V tomto období nebyly vystaveny žádné doklady.", cancellationToken);
        }

        try
        {
            var response = File(archive.Content, "application/zip", period.FileName);
            Response.RegisterForDispose(archive);
            return response;
        }
        catch
        {
            archive.Dispose();
            throw;
        }
    }

    private async Task<PageResult> ShowArchiveMessageAsync(
        int statusCode, string message, CancellationToken cancellationToken)
    {
        ArchiveMessage = message;
        if (!ModelState.ContainsKey(nameof(WorkbookMonth)))
        {
            WorkbookMonth = AccountingPeriod.CurrentMonth(_clock.GetUtcNow());
        }

        Response.StatusCode = statusCode;
        await OnGetAsync(cancellationToken);
        return Page();
    }

    private bool ExportFieldsAreValid(params string[] names) =>
        names.All(name => ModelState.GetFieldValidationState(name) != ModelValidationState.Invalid);

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
