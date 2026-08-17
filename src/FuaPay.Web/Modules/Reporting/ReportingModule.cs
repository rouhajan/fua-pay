using FuaPay.Web.Modules.Reporting.Application;

using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FuaPay.Web.Modules.Reporting;

public static class ReportingModule
{
    public static IServiceCollection AddReportingModule(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<AdministrationCsvExportService>();

        return services;
    }
}
