using FuaPay.Web.Modules.Jobs.Application;
using FuaPay.Web.Modules.Jobs.Infrastructure.Persistence;
using FuaPay.Web.Modules.Jobs.Web;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FuaPay.Web.Modules.Jobs;

public static class JobsModule
{
    public static IServiceCollection AddJobsModule(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<JobManagementService>();
        services.AddScoped<JobSettlementService>();
        services.AddScoped<JobManagementPageContextResolver>();
        services.AddScoped<JobPresentationComposer>();
        services.AddScoped<IJobQueries, EfJobQueries>();
        services.AddScoped<IJobRepository, EfJobRepository>();
        services.AddScoped<IJobNumberAllocator, EfJobNumberAllocator>();

        return services;
    }
}
