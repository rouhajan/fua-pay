using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FuaPay.Web.Modules.Payments.Infrastructure.Csob;

public static class CsobGatewayServiceCollectionExtensions
{
    public static IServiceCollection AddCsobPaymentGateway(
        this IServiceCollection services,
        CsobGatewayConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.TryAddSingleton(configuration);
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton(
            new CsobGatewayAvailability(configuration.Enabled));

        if (!configuration.Enabled)
        {
            return services;
        }

        services.AddSingleton<ICsobGatewaySignature, CsobGatewaySignature>();
        services.AddHttpClient<ICsobGatewayClient, CsobGatewayClient>(
            client =>
            {
                client.BaseAddress = configuration.ApiBaseUri;
                client.Timeout = configuration.RequestTimeout;
                client.DefaultRequestHeaders.Accept.ParseAdd(
                    "application/json");
                client.DefaultRequestHeaders.UserAgent.ParseAdd(
                    "FuaPay/1.0 CSOB-eAPI-1.9");
            })
            .ConfigurePrimaryHttpMessageHandler(
                () => new HttpClientHandler
                {
                    AllowAutoRedirect = false
                });

        return services;
    }
}
