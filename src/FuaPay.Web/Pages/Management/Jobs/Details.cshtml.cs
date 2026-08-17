using FuaPay.Web.Modules.Jobs.Application;
using FuaPay.Web.Modules.Jobs.Web;
using FuaPay.Web.Pages.Shared;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FuaPay.Web.Pages.Management.Jobs;

[Authorize(Roles = "Requester,Admin")]
public sealed class DetailsModel : PageModel
{
    private readonly JobManagementPageContextResolver _contextResolver;
    private readonly IJobQueries _jobQueries;
    private readonly JobManagementService _jobManagementService;
    private readonly JobPresentationComposer _composer;

    public DetailsModel(
        JobManagementPageContextResolver contextResolver,
        IJobQueries jobQueries,
        JobManagementService jobManagementService,
        JobPresentationComposer composer)
    {
        _contextResolver = contextResolver;
        _jobQueries = jobQueries;
        _jobManagementService = jobManagementService;
        _composer = composer;
    }

    public JobManagementPageContext Context { get; private set; } = null!;

    public JobDetailPresentation Presentation { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync(
        Guid id,
        string? view = null,
        CancellationToken cancellationToken = default)
    {
        return await LoadAsync(id, view, cancellationToken)
            ? Page()
            : NotFound();
    }

    public Task<IActionResult> OnPostPublishAsync(
        Guid id,
        string? view = null,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(
            id,
            view,
            actor => _jobManagementService.PublishAsync(
                actor,
                id,
                cancellationToken),
            "Zakázka byla zveřejněna zákazníkovi.",
            cancellationToken);
    }

    public Task<IActionResult> OnPostCancelAsync(
        Guid id,
        string? view = null,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(
            id,
            view,
            actor => _jobManagementService.CancelAsync(
                actor,
                id,
                cancellationToken),
            "Zakázka byla zrušena.",
            cancellationToken);
    }

    public Task<IActionResult> OnPostStartProductionAsync(
        Guid id,
        string? view = null,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(
            id,
            view,
            actor => _jobManagementService.StartProductionAsync(
                actor,
                id,
                cancellationToken),
            "Výroba zakázky byla zahájena.",
            cancellationToken);
    }

    public Task<IActionResult> OnPostReadyAsync(
        Guid id,
        string? view = null,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(
            id,
            view,
            actor => _jobManagementService.MarkReadyForPickupAsync(
                actor,
                id,
                cancellationToken),
            "Zakázka je připravena k vyzvednutí.",
            cancellationToken);
    }

    public Task<IActionResult> OnPostCompleteAsync(
        Guid id,
        string? view = null,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(
            id,
            view,
            actor => _jobManagementService.CompleteAsync(
                actor,
                id,
                cancellationToken),
            "Zakázka byla dokončena.",
            cancellationToken);
    }

    private async Task<IActionResult> ExecuteAsync(
        Guid id,
        string? view,
        Func<JobManagementActor, Task> operation,
        string successMessage,
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

        try
        {
            await operation(Context.Actor);
            TempData["StatusMessage"] = successMessage;
            return RedirectToPage(
                new { id, view = Context.ViewKey });
        }
        catch (Exception exception) when (
            PageOperationError.IsExpected(exception))
        {
            PageOperationError.Add(
                this,
                exception,
                "job.transition",
                "Operaci se zakázkou se nepodařilo dokončit. Obnovte stránku a zkuste to znovu.");

            if (!await LoadAsync(id, view, cancellationToken))
            {
                return NotFound();
            }

            return Page();
        }
    }

    private async Task<bool> LoadAsync(
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
            return false;
        }

        var job = await _jobQueries.FindForManagementAsync(
            Context.Actor,
            id,
            cancellationToken);

        if (job is null)
        {
            return false;
        }

        Presentation = await _composer.ComposeAsync(
            job,
            cancellationToken);

        return true;
    }
}
