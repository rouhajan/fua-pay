using FuaPay.Web.Modules.FinancialDocuments.Application;
using FuaPay.Web.Modules.FinancialDocuments.Infrastructure.Pdf;
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
            IFinancialDocumentQueries,
            EfFinancialDocumentQueries>();
        services.AddScoped<
            IFinancialDocumentNumberAllocator,
            EfFinancialDocumentNumberAllocator>();
        services.AddSingleton<
            IFinancialDocumentIssuanceProfile,
            ApprovedFinancialDocumentIssuanceProfile>();
        services.AddSingleton<
            IFinancialDocumentPdfRenderer,
            PdfSharpFinancialDocumentRenderer>();
        services.AddScoped<FinancialDocumentDownloadService>();

        return services;
    }
}
