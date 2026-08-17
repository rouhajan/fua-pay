using FuaPay.Web.Modules.Access.Web;
using FuaPay.Web.Modules.Jobs.Application;
using FuaPay.Web.Modules.Jobs.Domain;
using FuaPay.Web.Modules.Payments.Application;
using FuaPay.Web.Modules.Payments.Domain;
using FuaPay.Web.Pages.Shared;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FuaPay.Web.Pages.Customer.Payments;

[Authorize(Roles = "Customer")]
public sealed class DetailsModel : PageModel
{
    private readonly IPaymentQueries _paymentQueries;
    private readonly DevelopmentPaymentService _developmentPaymentService;
    private readonly IJobQueries _jobQueries;
    private readonly PaymentCreationService _paymentCreationService;
    private readonly DevelopmentPaymentAvailability _developmentAvailability;

    public DetailsModel(
        IPaymentQueries paymentQueries,
        DevelopmentPaymentService developmentPaymentService,
        IJobQueries jobQueries,
        PaymentCreationService paymentCreationService,
        DevelopmentPaymentAvailability developmentAvailability)
    {
        ArgumentNullException.ThrowIfNull(paymentQueries);
        ArgumentNullException.ThrowIfNull(developmentPaymentService);
        ArgumentNullException.ThrowIfNull(jobQueries);
        ArgumentNullException.ThrowIfNull(paymentCreationService);
        ArgumentNullException.ThrowIfNull(developmentAvailability);

        _paymentQueries = paymentQueries;
        _developmentPaymentService = developmentPaymentService;
        _jobQueries = jobQueries;
        _paymentCreationService = paymentCreationService;
        _developmentAvailability = developmentAvailability;
    }

    public PaymentDetail Payment { get; private set; } = null!;

    public string? JobNumber { get; private set; }

    public bool JobCanBePaid { get; private set; }

    public bool CanRetryJobPayment =>
        Payment.PurposeType == PaymentPurposeType.Job &&
        PaymentDisplay.IsUnsuccessfulTerminalStatus(Payment.Status) &&
        Payment.JobId.HasValue &&
        JobCanBePaid;

    public bool DevelopmentSimulationEnabled =>
        _developmentAvailability.IsEnabled &&
        Payment.Provider == PaymentProvider.Development;

    public async Task<IActionResult> OnGetAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        return await LoadAsync(id, cancellationToken)
            ? Page()
            : NotFound();
    }

    public Task<IActionResult> OnPostCompleteAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(
            id,
            () => _developmentPaymentService.CompleteAsync(
                RequireCustomerUserId(),
                id,
                cancellationToken),
            "Vývojová platba byla potvrzena.",
            isWarning: false,
            cancellationToken: cancellationToken);
    }

    public Task<IActionResult> OnPostFailAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(
            id,
            () => _developmentPaymentService.FailAsync(
                RequireCustomerUserId(),
                id,
                "Vývojová simulace zamítnuté platby.",
                cancellationToken),
            "Platební pokus byl zamítnut. Peníze nebyly připsány.",
            isWarning: true,
            cancellationToken: cancellationToken);
    }

    public Task<IActionResult> OnPostCancelAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(
            id,
            () => _developmentPaymentService.CancelAsync(
                RequireCustomerUserId(),
                id,
                cancellationToken),
            "Platební pokus byl zrušen. Peníze nebyly připsány.",
            isWarning: true,
            cancellationToken: cancellationToken);
    }

    public async Task<IActionResult> OnPostRetryJobPaymentAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        if (!await LoadAsync(id, cancellationToken))
        {
            return NotFound();
        }

        if (!CanRetryJobPayment || !Payment.JobId.HasValue)
        {
            ModelState.AddModelError(
                string.Empty,
                "Pro tuto platbu nelze vytvořit nový platební pokus.");

            return Page();
        }

        try
        {
            var retry = await _paymentCreationService.CreateJobPaymentAsync(
                RequireCustomerUserId(),
                Payment.JobId.Value,
                cancellationToken);

            return RedirectToPage(
                new { id = retry.Id, view = "customer" });
        }
        catch (Exception exception) when (
            PageOperationError.IsExpected(exception))
        {
            PageOperationError.Add(
                this,
                exception,
                "payment.change",
                "Platební operaci se nepodařilo dokončit. Obnovte stránku a zkuste to znovu.");
            return Page();
        }
    }

    private async Task<IActionResult> ExecuteAsync(
        Guid id,
        Func<Task<bool>> operation,
        string statusMessage,
        bool isWarning,
        CancellationToken cancellationToken)
    {
        if (!_developmentAvailability.IsEnabled)
        {
            return Forbid();
        }

        try
        {
            await operation();
            TempData["StatusMessage"] = statusMessage;
            TempData["StatusMessageKind"] =
                isWarning ? "warning" : "success";
            return RedirectToPage(new { id, view = "customer" });
        }
        catch (Exception exception) when (
            PageOperationError.IsExpected(exception))
        {
            PageOperationError.Add(
                this,
                exception,
                "payment.change",
                "Platební operaci se nepodařilo dokončit. Obnovte stránku a zkuste to znovu.");

            if (!await LoadAsync(id, cancellationToken))
            {
                return NotFound();
            }

            return Page();
        }
    }

    private async Task<bool> LoadAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        var customerUserId = RequireCustomerUserId();

        Payment = await _paymentQueries.FindForCustomerAsync(
                customerUserId,
                id,
                cancellationToken)
            ?? null!;

        if (Payment is null)
        {
            return false;
        }

        if (Payment.JobId.HasValue)
        {
            var job = await _jobQueries.FindForCustomerAsync(
                customerUserId,
                Payment.JobId.Value,
                cancellationToken);

            JobNumber = job?.Number;
            JobCanBePaid =
                job is not null &&
                job.ProductionStatus == JobProductionStatus.Published &&
                job.PaymentStatus == JobPaymentStatus.Unpaid;
        }

        return true;
    }

    private Guid RequireCustomerUserId()
    {
        return User.FindAccessUserId()
            ?? throw new InvalidOperationException(
                "Přihlášený zákazník nemá interní ID.");
    }
}
