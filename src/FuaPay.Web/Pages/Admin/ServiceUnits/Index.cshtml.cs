using System.ComponentModel.DataAnnotations;

using FuaPay.Web.Modules.Access.Web;
using FuaPay.Web.Modules.ServiceUnits.Application;
using FuaPay.Web.Modules.ServiceUnits.Domain;
using FuaPay.Web.Pages.Shared;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FuaPay.Web.Pages.Admin.ServiceUnits;

[Authorize(Roles = "Admin")]
public sealed class IndexModel : PageModel
{
    private readonly IServiceUnitQueries _queries;
    private readonly ServiceUnitAdministrationService _administration;

    public IndexModel(
        IServiceUnitQueries queries,
        ServiceUnitAdministrationService administration)
    {
        ArgumentNullException.ThrowIfNull(queries);
        ArgumentNullException.ThrowIfNull(administration);
        _queries = queries;
        _administration = administration;
    }

    public IReadOnlyList<ServiceUnitAdministrationListItem>
        ServiceUnits
    { get; private set; } = [];

    [BindProperty]
    [Required]
    [StringLength(8, MinimumLength = 2)]
    [RegularExpression(
        "^[A-Za-z0-9]{2,8}$",
        ErrorMessage = "Kód musí mít 2 až 8 písmen nebo číslic.")]
    public string Code { get; set; } = string.Empty;

    [BindProperty]
    [Required]
    [StringLength(120)]
    public string DisplayName { get; set; } = string.Empty;

    [BindProperty]
    public ServiceType DefaultServiceType { get; set; } =
        ServiceType.ThreeDPrint;

    public async Task OnGetAsync(
        CancellationToken cancellationToken = default)
    {
        await LoadAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostCreateAsync(
        CancellationToken cancellationToken = default)
    {
        ValidateServiceType(
            DefaultServiceType,
            nameof(DefaultServiceType));

        if (!ModelState.IsValid)
        {
            await LoadAsync(cancellationToken);
            return Page();
        }

        try
        {
            await _administration.CreateAsync(
                Guid.NewGuid(),
                Code,
                DisplayName,
                DefaultServiceType,
                ServiceUnitChangeActor.ForUser(
                    RequireActorUserId()),
                cancellationToken);

            TempData["StatusMessage"] = "Pracoviště bylo vytvořeno.";
            return RedirectToPage(new { view = "admin" });
        }
        catch (Exception exception) when (
            PageOperationError.IsExpected(exception))
        {
            PageOperationError.Add(
                this,
                exception,
                "service-unit.change",
                "Změnu pracoviště se nepodařilo uložit. Obnovte stránku a zkuste to znovu.");
            await LoadAsync(cancellationToken);
            return Page();
        }
    }

    public async Task<IActionResult> OnPostUpdateAsync(
        Guid id,
        string displayName,
        ServiceType defaultServiceType,
        CancellationToken cancellationToken = default)
    {
        ValidateId(id, nameof(id));

        if (string.IsNullOrWhiteSpace(displayName) || displayName.Trim().Length > 120)
        {
            ModelState.AddModelError(
                nameof(displayName),
                "Název pracoviště musí mít 1 až 120 znaků.");
        }

        ValidateServiceType(defaultServiceType, nameof(defaultServiceType));

        if (!ModelState.IsValid)
        {
            await LoadAsync(cancellationToken);
            return Page();
        }

        return await ExecuteAsync(
            () => _administration.UpdateDetailsAsync(
                id,
                displayName,
                defaultServiceType,
                ServiceUnitChangeActor.ForUser(
                    RequireActorUserId()),
                cancellationToken),
            "Pracoviště bylo upraveno.",
            cancellationToken);
    }

    public async Task<IActionResult> OnPostDeactivateAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        ValidateId(id, nameof(id));

        if (!ModelState.IsValid)
        {
            await LoadAsync(cancellationToken);
            return Page();
        }

        return await ExecuteAsync(
            () => _administration.DeactivateAsync(
                id,
                ServiceUnitChangeActor.ForUser(
                    RequireActorUserId()),
                cancellationToken),
            "Pracoviště bylo deaktivováno. Historické zakázky zůstávají zachovány.",
            cancellationToken);
    }

    private async Task<IActionResult> ExecuteAsync(
        Func<Task> operation,
        string successMessage,
        CancellationToken cancellationToken)
    {
        try
        {
            await operation();
            TempData["StatusMessage"] = successMessage;
            return RedirectToPage(new { view = "admin" });
        }
        catch (Exception exception) when (
            PageOperationError.IsExpected(exception))
        {
            PageOperationError.Add(
                this,
                exception,
                "service-unit.change",
                "Změnu pracoviště se nepodařilo uložit. Obnovte stránku a zkuste to znovu.");
            await LoadAsync(cancellationToken);
            return Page();
        }
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        ServiceUnits = await _queries.ListAllAsync(cancellationToken);
    }

    private Guid RequireActorUserId()
    {
        return User.FindAccessUserId()
            ?? throw new InvalidOperationException(
                "Přihlášený administrátor nemá interní ID.");
    }

    private void ValidateId(Guid id, string key)
    {
        if (id == Guid.Empty)
        {
            ModelState.AddModelError(key, "Identifikátor pracoviště není platný.");
        }
    }

    private void ValidateServiceType(ServiceType serviceType, string key)
    {
        if (serviceType == ServiceType.Unknown || !Enum.IsDefined(serviceType))
        {
            ModelState.AddModelError(key, "Druh služby není podporovaný.");
        }
    }
}
