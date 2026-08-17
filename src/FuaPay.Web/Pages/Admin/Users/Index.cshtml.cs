using FuaPay.Web.Modules.Access.Application;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FuaPay.Web.Pages.Admin.Users;

[Authorize(Roles = "Admin")]
public sealed class IndexModel : PageModel
{
    private readonly IAccessUserQueries _queries;

    public IndexModel(IAccessUserQueries queries)
    {
        ArgumentNullException.ThrowIfNull(queries);
        _queries = queries;
    }

    public AccessUserPage Users { get; private set; } =
        new([], 0, AccessUserListRequest.DefaultLimit, 0);

    public string? Search { get; private set; }

    public async Task OnGetAsync(
        string? search = null,
        int offset = 0,
        CancellationToken cancellationToken = default)
    {
        Search = string.IsNullOrWhiteSpace(search)
            ? null
            : search.Trim();

        Users = await _queries.ListAsync(
            new AccessUserListRequest(Search, offset),
            cancellationToken);
    }
}
