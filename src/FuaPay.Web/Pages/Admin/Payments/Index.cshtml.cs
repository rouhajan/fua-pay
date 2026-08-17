using FuaPay.Web.Modules.Access.Application;
using FuaPay.Web.Modules.Payments.Application;
using FuaPay.Web.Modules.Payments.Domain;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FuaPay.Web.Pages.Admin.Payments;

[Authorize(Roles = "Admin")]
public sealed class IndexModel : PageModel
{
    private const int PageSize = 40;
    private readonly IPaymentQueries _paymentQueries;
    private readonly IAccessUserQueries _accessUserQueries;
    private readonly IPaymentReconciliationQueries _reconciliationQueries;

    public IndexModel(
        IPaymentQueries paymentQueries,
        IAccessUserQueries accessUserQueries,
        IPaymentReconciliationQueries reconciliationQueries)
    {
        ArgumentNullException.ThrowIfNull(paymentQueries);
        ArgumentNullException.ThrowIfNull(accessUserQueries);
        ArgumentNullException.ThrowIfNull(reconciliationQueries);

        _paymentQueries = paymentQueries;
        _accessUserQueries = accessUserQueries;
        _reconciliationQueries = reconciliationQueries;
    }

    public PaymentPage Payments { get; private set; } =
        new([], 0, PageSize, 0);

    public IReadOnlyDictionary<Guid, AccessUserOption> Users { get; private set; } =
        new Dictionary<Guid, AccessUserOption>();

    public PaymentStatus? Status { get; private set; }

    public PaymentPurposeType? PurposeType { get; private set; }

    public string? Search { get; private set; }

    public IReadOnlyList<PaymentReconciliationAdminItem> ReconciliationItems
    {
        get;
        private set;
    } = [];

    public async Task OnGetAsync(
        PaymentStatus? status = null,
        PaymentPurposeType? purposeType = null,
        string? search = null,
        int offset = 0,
        CancellationToken cancellationToken = default)
    {
        Status = status.HasValue && status.Value != PaymentStatus.Unknown
            ? status
            : null;
        PurposeType = purposeType.HasValue && purposeType.Value != PaymentPurposeType.Unknown
            ? purposeType
            : null;
        Search = string.IsNullOrWhiteSpace(search) ? null : search.Trim();

        Payments = await _paymentQueries.ListForAdministrationAsync(
            new PaymentListFilter(Status, PurposeType, Search),
            new PaymentPageRequest(Math.Max(0, offset), PageSize),
            cancellationToken);

        Users = await _accessUserQueries.FindOptionsAsync(
            Payments.Items.Select(item => item.CustomerUserId),
            cancellationToken);

        ReconciliationItems =
            await _reconciliationQueries.ListOpenAsync(
                limit: 20,
                cancellationToken);
    }
}
