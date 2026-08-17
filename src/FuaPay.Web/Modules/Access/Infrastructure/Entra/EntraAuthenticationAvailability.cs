namespace FuaPay.Web.Modules.Access.Infrastructure.Entra;

public sealed record EntraAuthenticationAvailability(
    bool IsEnabled,
    Guid? TenantId);
