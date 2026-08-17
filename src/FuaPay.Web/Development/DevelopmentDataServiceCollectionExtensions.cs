using FuaPay.Web.Modules.Access.Development;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FuaPay.Web.Development;

public static class DevelopmentDataServiceCollectionExtensions
{
    public static IServiceCollection AddDevelopmentData(
        this IServiceCollection services,
        bool enabled)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (!enabled)
        {
            return services;
        }

        services.TryAddScoped<DevelopmentSignInService>();
        services.AddScoped<DevelopmentDataSeeder>();
        services.AddScoped<
            IDevelopmentDataResetter,
            EfDevelopmentDataResetter>();

        return services;
    }
}
