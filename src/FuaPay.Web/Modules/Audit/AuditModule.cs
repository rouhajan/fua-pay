using FuaPay.Web.BuildingBlocks.Auditing;
using FuaPay.Web.Modules.Audit.Application;
using FuaPay.Web.Modules.Audit.Infrastructure.Persistence;

namespace FuaPay.Web.Modules.Audit;

public static class AuditModule
{
    public static IServiceCollection AddAuditModule(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<IAuditTrail, EfAuditTrail>();
        services.AddScoped<IAuditQueries, EfAuditQueries>();
        return services;
    }
}
