namespace FuaPay.Web.BuildingBlocks.Pdf;

public static class PdfInfrastructure
{
    public static IServiceCollection AddFuaPayPdf(
        this IServiceCollection services,
        PdfAssetsConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddSingleton(configuration);
        services.AddSingleton<PdfSharpFontManager>();
        return services;
    }
}
