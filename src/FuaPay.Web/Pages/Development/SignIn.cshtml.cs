using FuaPay.Web.Modules.Access.Development;
using FuaPay.Web.Modules.Access.Web;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FuaPay.Web.Pages.Development;

public sealed class SignInModel : PageModel
{
    private readonly DevelopmentSignInService _signInService;
    private readonly DevelopmentSignInAvailability
        _availability;

    public SignInModel(
        DevelopmentSignInService signInService,
        DevelopmentSignInAvailability availability)
    {
        ArgumentNullException.ThrowIfNull(signInService);
        ArgumentNullException.ThrowIfNull(availability);

        _signInService = signInService;
        _availability = availability;
    }

    public IReadOnlyList<DevelopmentIdentityProfileSection> Sections =>
        DevelopmentIdentityProfiles.Sections;

    [BindProperty]
    public string ProfileKey { get; set; } = string.Empty;

    [BindProperty]
    public string? ReturnUrl { get; set; }

    public IActionResult OnGet(string? returnUrl = null)
    {
        if (!_availability.IsEnabled)
        {
            return NotFound();
        }

        ReturnUrl = returnUrl;

        return Page();
    }

    public async Task<IActionResult> OnPostSignInAsync(
        CancellationToken cancellationToken)
    {
        if (!_availability.IsEnabled)
        {
            return NotFound();
        }

        var profile =
            DevelopmentIdentityProfiles.Find(ProfileKey);

        if (profile is null)
        {
            ModelState.AddModelError(
                nameof(ProfileKey),
                "Development profile is not supported.");

            return Page();
        }

        var user =
            await _signInService.ResolveAsync(
                profile,
                cancellationToken);

        var principal =
            AccessClaimsPrincipalFactory.Create(
                user,
                CookieAuthenticationDefaults.AuthenticationScheme);

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            new AuthenticationProperties
            {
                IsPersistent = false,
                AllowRefresh = false
            });

        return LocalRedirect(
            ResolveReturnUrl(ReturnUrl));
    }

    public async Task<IActionResult> OnPostSignOutAsync()
    {
        if (!_availability.IsEnabled)
        {
            return NotFound();
        }

        await HttpContext.SignOutAsync(
            CookieAuthenticationDefaults.AuthenticationScheme);

        return LocalRedirect(ResolveReturnUrl(null));
    }

    private string ResolveReturnUrl(string? returnUrl)
    {
        return
            !string.IsNullOrWhiteSpace(returnUrl) &&
            Url.IsLocalUrl(returnUrl)
                ? returnUrl
                : Url.Content("~/") ?? "/";
    }
}
