using FuaPay.Web.Modules.Access.Application;
using FuaPay.Web.Modules.Access.Web;
using FuaPay.Web.Modules.Payments.Application;
using FuaPay.Web.Modules.Payments.Domain;
using FuaPay.Web.Pages.Shared;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FuaPay.Web.Pages.Admin.Payments;

[Authorize(Roles = "Admin")]
public sealed class IndexModel : PageModel
{
    private const int PageSize = 40;
    private readonly IPaymentQueries _paymentQueries;
    private readonly IAccessUserQueries _accessUserQueries;
    private readonly IPaymentReconciliationQueries _reconciliationQueries;
    private readonly ICardJobSettlementReturnService
        _cardJobSettlementReturnService;

    public IndexModel(
        IPaymentQueries paymentQueries,
        IAccessUserQueries accessUserQueries,
        IPaymentReconciliationQueries reconciliationQueries,
        ICardJobSettlementReturnService cardJobSettlementReturnService)
    {
        ArgumentNullException.ThrowIfNull(paymentQueries);
        ArgumentNullException.ThrowIfNull(accessUserQueries);
        ArgumentNullException.ThrowIfNull(reconciliationQueries);
        ArgumentNullException.ThrowIfNull(cardJobSettlementReturnService);

        _paymentQueries = paymentQueries;
        _accessUserQueries = accessUserQueries;
        _reconciliationQueries = reconciliationQueries;
        _cardJobSettlementReturnService =
            cardJobSettlementReturnService;
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

    public async Task<IActionResult> OnPostReverseAsync(
        Guid operationId,
        Guid originalPaymentId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        if (operationId == Guid.Empty)
        {
            ModelState.AddModelError(
                nameof(operationId),
                "Identifikátor vratky není platný.");
        }

        if (originalPaymentId == Guid.Empty)
        {
            ModelState.AddModelError(
                nameof(originalPaymentId),
                "Identifikátor platby není platný.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            ModelState.AddModelError(
                nameof(reason),
                "Důvod vratky je povinný.");
        }
        else if (reason.Trim().Length > SettlementReturn.MaximumReasonLength)
        {
            ModelState.AddModelError(
                nameof(reason),
                "Důvod vratky je příliš dlouhý.");
        }

        if (!ModelState.IsValid)
        {
            await LoadAsync(
                status: null,
                purposeType: null,
                search: null,
                offset: 0,
                cancellationToken);
            return Page();
        }

        try
        {
            var result = await _cardJobSettlementReturnService.ReturnAsync(
                new CardJobSettlementReturnCommand(
                    operationId,
                    originalPaymentId,
                    User.FindAccessUserId()
                        ?? throw new InvalidOperationException(
                            "Administrátor nemá interní ID."),
                    reason),
                cancellationToken);

            TempData["StatusMessage"] = result.Outcome switch
            {
                CardJobSettlementReturnOutcome.Confirmed =>
                    "ČSOB reverse byl ověřen a vratka dokončena.",
                CardJobSettlementReturnOutcome.ReverseRejected =>
                    "Platba již není reverzibilní. Vratka vyžaduje " +
                    "samostatné rozhodnutí o refundu.",
                _ =>
                    "Výsledek ČSOB reverse je nejasný. Vratka vyžaduje " +
                    "pozornost; další ověření je pouze stavové."
            };

            return RedirectToPage(new { view = "admin" });
        }
        catch (Exception exception) when (
            PageOperationError.IsExpected(exception))
        {
            PageOperationError.Add(
                this,
                exception,
                "settlement-return.card-job.reverse",
                "Karetní vratku se nepodařilo bezpečně zpracovat.");
            await LoadAsync(
                status: null,
                purposeType: null,
                search: null,
                offset: 0,
                cancellationToken);
            return Page();
        }
    }

    private async Task LoadAsync(
        PaymentStatus? status,
        PaymentPurposeType? purposeType,
        string? search,
        int offset,
        CancellationToken cancellationToken)
    {
        await OnGetAsync(
            status,
            purposeType,
            search,
            offset,
            cancellationToken);
    }
}
