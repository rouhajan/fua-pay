using FuaPay.Web.Modules.Access.Application;
using FuaPay.Web.Modules.Audit.Application;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FuaPay.Web.Pages.Admin.Audit;

[Authorize(Roles = "Admin")]
public sealed class IndexModel : PageModel
{
    private const int PageSize = 50;
    private readonly IAuditQueries _auditQueries;
    private readonly IAccessUserQueries _accessUserQueries;

    public IndexModel(
        IAuditQueries auditQueries,
        IAccessUserQueries accessUserQueries)
    {
        _auditQueries = auditQueries;
        _accessUserQueries = accessUserQueries;
    }

    public AuditPage Events { get; private set; } =
        new([], 0, PageSize, 0);

    public IReadOnlyDictionary<Guid, AccessUserOption> Users { get; private set; } =
        new Dictionary<Guid, AccessUserOption>();

    public string? Search { get; private set; }

    public async Task OnGetAsync(
        string? search = null,
        int offset = 0,
        CancellationToken cancellationToken = default)
    {
        Search = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
        Events = await _auditQueries.ListAsync(
            new AuditListFilter(Search),
            new AuditPageRequest(Math.Max(0, offset), PageSize),
            cancellationToken);

        Users = await _accessUserQueries.FindOptionsAsync(
            Events.Items
                .Where(item => item.ActorUserId.HasValue)
                .Select(item => item.ActorUserId!.Value),
            cancellationToken);
    }
}
