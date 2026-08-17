using FuaPay.Web.Modules.Access.Domain;
using FuaPay.Web.Modules.Jobs.Domain;
using FuaPay.Web.Modules.ServiceUnits.Domain;

namespace FuaPay.Web.Pages;

public static class AdministrationDisplay
{
    public static string AccessRoleLabel(AccessRole role)
    {
        return role switch
        {
            AccessRole.Customer => "Zákazník",
            AccessRole.Requester => "Zadavatel",
            AccessRole.Admin => "Administrátor",
            _ => "Neznámá role"
        };
    }

    public static string AccessUserStatusLabel(
        AccessUserStatus status)
    {
        return status switch
        {
            AccessUserStatus.Active => "Aktivní",
            AccessUserStatus.Blocked => "Zablokovaný",
            _ => "Neznámý stav"
        };
    }

    public static string ServiceUnitStatusLabel(
        ServiceUnitStatus status)
    {
        return status switch
        {
            ServiceUnitStatus.Active => "Aktivní",
            ServiceUnitStatus.Inactive => "Neaktivní",
            _ => "Neznámý stav"
        };
    }

    public static string ServiceTypeLabel(
        ServiceType serviceType)
    {
        return DashboardDisplay.FormatServiceType(serviceType);
    }
}
