using FuaPay.Web.BuildingBlocks.Domain;
using FuaPay.Web.Modules.Access.Application;
using FuaPay.Web.Modules.Jobs.Application;
using FuaPay.Web.Modules.Jobs.Domain;
using FuaPay.Web.Modules.Jobs.Web;
using FuaPay.Web.Pages.Shared;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FuaPay.Web.Pages.Management.Jobs;

[Authorize(Roles = "Requester,Admin")]
public sealed class EditModel : PageModel
{
    private readonly JobManagementPageContextResolver _contextResolver;
    private readonly IJobQueries _jobQueries;
    private readonly JobManagementService _jobManagementService;
    private readonly IAccessUserQueries _accessUserQueries;
    private readonly JobPresentationComposer _composer;

    public EditModel(
        JobManagementPageContextResolver contextResolver,
        IJobQueries jobQueries,
        JobManagementService jobManagementService,
        IAccessUserQueries accessUserQueries,
        JobPresentationComposer composer)
    {
        _contextResolver = contextResolver;
        _jobQueries = jobQueries;
        _jobManagementService = jobManagementService;
        _accessUserQueries = accessUserQueries;
        _composer = composer;
    }

    public JobManagementPageContext Context { get; private set; } = null!;

    public JobDetailPresentation Presentation { get; private set; } = null!;

    public IReadOnlyList<AccessUserOption> Customers { get; private set; } = [];

    [BindProperty]
    public JobInputModel Input { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(
        Guid id,
        string? view = null,
        CancellationToken cancellationToken = default)
    {
        var loadResult = await LoadAsync(id, view, cancellationToken);

        if (loadResult is not null)
        {
            return loadResult;
        }

        if (Presentation.Job.ProductionStatus != JobProductionStatus.Draft)
        {
            return RedirectToPage(
                "./Details",
                new { id, view = Context.ViewKey });
        }

        Input = new JobInputModel
        {
            ServiceUnitId = Presentation.Job.ServiceUnitId,
            CustomerUserId = Presentation.Job.CustomerUserId,
            ServiceType = Presentation.Job.ServiceType,
            Title = Presentation.Job.Title,
            Description = Presentation.Job.Description,
            PriceCrowns = new Money(
                Presentation.Job.PriceMinorUnits).ToCrowns()
        };

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(
        Guid id,
        string? view = null,
        CancellationToken cancellationToken = default)
    {
        var loadResult = await LoadAsync(id, view, cancellationToken);

        if (loadResult is not null)
        {
            return loadResult;
        }

        Input.ServiceUnitId = Presentation.Job.ServiceUnitId;
        Input.ServiceType = Presentation.Job.ServiceType;

        if (!ModelState.IsValid)
        {
            return Page();
        }

        try
        {
            await _jobManagementService.UpdateDraftAsync(
                Context.Actor,
                id,
                Input.CustomerUserId!.Value,
                Input.ServiceType,
                Input.Title,
                Input.Description,
                Money.FromCrowns(Input.PriceCrowns),
                cancellationToken);

            TempData["StatusMessage"] = "Koncept zakázky byl upraven.";
            return RedirectToPage(
                "./Details",
                new { id, view = Context.ViewKey });
        }
        catch (Exception exception) when (
            PageOperationError.IsExpected(exception))
        {
            PageOperationError.Add(
                this,
                exception,
                "job.edit",
                "Zakázku se nepodařilo uložit. Obnovte stránku a zkuste to znovu.");
            return Page();
        }
    }

    private async Task<IActionResult?> LoadAsync(
        Guid id,
        string? view,
        CancellationToken cancellationToken)
    {
        Context = await _contextResolver.ResolveAsync(
                User,
                view,
                cancellationToken: cancellationToken)
            ?? null!;

        if (Context is null)
        {
            return Forbid();
        }

        var job = await _jobQueries.FindForManagementAsync(
            Context.Actor,
            id,
            cancellationToken);

        if (job is null)
        {
            return NotFound();
        }

        Presentation = await _composer.ComposeAsync(
            job,
            cancellationToken);

        Customers = await _accessUserQueries.ListActiveCustomersAsync(
            cancellationToken);

        return null;
    }
}
