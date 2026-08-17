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
    private readonly IAccessUserQueries _accessUserQueries;

    public IndexModel(
        ICreditQueries creditQueries,
        CreditAdministrationService administration,
        IAccessUserQueries accessUserQueries)
    {
        _creditQueries = creditQueries;
        _administration = administration;
        _accessUserQueries = accessUserQueries;
    }

    public CreditAdministrationMovementPage Movements { get; private set; } =
        new([], 0, PageSize, 0);

    public IReadOnlyList<AccessUserOption> Customers { get; private set; } = [];

    public IReadOnlyDictionary<Guid, AccessUserOption> MovementOwners { get; private set; } =
        new Dictionary<Guid, AccessUserOption>();

    [BindProperty]
    [Required]
    public Guid OwnerId { get; set; }

    [BindProperty]
    public Guid CommandId { get; set; }

    [BindProperty]
    [FinancialAmountRange(
        FinancialAmountKind.CreditAdjustmentAbsolute,
        ErrorMessage = "Korekce musí být mezi −100 000 Kč a 100 000 Kč.")]
    public decimal SignedAmountCrowns { get; set; }

    [BindProperty]
    [Required(ErrorMessage = "Důvod korekce je povinný.")]
    [StringLength(CreditAdjustmentCommand.ReasonMaxLength)]
    public string Reason { get; set; } = string.Empty;

    public async Task OnGetAsync(
        int offset = 0,
        CancellationToken cancellationToken = default)
    {
        CommandId = Guid.NewGuid();
        await LoadAsync(offset, cancellationToken);
    }

    public async Task<IActionResult> OnPostAdjustAsync(
        CancellationToken cancellationToken = default)
    {
        if (OwnerId == Guid.Empty)
        {
            ModelState.AddModelError(
                nameof(OwnerId),
                "Vyberte uživatele.");
        }

        if (SignedAmountCrowns == 0)
        {
            ModelState.AddModelError(
                nameof(SignedAmountCrowns),
                "Korekce nesmí být nulová.");
        }

        if (CommandId == Guid.Empty)
        {
            ModelState.AddModelError(
                nameof(CommandId),
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
                    CommandId,
                    User.FindAccessUserId()
                        ?? throw new InvalidOperationException(
                            "Administrátor nemá interní ID."),
                    OwnerId,
                    Money.FromCrowns(SignedAmountCrowns),
                    Reason),
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
}
