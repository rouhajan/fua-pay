using FuaPay.Web.Modules.Receipts.Application;
using FuaPay.Web.Modules.Receipts.Infrastructure.Pdf;

using Microsoft.Extensions.DependencyInjection;

namespace FuaPay.Web.Modules.Receipts;

public static class ReceiptsModule
{
    public static IServiceCollection AddReceiptsModule(
        this IServiceCollection services,
        ReceiptConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddSingleton(configuration);
        services.AddScoped<JobPaymentReceiptService>();
        services.AddSingleton<IReceiptPdfRenderer, PdfSharpReceiptPdfRenderer>();

        return services;
    }
}
