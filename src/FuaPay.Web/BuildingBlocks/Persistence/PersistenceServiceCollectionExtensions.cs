using FuaPay.Web.BuildingBlocks.Application;

using Microsoft.EntityFrameworkCore;

namespace FuaPay.Web.BuildingBlocks.Persistence;

public static class PersistenceServiceCollectionExtensions
{
    public static IServiceCollection AddFuaPayPersistence(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddDbContext<FuaPayDbContext>(options =>
        {
            var connectionString =
                configuration.GetConnectionString("FuaPay");

            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException(
                    "Connection string 'FuaPay' není nastaven.");
            }

            options.UseNpgsql(
                connectionString,
                npgsqlOptions =>
                {
                    npgsqlOptions.MigrationsHistoryTable(
                        "__ef_migrations_history",
                        "app");
                });
        });

        services.AddScoped<
            IApplicationTransaction,
            EfApplicationTransaction>();

        return services;
    }
}
