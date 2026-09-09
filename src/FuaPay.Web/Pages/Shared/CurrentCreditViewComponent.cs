using FuaPay.Web.Modules.Access.Web;
using FuaPay.Web.Modules.Credits.Application;

using Microsoft.AspNetCore.Mvc;

namespace FuaPay.Web.Pages.Shared;

public sealed class CurrentCreditViewComponent : ViewComponent
{
    private readonly ICreditQueries _creditQueries;
    private readonly CreditAvailabilityService _creditAvailabilityService;
    private readonly ILogger<CurrentCreditViewComponent> _logger;

    public CurrentCreditViewComponent(
        ICreditQueries creditQueries,
        CreditAvailabilityService creditAvailabilityService,
        ILogger<CurrentCreditViewComponent> logger)
    {
        ArgumentNullException.ThrowIfNull(creditQueries);
        ArgumentNullException.ThrowIfNull(creditAvailabilityService);
        ArgumentNullException.ThrowIfNull(logger);

        _creditQueries = creditQueries;
        _creditAvailabilityService = creditAvailabilityService;
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
            var availableMinorUnits = account is null
                ? 0
                : (await _creditAvailabilityService.GetAvailableAsync(
                    account,
                    HttpContext.RequestAborted)).MinorUnits;

            return View(
                new CurrentCreditViewModel(
                    availableMinorUnits));
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

public sealed record CurrentCreditViewModel(long AvailableMinorUnits);
