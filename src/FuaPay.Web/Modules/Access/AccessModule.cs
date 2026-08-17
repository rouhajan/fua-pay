using FuaPay.Web.Modules.Access.Application;
using FuaPay.Web.Modules.Access.Infrastructure.Persistence;
using FuaPay.Web.Modules.Access.Web;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FuaPay.Web.Modules.Access;

public static class AccessModule
{
    public static IServiceCollection AddAccessModule(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<AccessIdentityService>();
        services.AddScoped<AccessUserAdministrationService>();
        services.AddScoped<ExternalIdentityAdministrationService>();
        services.AddScoped<AccessSessionSynchronizer>();
        services.AddScoped<
            IAccessUserRepository,
            EfAccessUserRepository>();
        services.AddScoped<IAccessUserQueries, EfAccessUserQueries>();
        services.AddScoped<
            IAccessSessionQueries,
            EfAccessSessionQueries>();
        services.AddScoped<
            IAccessAdministrationLock,
            EfAccessAdministrationLock>();
        services.AddScoped<
            IExternalIdentityLinkRepository,
            EfExternalIdentityLinkRepository>();

        return services;
    }
}
