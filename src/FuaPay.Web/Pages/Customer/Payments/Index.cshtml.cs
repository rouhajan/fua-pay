using FuaPay.Web.Modules.Access.Web;
using FuaPay.Web.Modules.Payments.Application;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FuaPay.Web.Pages.Customer.Payments;

[Authorize(Roles = "Customer")]
public sealed class IndexModel : PageModel
{
    private const int PageSize = 30;
    private readonly IPaymentQueries _queries;

    public IndexModel(IPaymentQueries queries)
    {
        ArgumentNullException.ThrowIfNull(queries);
        _queries = queries;
    }

    public PaymentPage Payments { get; private set; } =
        new([], 0, PageSize, 0);

    public async Task OnGetAsync(
        int offset = 0,
        CancellationToken cancellationToken = default)
    {
        var userId = User.FindAccessUserId()
            ?? throw new InvalidOperationException(
                "Přihlášený zákazník nemá interní ID.");

        Payments = await _queries.ListForCustomerAsync(
            userId,
            new PaymentListFilter(),
            new PaymentPageRequest(Math.Max(0, offset), PageSize),
            cancellationToken);
    }
}
