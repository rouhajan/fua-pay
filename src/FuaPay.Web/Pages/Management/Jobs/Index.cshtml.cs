using FuaPay.Web.Modules.Access.Application;
using FuaPay.Web.Modules.Jobs.Application;
using FuaPay.Web.Modules.Jobs.Domain;
using FuaPay.Web.Modules.Jobs.Web;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FuaPay.Web.Pages.Management.Jobs;

[Authorize(Roles = "Requester,Admin")]
public sealed class IndexModel : PageModel
{
    private const int PageSize = 25;

    private readonly JobManagementPageContextResolver _contextResolver;
    private readonly IJobQueries _jobQueries;
    private readonly IAccessUserQueries _accessUserQueries;
    private readonly JobPresentationComposer _composer;

    public IndexModel(
        JobManagementPageContextResolver contextResolver,
        IJobQueries jobQueries,
        IAccessUserQueries accessUserQueries,
        JobPresentationComposer composer)
    {
        ArgumentNullException.ThrowIfNull(contextResolver);
        ArgumentNullException.ThrowIfNull(jobQueries);
        ArgumentNullException.ThrowIfNull(accessUserQueries);
        ArgumentNullException.ThrowIfNull(composer);

        _contextResolver = contextResolver;
        _jobQueries = jobQueries;
        _accessUserQueries = accessUserQueries;
        _composer = composer;
    }

    public JobManagementPageContext Context { get; private set; } = null!;

    public JobPage<JobListItem> Jobs { get; private set; } =
        new([], 0, PageSize, 0);

    public IReadOnlyList<JobListPresentation> Rows { get; private set; } = [];

    public IReadOnlyList<AccessUserOption> Customers { get; private set; } = [];

    public string? Search { get; private set; }

    public JobProductionStatus? ProductionStatus { get; private set; }

    public JobPaymentStatus? PaymentStatus { get; private set; }

    public Guid? CustomerUserId { get; private set; }

    public async Task OnGetAsync(
        string? view = null,
        Guid? unit = null,
        string? search = null,
        JobProductionStatus? productionStatus = null,
        JobPaymentStatus? paymentStatus = null,
        Guid? customerUserId = null,
        int offset = 0,
        CancellationToken cancellationToken = default)
    {
        Context = await _contextResolver.ResolveAsync(
                User,
                view,
                unit,
                cancellationToken)
            ?? throw new InvalidOperationException(
                "Přihlášený uživatel nemá přístup ke správě zakázek.");

        Search = string.IsNullOrWhiteSpace(search)
            ? null
            : search.Trim();
        ProductionStatus = NormalizeProductionStatus(productionStatus);
        PaymentStatus = NormalizePaymentStatus(paymentStatus);
        CustomerUserId = customerUserId == Guid.Empty
            ? null
            : customerUserId;

        var filter = new JobListFilter(
            ProductionStatus,
            PaymentStatus,
            customerUserId: CustomerUserId,
            search: Search);

        Jobs = await _jobQueries.ListForManagementAsync(
            Context.Actor,
            filter,
            new JobPageRequest(Math.Max(0, offset), PageSize),
            cancellationToken);

        Rows = await _composer.ComposeAsync(
            Jobs.Items,
            cancellationToken);

        Customers = await _accessUserQueries.ListActiveCustomersAsync(
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
