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
public sealed class CreateModel : PageModel
{
    private readonly JobManagementPageContextResolver _contextResolver;
    private readonly JobManagementService _jobManagementService;
    private readonly IAccessUserQueries _accessUserQueries;

    public CreateModel(
        JobManagementPageContextResolver contextResolver,
        JobManagementService jobManagementService,
        IAccessUserQueries accessUserQueries)
    {
        ArgumentNullException.ThrowIfNull(contextResolver);
        ArgumentNullException.ThrowIfNull(jobManagementService);
        ArgumentNullException.ThrowIfNull(accessUserQueries);
        _contextResolver = contextResolver;
        _jobManagementService = jobManagementService;
        _accessUserQueries = accessUserQueries;
    }

    public JobManagementPageContext Context { get; private set; } = null!;

    public IReadOnlyList<AccessUserOption> Customers { get; private set; } = [];

    [BindProperty]
    public JobInputModel Input { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(
        string? view = null,
        CancellationToken cancellationToken = default)
    {
        if (!await LoadAsync(view, cancellationToken))
        {
            return Forbid();
        }

        if (Context.AvailableServiceUnits.Count == 1)
        {
            var unit = Context.AvailableServiceUnits[0];
            Input.ServiceUnitId = unit.Id;
            Input.ServiceType = unit.DefaultServiceType;
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(
        string? view = null,
        CancellationToken cancellationToken = default)
    {
        if (!await LoadAsync(view, cancellationToken))
        {
            return Forbid();
        }

        var selectedServiceUnit = Input.ServiceUnitId.HasValue
            ? Context.AvailableServiceUnits.SingleOrDefault(
                item => item.Id == Input.ServiceUnitId.Value)
            : null;

        if (Input.ServiceUnitId.HasValue && selectedServiceUnit is null)
        {
            ModelState.AddModelError(
                "Input.ServiceUnitId",
                "Vybraná služba není dostupná pro přihlášený účet.");
        }
        else if (selectedServiceUnit is not null)
        {
            Input.ServiceType = selectedServiceUnit.DefaultServiceType;
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        try
        {
            var job = await _jobManagementService.CreateDraftAsync(
                Context.Actor,
                Input.ServiceUnitId!.Value,
                Input.CustomerUserId!.Value,
                Input.ServiceType,
                Input.Title,
                Input.Description,
                Money.FromCrowns(Input.PriceCrowns),
                cancellationToken);

            TempData["StatusMessage"] = "Koncept zakázky byl vytvořen.";
            return RedirectToPage(
                "./Details",
                new
                {
                    id = job.Id,
                    view = Context.ViewKey
                });
        }
        catch (Exception exception) when (
            PageOperationError.IsExpected(exception))
        {
            PageOperationError.Add(
                this,
                exception,
                "job.create",
                "Zakázku se nepodařilo vytvořit. Zkontrolujte zadané údaje a zkuste to znovu.");
            return Page();
        }
    }

    private async Task<bool> LoadAsync(
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
            return false;
        }

        Customers = await _accessUserQueries.ListActiveCustomersAsync(
            cancellationToken);

        return true;
    }
}
