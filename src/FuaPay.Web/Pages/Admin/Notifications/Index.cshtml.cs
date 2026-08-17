using FuaPay.Web.Modules.Access.Application;
using FuaPay.Web.Modules.Notifications.Application;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FuaPay.Web.Pages.Admin.Notifications;

[Authorize(Roles = "Admin")]
public sealed class IndexModel : PageModel
{
    private readonly INotificationQueries _notificationQueries;
    private readonly IAccessUserQueries _accessUserQueries;

    public IndexModel(
        INotificationQueries notificationQueries,
        IAccessUserQueries accessUserQueries)
    {
        _notificationQueries = notificationQueries;
        _accessUserQueries = accessUserQueries;
    }

    public IReadOnlyList<NotificationOutboxItem> Notifications { get; private set; } = [];
    public IReadOnlyDictionary<Guid, AccessUserOption> Users { get; private set; } =
        new Dictionary<Guid, AccessUserOption>();

    public async Task OnGetAsync(CancellationToken cancellationToken = default)
    {
        Notifications = await _notificationQueries.ListRecentAsync(
            cancellationToken: cancellationToken);
        Users = await _accessUserQueries.FindOptionsAsync(
            Notifications.Select(item => item.RecipientUserId),
            cancellationToken);
    }
}
