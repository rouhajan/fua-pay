using FuaPay.Web.Modules.Access.Infrastructure.Entra;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FuaPay.Web.Pages.Account;

[AllowAnonymous]
public sealed class SignInModel : PageModel
{
    private readonly EntraAuthenticationAvailability
        _entraAuthentication;

    public SignInModel(
        EntraAuthenticationAvailability entraAuthentication)
    {
        ArgumentNullException.ThrowIfNull(entraAuthentication);
        _entraAuthentication = entraAuthentication;
    }

    public IActionResult OnGet(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return LocalRedirect(ResolveReturnUrl(returnUrl));
        }

        if (!_entraAuthentication.IsEnabled)
        {
            return NotFound();
        }

        return Challenge(
            new AuthenticationProperties
            {
                RedirectUri = ResolveReturnUrl(returnUrl)
            },
            EntraAuthenticationDefaults.AuthenticationScheme);
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
