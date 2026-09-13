using System.ComponentModel.DataAnnotations;

using FuaPay.Web.BuildingBlocks.Domain;
using FuaPay.Web.Modules.Access.Application;
using FuaPay.Web.Modules.Access.Web;
using FuaPay.Web.Modules.Credits.Application;
using FuaPay.Web.Pages.Shared;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FuaPay.Web.Pages.Admin.Credit;

[Authorize(Roles = "Admin")]
public sealed class IndexModel : PageModel
{
    private const int PageSize = 40;

    private readonly ICreditQueries _creditQueries;
    private readonly CreditAdministrationService _administration;
    private readonly ManualCreditTopUpService _manualTopUps;
    private readonly IAccessUserQueries _accessUserQueries;

    public IndexModel(
        ICreditQueries creditQueries,
        CreditAdministrationService administration,
        ManualCreditTopUpService manualTopUps,
        IAccessUserQueries accessUserQueries)
    {
        _creditQueries = creditQueries;
        _administration = administration;
        _manualTopUps = manualTopUps;
        _accessUserQueries = accessUserQueries;
    }

    public CreditAdministrationMovementPage Movements { get; private set; } =
        new([], 0, PageSize, 0);

    public IReadOnlyList<AccessUserOption> Customers { get; private set; } = [];

    public IReadOnlyDictionary<Guid, AccessUserOption> MovementOwners { get; private set; } =
        new Dictionary<Guid, AccessUserOption>();

    public CreditAdjustmentInput Adjustment { get; private set; } = new();

    public ManualCreditTopUpInput ManualTopUp { get; private set; } = new();

    public async Task OnGetAsync(
        int offset = 0,
        CancellationToken cancellationToken = default)
    {
        Adjustment.CommandId = Guid.NewGuid();
        ManualTopUp.CommandId = Guid.NewGuid();
        await LoadAsync(offset, cancellationToken);
    }

    public async Task<IActionResult> OnPostAdjustAsync(
        CreditAdjustmentInput adjustment,
        CancellationToken cancellationToken = default)
    {
        Adjustment = adjustment;
        ManualTopUp.CommandId = Guid.NewGuid();

        if (adjustment.OwnerId == Guid.Empty)
        {
            ModelState.AddModelError(
                $"{nameof(Adjustment)}.{nameof(adjustment.OwnerId)}",
                "Vyberte uživatele.");
        }

        if (adjustment.SignedAmountCrowns == 0)
        {
            ModelState.AddModelError(
                $"{nameof(Adjustment)}.{nameof(adjustment.SignedAmountCrowns)}",
                "Korekce nesmí být nulová.");
        }

        if (adjustment.CommandId == Guid.Empty)
        {
            ModelState.AddModelError(
                $"{nameof(Adjustment)}.{nameof(adjustment.CommandId)}",
                "Identifikátor korekce není platný.");
        }

        if (!ModelState.IsValid)
        {
            await LoadAsync(0, cancellationToken);
            return Page();
        }

        try
        {
            await _administration.AdjustAsync(
                new CreditAdjustmentCommand(
                    adjustment.CommandId,
                    User.FindAccessUserId()
                        ?? throw new InvalidOperationException(
                            "Administrátor nemá interní ID."),
                    adjustment.OwnerId,
                    Money.FromCrowns(adjustment.SignedAmountCrowns),
                    adjustment.Reason),
                cancellationToken);

            TempData["StatusMessage"] =
                "Administrativní kreditní korekce byla zapsána jako nový neměnný pohyb.";
            return RedirectToPage(new { view = "admin" });
        }
        catch (Exception exception) when (
            PageOperationError.IsExpected(exception))
        {
            PageOperationError.Add(
                this,
                exception,
                "credit.adjust",
                "Kreditní operaci se nepodařilo dokončit. Obnovte stránku a zkuste to znovu.");
            await LoadAsync(0, cancellationToken);
            return Page();
        }
    }

    public async Task<IActionResult> OnPostManualTopUpAsync(
        ManualCreditTopUpInput manualTopUp,
        CancellationToken cancellationToken = default)
    {
        ManualTopUp = manualTopUp;
        Adjustment.CommandId = Guid.NewGuid();

        var ownerIsEligible =
            manualTopUp.OwnerId != Guid.Empty &&
            await _accessUserQueries.IsActiveCustomerAsync(
                manualTopUp.OwnerId,
                cancellationToken);

        if (!ownerIsEligible)
        {
            ModelState.AddModelError(
                $"{nameof(ManualTopUp)}.{nameof(manualTopUp.OwnerId)}",
                "Vyberte aktivního zákazníka.");
        }

        if (manualTopUp.CommandId == Guid.Empty)
        {
            ModelState.AddModelError(
                $"{nameof(ManualTopUp)}.{nameof(manualTopUp.CommandId)}",
                "Identifikátor ručního dobití není platný.");
        }

        if (!ModelState.IsValid)
        {
            await LoadAsync(0, cancellationToken);
            return Page();
        }

        try
        {
            await _manualTopUps.TopUpAsync(
                new ManualCreditTopUpCommand(
                    manualTopUp.CommandId,
                    User.FindAccessUserId()
                        ?? throw new InvalidOperationException(
                            "Administrátor nemá interní ID."),
                    manualTopUp.OwnerId,
                    Money.FromCrowns(manualTopUp.AmountCrowns),
                    manualTopUp.Note),
                cancellationToken);

            TempData["StatusMessage"] =
                "Kredit byl ručně dobit jako nový neměnný pohyb.";
            return RedirectToPage(new { view = "admin" });
        }
        catch (Exception exception) when (
            PageOperationError.IsExpected(exception))
        {
            PageOperationError.Add(
                this,
                exception,
                "credit.manual-topup",
                "Ruční dobití kreditu se nepodařilo dokončit. Obnovte stránku a zkuste to znovu.");
            await LoadAsync(0, cancellationToken);
            return Page();
        }
    }

    private async Task LoadAsync(
        int offset,
        CancellationToken cancellationToken)
    {
        Customers = await _accessUserQueries.ListActiveCustomersAsync(
            cancellationToken);

        Movements = await _creditQueries.ListAdministrationMovementsAsync(
            new CreditAdministrationMovementFilter(),
            new CreditMovementPageRequest(
                Math.Max(0, offset),
                PageSize),
            cancellationToken);

        MovementOwners = await _accessUserQueries.FindOptionsAsync(
            Movements.Items.Select(item => item.OwnerId),
            cancellationToken);
    }

    public sealed class CreditAdjustmentInput
    {
        public Guid CommandId { get; set; }

        [Required]
        public Guid OwnerId { get; set; }

        [FinancialAmountRange(
            FinancialAmountKind.CreditAdjustmentAbsolute,
            ErrorMessage = "Korekce musí být mezi −100 000 Kč a 100 000 Kč.")]
        public decimal SignedAmountCrowns { get; set; }

        [Required(ErrorMessage = "Důvod korekce je povinný.")]
        [StringLength(CreditAdjustmentCommand.ReasonMaxLength)]
        public string Reason { get; set; } = string.Empty;
    }

    public sealed class ManualCreditTopUpInput
    {
        public Guid CommandId { get; set; }

        [Required]
        public Guid OwnerId { get; set; }

        [FinancialAmountRange(
            FinancialAmountKind.ManualCreditTopUp,
            ErrorMessage = "Dobití musí být vyšší než 0 Kč a nejvýše 100 000 Kč.")]
        public decimal AmountCrowns { get; set; }

        [Required(ErrorMessage = "Poznámka k dobití je povinná.")]
        [StringLength(ManualCreditTopUpCommand.NoteMaxLength)]
        public string Note { get; set; } = string.Empty;
    }
}
