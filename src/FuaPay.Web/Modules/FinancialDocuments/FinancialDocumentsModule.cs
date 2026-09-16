using FuaPay.Web.Modules.FinancialDocuments.Application;
using FuaPay.Web.Modules.FinancialDocuments.Infrastructure.Persistence;

namespace FuaPay.Web.Modules.FinancialDocuments;

public static class FinancialDocumentsModule
{
    public static IServiceCollection AddFinancialDocumentsModule(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<
            IFinancialDocumentRepository,
            EfFinancialDocumentRepository>();
        services.AddScoped<
            IFinancialDocumentNumberAllocator,
            EfFinancialDocumentNumberAllocator>();

        return services;
    }
}
