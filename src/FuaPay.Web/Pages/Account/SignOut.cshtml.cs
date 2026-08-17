using FuaPay.Web.Modules.Access.Infrastructure.Entra;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FuaPay.Web.Pages.Account;

public sealed class SignOutModel : PageModel
{
    private readonly EntraAuthenticationAvailability
        _entraAuthentication;

    public SignOutModel(
        EntraAuthenticationAvailability entraAuthentication)
    {
        ArgumentNullException.ThrowIfNull(entraAuthentication);
        _entraAuthentication = entraAuthentication;
    }

    public IActionResult OnGet()
    {
        return RedirectToPage("/Index");
    }

    public IActionResult OnPost()
    {
        var properties = new AuthenticationProperties
        {
            RedirectUri = Url.Content("~/") ?? "/"
        };

        return _entraAuthentication.IsEnabled
            ? SignOut(
                properties,
                CookieAuthenticationDefaults.AuthenticationScheme,
                EntraAuthenticationDefaults.AuthenticationScheme)
            : SignOut(
                properties,
                CookieAuthenticationDefaults.AuthenticationScheme);
    }
}
