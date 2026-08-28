using FuaPay.Web.Modules.Credits.Application;
using FuaPay.Web.Modules.Credits.Infrastructure.Persistence;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FuaPay.Web.Modules.Credits;

public static class CreditsModule
{
    public static IServiceCollection AddCreditsModule(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<CreditAvailabilityService>();
        services.AddScoped<CreditService>();
        services.AddScoped<CreditAdministrationService>();
        services.AddScoped<PrintReservationService>();
        services.AddScoped<CreditReturnHoldService>();
        services.AddScoped<
            ICreditQueries,
            EfCreditQueries>();
        services.AddScoped<
            ICreditAccountRepository,
            EfCreditAccountRepository>();
        services.AddScoped<
            ICreditAdjustmentCommandRepository,
            EfCreditAdjustmentCommandRepository>();
        services.AddScoped<
            IPrintReservationRepository,
            EfPrintReservationRepository>();
        services.AddScoped<
            ICreditAvailabilityRepository,
            EfCreditAvailabilityRepository>();
        services.AddScoped<
            ICreditReturnHoldRepository,
            EfCreditReturnHoldRepository>();

        return services;
    }
}
