using FuaPay.Web.Modules.ServiceUnits.Application;
using FuaPay.Web.Modules.ServiceUnits.Infrastructure.Persistence;

using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FuaPay.Web.Modules.ServiceUnits;

public static class ServiceUnitsModule
{
    public static IServiceCollection AddServiceUnitsModule(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<ServiceUnitAdministrationService>();
        services.AddScoped<
            IServiceUnitRepository,
            EfServiceUnitRepository>();
        services.AddScoped<
            IRequesterServiceUnitAssignmentRepository,
            EfRequesterServiceUnitAssignmentRepository>();
        services.AddScoped<
            IServiceUnitQueries,
            EfServiceUnitQueries>();

        return services;
    }
}
