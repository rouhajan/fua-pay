using FuaPay.Web.Modules.Access.Web;
using FuaPay.Web.Modules.Jobs.Application;
using FuaPay.Web.Modules.Jobs.Domain;
using FuaPay.Web.Modules.Jobs.Web;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FuaPay.Web.Pages.Customer.Jobs;

[Authorize(Roles = "Customer")]
public sealed class IndexModel : PageModel
{
    private const int PageSize = 25;

    private readonly IJobQueries _jobQueries;
    private readonly JobPresentationComposer _composer;

    public IndexModel(
        IJobQueries jobQueries,
        JobPresentationComposer composer)
    {
        ArgumentNullException.ThrowIfNull(jobQueries);
        ArgumentNullException.ThrowIfNull(composer);
        _jobQueries = jobQueries;
        _composer = composer;
    }

    public JobPage<JobListItem> Jobs { get; private set; } =
        new([], 0, PageSize, 0);

    public IReadOnlyList<JobListPresentation> Rows { get; private set; } = [];

    public string? Search { get; private set; }

    public JobProductionStatus? ProductionStatus { get; private set; }

    public JobPaymentStatus? PaymentStatus { get; private set; }

    public async Task OnGetAsync(
        string? search = null,
        JobProductionStatus? productionStatus = null,
        JobPaymentStatus? paymentStatus = null,
        int offset = 0,
        CancellationToken cancellationToken = default)
    {
        var customerUserId = User.FindAccessUserId()
            ?? throw new InvalidOperationException(
                "Přihlášený zákazník nemá interní ID.");

        Search = string.IsNullOrWhiteSpace(search)
            ? null
            : search.Trim();
        ProductionStatus = NormalizeProductionStatus(productionStatus);
        PaymentStatus = NormalizePaymentStatus(paymentStatus);

        Jobs = await _jobQueries.ListForCustomerAsync(
            customerUserId,
            new JobListFilter(
                ProductionStatus,
                PaymentStatus,
                search: Search),
            new JobPageRequest(Math.Max(0, offset), PageSize),
            cancellationToken);

        Rows = await _composer.ComposeAsync(
            Jobs.Items,
            cancellationToken);
    }

    private static JobProductionStatus? NormalizeProductionStatus(
        JobProductionStatus? status)
    {
        return status.HasValue &&
               status.Value != JobProductionStatus.Unknown &&
               Enum.IsDefined(status.Value)
            ? status
            : null;
    }

    private static JobPaymentStatus? NormalizePaymentStatus(
        JobPaymentStatus? status)
    {
        return status.HasValue &&
               status.Value != JobPaymentStatus.Unknown &&
               Enum.IsDefined(status.Value)
            ? status
            : null;
    }
}
