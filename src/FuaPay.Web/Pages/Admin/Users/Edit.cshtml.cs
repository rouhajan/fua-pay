using FuaPay.Web.Modules.Access.Application;
using FuaPay.Web.Modules.Access.Domain;
using FuaPay.Web.Modules.Access.Infrastructure.Entra;
using FuaPay.Web.Modules.Access.Web;
using FuaPay.Web.Modules.ServiceUnits.Application;
using FuaPay.Web.Modules.ServiceUnits.Domain;
using FuaPay.Web.Pages.Shared;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FuaPay.Web.Pages.Admin.Users;

[Authorize(Roles = "Admin")]
public sealed class EditModel : PageModel
{
    private readonly IAccessUserQueries _accessQueries;
    private readonly AccessUserAdministrationService _accessAdministration;
    private readonly ExternalIdentityAdministrationService
        _identityAdministration;
    private readonly EntraAuthenticationAvailability _entraAuthentication;
    private readonly IServiceUnitQueries _serviceUnitQueries;
    private readonly ServiceUnitAdministrationService _serviceUnitAdministration;

    public EditModel(
        IAccessUserQueries accessQueries,
        AccessUserAdministrationService accessAdministration,
        ExternalIdentityAdministrationService identityAdministration,
        EntraAuthenticationAvailability entraAuthentication,
        IServiceUnitQueries serviceUnitQueries,
        ServiceUnitAdministrationService serviceUnitAdministration)
    {
        ArgumentNullException.ThrowIfNull(accessQueries);
        ArgumentNullException.ThrowIfNull(accessAdministration);
        ArgumentNullException.ThrowIfNull(identityAdministration);
        ArgumentNullException.ThrowIfNull(entraAuthentication);
        ArgumentNullException.ThrowIfNull(serviceUnitQueries);
        ArgumentNullException.ThrowIfNull(serviceUnitAdministration);

        _accessQueries = accessQueries;
        _accessAdministration = accessAdministration;
        _identityAdministration = identityAdministration;
        _entraAuthentication = entraAuthentication;
        _serviceUnitQueries = serviceUnitQueries;
        _serviceUnitAdministration = serviceUnitAdministration;
    }

    public AccessUserDetail? ManagedUser { get; private set; }

    public IReadOnlyList<RequesterServiceUnitAssignmentReadModel>
        ServiceUnitAssignments
    { get; private set; } = [];

    public IReadOnlyList<ServiceUnitAdministrationListItem>
        AllServiceUnits
    { get; private set; } = [];

    [BindProperty]
    public AccessRole Role { get; set; }

    [BindProperty]
    public Guid ServiceUnitId { get; set; }

    [BindProperty]
    public Guid EntraObjectId { get; set; }

    public Guid? EntraTenantId => _entraAuthentication.TenantId;

    public bool CanLinkEntraIdentity =>
        _entraAuthentication.IsEnabled &&
        _entraAuthentication.TenantId.HasValue &&
        ManagedUser is not null &&
        !ManagedUser.ExternalIdentities.Any(
            identity =>
                string.Equals(
                    identity.Provider,
                    EntraAuthenticationDefaults
                        .ExternalIdentityProvider,
                    StringComparison.Ordinal) &&
                string.Equals(
                    identity.Tenant,
                    _entraAuthentication.TenantId.Value.ToString("D"),
                    StringComparison.Ordinal));

    public async Task<IActionResult> OnGetAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        return await LoadAsync(id, cancellationToken)
            ? Page()
            : NotFound();
    }

    public Task<IActionResult> OnPostGrantRoleAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(
            id,
            () => _accessAdministration.GrantRoleAsync(
                RequireActorUserId(),
                id,
                Role,
                cancellationToken),
            "Role byla přidělena.",
            cancellationToken);
    }

    public Task<IActionResult> OnPostRevokeRoleAsync(
        Guid id,
        AccessRole role,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(
            id,
            () => _accessAdministration.RevokeRoleAsync(
                RequireActorUserId(),
                id,
                role,
                cancellationToken),
            "Role byla odebrána.",
            cancellationToken);
    }

    public Task<IActionResult> OnPostBlockAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(
            id,
            () => _accessAdministration.BlockAsync(
                RequireActorUserId(),
                id,
                cancellationToken),
            "Uživatel byl zablokován.",
            cancellationToken);
    }

    public Task<IActionResult> OnPostActivateAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(
            id,
            () => _accessAdministration.ActivateAsync(
                RequireActorUserId(),
                id,
                cancellationToken),
            "Uživatel byl znovu aktivován.",
            cancellationToken);
    }

    public Task<IActionResult> OnPostAssignServiceUnitAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(
            id,
            () => _serviceUnitAdministration.AssignRequesterAsync(
                Guid.NewGuid(),
                ServiceUnitId,
                id,
                ServiceUnitChangeActor.ForUser(
                    RequireActorUserId()),
                cancellationToken),
            "Pracoviště bylo zadavateli přiřazeno.",
            cancellationToken);
    }

    public Task<IActionResult> OnPostRevokeServiceUnitAsync(
        Guid id,
        Guid serviceUnitId,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(
            id,
            () => _serviceUnitAdministration.RevokeRequesterAsync(
                serviceUnitId,
                id,
                ServiceUnitChangeActor.ForUser(
                    RequireActorUserId()),
                cancellationToken),
            "Přiřazení pracoviště bylo odebráno.",
            cancellationToken);
    }

    public async Task<IActionResult> OnPostLinkEntraIdentityAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        if (
            !_entraAuthentication.IsEnabled ||
            !_entraAuthentication.TenantId.HasValue)
        {
            return NotFound();
        }

        if (EntraObjectId == Guid.Empty)
        {
            ModelState.AddModelError(
                nameof(EntraObjectId),
                "Zadejte platné Entra object ID.");

            if (!await LoadAsync(id, cancellationToken))
            {
                return NotFound();
            }

            return Page();
        }

        return await ExecuteAsync(
            id,
            async () =>
            {
                await _identityAdministration.AttachEntraIdentityAsync(
                    RequireActorUserId(),
                    id,
                    _entraAuthentication.TenantId.Value,
                    EntraObjectId,
                    cancellationToken);
            },
            "Entra identita byla bezpečně připojena k existujícímu účtu.",
            cancellationToken);
    }


    private async Task<IActionResult> ExecuteAsync(
        Guid id,
        Func<Task> operation,
        string successMessage,
        CancellationToken cancellationToken)
    {
        try
        {
            await operation();
            TempData["StatusMessage"] = successMessage;
            return RedirectToPage(new { id, view = "admin" });
        }
        catch (Exception exception) when (
            PageOperationError.IsExpected(exception))
        {
            PageOperationError.Add(
                this,
                exception,
                "access-user.change",
                "Změnu uživatelského účtu se nepodařilo uložit. Obnovte stránku a zkuste to znovu.");

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
        ManagedUser = await _accessQueries.FindDetailAsync(
            id,
            cancellationToken);

        if (ManagedUser is null)
        {
            return false;
        }

        ServiceUnitAssignments =
            await _serviceUnitQueries.ListAssignmentsForUserAsync(
                id,
                includeRevoked: true,
                cancellationToken);

        AllServiceUnits = await _serviceUnitQueries.ListAllAsync(
            cancellationToken);

        return true;
    }

    private Guid RequireActorUserId()
    {
        return User.FindAccessUserId()
            ?? throw new InvalidOperationException(
                "Přihlášený administrátor nemá interní ID.");
    }
}
