using FuaPay.Web.Modules.Access.Web;
using FuaPay.Web.Modules.Credits.Application;

using Microsoft.AspNetCore.Mvc;

namespace FuaPay.Web.Pages.Shared;

public sealed class CurrentCreditViewComponent : ViewComponent
{
    private readonly ICreditQueries _creditQueries;
    private readonly ILogger<CurrentCreditViewComponent> _logger;

    public CurrentCreditViewComponent(
        ICreditQueries creditQueries,
        ILogger<CurrentCreditViewComponent> logger)
    {
        ArgumentNullException.ThrowIfNull(creditQueries);
        ArgumentNullException.ThrowIfNull(logger);

        _creditQueries = creditQueries;
        _logger = logger;
    }

    public async Task<IViewComponentResult> InvokeAsync(bool visible)
    {
        if (!visible)
        {
            return Content(string.Empty);
        }

        var userId = UserClaimsPrincipal.FindAccessUserId();

        if (!userId.HasValue)
        {
            return Content(string.Empty);
        }

        try
        {
            var account =
                await _creditQueries.FindAccountForOwnerAsync(
                    userId.Value,
                    HttpContext.RequestAborted);

            return View(
                new CurrentCreditViewModel(
                    account?.BalanceMinorUnits ?? 0));
        }
        catch (Exception exception) when (
            exception is not OperationCanceledException)
        {
            _logger.LogWarning(
                exception,
                "Nepodařilo se načíst kredit do navigace uživatele {UserId}.",
                userId.Value);

            return Content(string.Empty);
        }
    }
}

public sealed record CurrentCreditViewModel(long BalanceMinorUnits);
