using System.ComponentModel.DataAnnotations;

using FuaPay.Web.Modules.Access.Web;
using FuaPay.Web.Modules.Credits.Application;
using FuaPay.Web.Modules.Credits.Infrastructure.PrintPayments;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FuaPay.Web.Pages.Customer.PrintCredential;

[Authorize(Roles = "Customer")]
public sealed class IndexModel : PageModel
{
    private readonly PrintCredentialService _service;
    private readonly PrintCredentialSecurityConfiguration _configuration;

    public IndexModel(
        PrintCredentialService service,
        PrintCredentialSecurityConfiguration configuration)
    {
        _service = service;
        _configuration = configuration;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public PrintCredentialView? Credential { get; private set; }

    [TempData]
    public string? SuccessMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(
        CancellationToken cancellationToken = default)
    {
        if (!_configuration.Enabled)
        {
            return NotFound();
        }

        await LoadAsync(cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostSetAsync(
        CancellationToken cancellationToken = default)
    {
        if (!_configuration.Enabled)
        {
            return NotFound();
        }

        if (!ModelState.IsValid)
        {
            await LoadAsync(cancellationToken);
            return Page();
        }

        try
        {
            await _service.SetAsync(
                RequiredOwnerId(),
                Input.PrintCode!,
                Input.Confirmation!,
                cancellationToken);
        }
        catch (ArgumentException)
        {
            ModelState.AddModelError(string.Empty, "Zadejte shodný šestimístný číselný kód.");
            await LoadAsync(cancellationToken);
            return Page();
        }
        catch (PrintCredentialUnavailableException)
        {
            ModelState.AddModelError(
                string.Empty,
                "Tiskový kód nelze nastavit bez jednoznačného univerzitního e-mailu.");
            await LoadAsync(cancellationToken);
            return Page();
        }

        SuccessMessage = "Tiskový kód byl uložen. Předchozí kód již není platný.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRevokeAsync(
        CancellationToken cancellationToken = default)
    {
        if (!_configuration.Enabled)
        {
            return NotFound();
        }

        try
        {
            await _service.RevokeAsync(RequiredOwnerId(), cancellationToken);
        }
        catch (PrintCredentialUnavailableException)
        {
            return Forbid();
        }

        SuccessMessage = "Tiskový kód byl zneplatněn.";
        return RedirectToPage();
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        try
        {
            Credential = await _service.GetAsync(RequiredOwnerId(), cancellationToken);
        }
        catch (PrintCredentialUnavailableException)
        {
            Credential = null;
        }
    }

    private Guid RequiredOwnerId() =>
        User.FindAccessUserId()
        ?? throw new InvalidOperationException("Authenticated customer has no internal ID.");

    public sealed class InputModel
    {
        [Required(ErrorMessage = "Zadejte tiskový kód.")]
        [RegularExpression("^[0-9]{6}$", ErrorMessage = "Tiskový kód musí mít přesně šest číslic.")]
        [DataType(DataType.Password)]
        public string? PrintCode { get; set; }

        [Required(ErrorMessage = "Zadejte kód znovu pro potvrzení.")]
        [Compare(nameof(PrintCode), ErrorMessage = "Zadané kódy se neshodují.")]
        [DataType(DataType.Password)]
        public string? Confirmation { get; set; }
    }
}
