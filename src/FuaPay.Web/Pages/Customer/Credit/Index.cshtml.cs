using FuaPay.Web.Modules.Access.Web;
using FuaPay.Web.Modules.Credits.Application;
using FuaPay.Web.Modules.Payments.Application;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FuaPay.Web.Pages.Customer.Credit;

[Authorize(Roles = "Customer")]
public sealed class IndexModel : PageModel
{
    private const int PageSize = 30;

    private readonly ICreditQueries _queries;
    private readonly DevelopmentPaymentAvailability _paymentAvailability;

    public IndexModel(
        ICreditQueries queries,
        DevelopmentPaymentAvailability paymentAvailability)
    {
        ArgumentNullException.ThrowIfNull(queries);
        ArgumentNullException.ThrowIfNull(paymentAvailability);
        _queries = queries;
        _paymentAvailability = paymentAvailability;
    }

    public bool CanCreatePayment => _paymentAvailability.IsEnabled;

    public CreditAccountSummary? Account { get; private set; }

    public CreditMovementPage Movements { get; private set; } =
        new([], 0, PageSize, 0);

    public async Task OnGetAsync(
        int offset = 0,
        CancellationToken cancellationToken = default)
    {
        var ownerId = User.FindAccessUserId()
            ?? throw new InvalidOperationException(
                "Přihlášený zákazník nemá interní ID.");

        Account = await _queries.FindAccountForOwnerAsync(
            ownerId,
            cancellationToken);

        Movements = await _queries.ListMovementsForOwnerAsync(
            ownerId,
            new CreditMovementPageRequest(
                Math.Max(0, offset),
                PageSize),
            cancellationToken);
    }
}
