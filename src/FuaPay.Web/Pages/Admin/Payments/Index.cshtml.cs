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
    private readonly ISettlementReturnQueries _settlementReturnQueries;
    private readonly ICardJobSettlementReturnService
        _cardJobSettlementReturnService;
    private readonly ICardTopUpSettlementReturnService
        _cardTopUpSettlementReturnService;

    public IndexModel(
        IPaymentQueries paymentQueries,
        IAccessUserQueries accessUserQueries,
        IPaymentReconciliationQueries reconciliationQueries,
        ISettlementReturnQueries settlementReturnQueries,
        ICardJobSettlementReturnService cardJobSettlementReturnService,
        ICardTopUpSettlementReturnService? cardTopUpSettlementReturnService = null)
    {
        ArgumentNullException.ThrowIfNull(paymentQueries);
        ArgumentNullException.ThrowIfNull(accessUserQueries);
        ArgumentNullException.ThrowIfNull(reconciliationQueries);
        ArgumentNullException.ThrowIfNull(settlementReturnQueries);
        ArgumentNullException.ThrowIfNull(cardJobSettlementReturnService);

        _paymentQueries = paymentQueries;
        _accessUserQueries = accessUserQueries;
        _reconciliationQueries = reconciliationQueries;
        _settlementReturnQueries = settlementReturnQueries;
        _cardJobSettlementReturnService =
            cardJobSettlementReturnService;
        _cardTopUpSettlementReturnService =
            cardTopUpSettlementReturnService ??
            new UnavailableCardTopUpSettlementReturnService();
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

    public IReadOnlyDictionary<
        Guid,
        IReadOnlyList<SettlementReturnAdministrationItem>>
        SettlementReturns
    { get; private set; } =
            new Dictionary<
                Guid,
                IReadOnlyList<SettlementReturnAdministrationItem>>();

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

        SettlementReturns =
            await _settlementReturnQueries.FindByOriginalPaymentIdsAsync(
                Payments.Items.Select(item => item.Id),
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
                CardJobSettlementReturnOutcome.ReverseCompleted =>
                    "ČSOB reverse byl ověřen a vratka dokončena.",
                CardJobSettlementReturnOutcome.RefundProcessing =>
                    "ČSOB refund se zpracovává. Další ověření je pouze stavové.",
                CardJobSettlementReturnOutcome.RefundCompleted =>
                    "ČSOB full refund byl ověřen a vratka dokončena.",
                CardJobSettlementReturnOutcome.PreExistingProviderRefund =>
                    "ČSOB již eviduje zpracovávaný nebo dokončený refund, " +
                    "který nevytvořil tento tok. Vratka vyžaduje kontrolu.",
                _ =>
                    "Výsledek karetní vratky je nejasný. Vratka vyžaduje " +
                    "pozornost; žádný providerový PUT se automaticky neopakuje."
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

    public async Task<IActionResult> OnPostPartialRefundAsync(
        Guid operationId,
        Guid originalPaymentId,
        decimal amount,
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

        long amountMinorUnits = 0;
        if (amount <= 0 || amount > long.MaxValue / 100m)
        {
            ModelState.AddModelError(
                nameof(amount),
                "Částka musí být kladná a mít nejvýše dvě desetinná místa.");
        }
        else
        {
            var scaledAmount = amount * 100m;
            if (scaledAmount != decimal.Truncate(scaledAmount))
            {
                ModelState.AddModelError(
                    nameof(amount),
                    "Částka musí být kladná a mít nejvýše dvě desetinná místa.");
            }
            else
            {
                amountMinorUnits = decimal.ToInt64(scaledAmount);
            }
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
            var result = await _cardJobSettlementReturnService
                .PartialRefundAsync(
                    new CardJobPartialRefundCommand(
                        operationId,
                        originalPaymentId,
                        User.FindAccessUserId()
                            ?? throw new InvalidOperationException(
                                "Administrátor nemá interní ID."),
                        amountMinorUnits,
                        reason),
                    cancellationToken);

            TempData["StatusMessage"] = result.Outcome switch
            {
                CardJobSettlementReturnOutcome.PartialRefundCompleted =>
                    "ČSOB partial refund byl přímo ověřen a vratka dokončena.",
                CardJobSettlementReturnOutcome.PartialRefundProcessing =>
                    "ČSOB partial refund se zpracovává. Další ověření je pouze stavové.",
                CardJobSettlementReturnOutcome.PartialRefundRejected =>
                    "ČSOB partial refund byl definitivně zamítnut; částka je znovu dostupná.",
                _ =>
                    "Výsledek partial refundu je nejasný. Částka zůstává " +
                    "rezervovaná a žádný další PUT se automaticky neposílá."
            };

            return RedirectToPage(new { view = "admin" });
        }
        catch (Exception exception) when (
            PageOperationError.IsExpected(exception))
        {
            PageOperationError.Add(
                this,
                exception,
                "settlement-return.card-job.partial-refund",
                "Částečnou karetní vratku se nepodařilo bezpečně zpracovat.");
            await LoadAsync(
                status: null,
                purposeType: null,
                search: null,
                offset: 0,
                cancellationToken);
            return Page();
        }
    }

    public async Task<IActionResult> OnPostReturnTopUpAsync(
        Guid requestId,
        Guid originalPaymentId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        if (requestId == Guid.Empty)
        {
            ModelState.AddModelError(
                nameof(requestId),
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
            await LoadAsync(null, null, null, 0, cancellationToken);
            return Page();
        }

        try
        {
            var result = await _cardTopUpSettlementReturnService.ReturnAsync(
                new CardTopUpSettlementReturnCommand(
                    requestId,
                    originalPaymentId,
                    User.FindAccessUserId()
                        ?? throw new InvalidOperationException(
                            "Administrátor nemá interní ID."),
                    reason),
                cancellationToken);

            TempData["StatusMessage"] = result.Outcome switch
            {
                CardTopUpSettlementReturnOutcome.ReverseCompleted =>
                    "Celé karetní dobití bylo vráceno a kredit odečten.",
                CardTopUpSettlementReturnOutcome.RefundCompleted =>
                    "Full refund dobití byl ověřen a kredit odečten.",
                CardTopUpSettlementReturnOutcome.RefundProcessing =>
                    "Refund dobití se zpracovává; kredit zůstává rezervován.",
                CardTopUpSettlementReturnOutcome.PreExistingProviderRefund =>
                    "Poskytovatel již eviduje refund mimo tento tok; kredit zůstává rezervován a vratka vyžaduje kontrolu.",
                _ =>
                    "Výsledek vratky dobití je nejasný; kredit zůstává rezervován a providerový PUT se neopakuje."
            };

            return RedirectToPage(new { view = "admin" });
        }
        catch (Exception exception) when (
            PageOperationError.IsExpected(exception))
        {
            PageOperationError.Add(
                this,
                exception,
                "settlement-return.card-top-up",
                "Vratku karetního dobití se nepodařilo bezpečně zpracovat.");
            await LoadAsync(null, null, null, 0, cancellationToken);
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
