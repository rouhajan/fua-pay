namespace FuaPay.Web.Modules.Access.Web;

public sealed record AccessNavigationItem(
    string Label,
    string Page,
    string? SectionPrefix = null,
    bool IsOverview = false);

public static class AccessNavigation
{
    private static readonly IReadOnlyList<AccessNavigationItem>
        CustomerItems =
        [
            new("Přehled", "/Index", IsOverview: true),
            new("Kredit", "/Customer/Credit/Index", "/Customer/Credit"),
            new("Platby", "/Customer/Payments/Index", "/Customer/Payments"),
            new("Zakázky", "/Customer/Jobs/Index", "/Customer/Jobs"),
            new("Nápověda", "/Help", "/Help")
        ];

    private static readonly IReadOnlyList<AccessNavigationItem>
        RequesterItems =
        [
            new("Přehled", "/Index", IsOverview: true),
            new("Nová zakázka", "/Management/Jobs/Create", "/Management/Jobs/Create"),
            new("Zakázky", "/Management/Jobs/Index", "/Management/Jobs"),
            new("Nápověda", "/Help", "/Help")
        ];

    private static readonly IReadOnlyList<AccessNavigationItem>
        AdminItems =
        [
            new("Přehled", "/Index", IsOverview: true),
            new("Uživatelé a role", "/Admin/Users/Index", "/Admin/Users"),
            new("Pracoviště", "/Admin/ServiceUnits/Index", "/Admin/ServiceUnits"),
            new("Zakázky", "/Management/Jobs/Index", "/Management/Jobs"),
            new("Platby", "/Admin/Payments/Index", "/Admin/Payments"),
            new("Kredit", "/Admin/Credit/Index", "/Admin/Credit"),
            new("Exporty", "/Admin/Exports/Index", "/Admin/Exports"),
            new("Audit", "/Admin/Audit/Index", "/Admin/Audit"),
            new("Oznámení", "/Admin/Notifications/Index", "/Admin/Notifications"),
            new("Nápověda", "/Help", "/Help")
        ];

    public static IReadOnlyList<AccessNavigationItem> For(
        AccessView view)
    {
        return view switch
        {
            AccessView.Customer => CustomerItems,
            AccessView.Requester => RequesterItems,
            AccessView.Admin => AdminItems,
            _ => throw new ArgumentOutOfRangeException(
                nameof(view),
                view,
                "Pracovní pohled není podporovaný.")
        };
    }
}
