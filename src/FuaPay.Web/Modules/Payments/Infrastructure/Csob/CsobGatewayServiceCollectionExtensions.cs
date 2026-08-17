using System.Threading.RateLimiting;

using FuaPay.Web.Modules.Payments.Application;
using FuaPay.Web.Modules.Payments.Infrastructure.Persistence;

using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FuaPay.Web.Modules.Payments.Infrastructure.Csob;

public static class CsobGatewayServiceCollectionExtensions
{
    public static IServiceCollection AddCsobPaymentGateway(
        this IServiceCollection services,
        CsobGatewayConfiguration configuration,
        CsobReconciliationConfiguration? reconciliationConfiguration = null,
        bool activateProviderInitiator = false)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        reconciliationConfiguration ??=
            new CsobReconciliationConfiguration(
                Enabled: false,
                PollInterval: TimeSpan.FromSeconds(15),
                PendingMinimumAge: TimeSpan.FromSeconds(15),
                LeaseDuration: TimeSpan.FromMinutes(3),
                BaseBackoff: TimeSpan.FromSeconds(15),
                MaximumBackoff: TimeSpan.FromMinutes(3),
                MaximumAttempts: 12,
                BatchSize: 20);

        if (reconciliationConfiguration.Enabled)
        {
            reconciliationConfiguration.Validate(configuration);
        }

        services.TryAddSingleton(configuration);
        services.TryAddSingleton(reconciliationConfiguration);
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton(
            new CsobGatewayAvailability(configuration.Enabled));
        services.AddRateLimiter(
            options =>
            {
                options.RejectionStatusCode =
                    StatusCodes.Status429TooManyRequests;
                options.AddPolicy(
                    CsobPaymentReturnEndpoint.RateLimitPolicy,
                    context =>
                        RateLimitPartition.GetFixedWindowLimiter(
                            context.Connection.RemoteIpAddress?.ToString()
                                ?? "unknown",
                            _ => new FixedWindowRateLimiterOptions
                            {
                                PermitLimit = 30,
                                Window = TimeSpan.FromMinutes(1),
                                QueueLimit = 0,
                                QueueProcessingOrder =
                                    QueueProcessingOrder.OldestFirst,
                                AutoReplenishment = true
                            }));
            });

        services.AddScoped<
            EfCsobPaymentRecoveryRepository>();
        services.AddScoped<ICsobPaymentRecoveryRepository>(
            provider => provider.GetRequiredService<
                EfCsobPaymentRecoveryRepository>());
        services.AddScoped<IPaymentReconciliationQueries>(
            provider => provider.GetRequiredService<
                EfCsobPaymentRecoveryRepository>());

        if (!configuration.Enabled)
        {
            if (activateProviderInitiator)
            {
                throw new InvalidOperationException(
                    "ČSOB initiator nelze aktivovat, pokud je ČSOB gateway vypnutá.");
            }

            return services;
        }

        services.AddScoped<CsobPaymentProviderInitiator>();
        if (activateProviderInitiator)
        {
            if (services.Any(
                    descriptor =>
                        descriptor.ServiceType ==
                        typeof(IPaymentProviderInitiator)))
            {
                throw new InvalidOperationException(
                    "Je nakonfigurováno více aktivních payment provider initiatorů.");
            }

            services.AddScoped<IPaymentProviderInitiator>(
                provider => provider.GetRequiredService<
                    CsobPaymentProviderInitiator>());
        }
        services.AddScoped<CsobPaymentReconciliationService>();
        services.AddScoped<ICsobPaymentReconciliationService>(
            provider => provider.GetRequiredService<
                CsobPaymentReconciliationService>());
        services.AddScoped<CsobPaymentRecoveryScheduler>();
        services.AddScoped<ICsobPaymentRecoveryScheduler>(
            provider => provider.GetRequiredService<
                CsobPaymentRecoveryScheduler>());
        services.AddScoped<CsobPaymentRecoveryProcessor>();

        if (reconciliationConfiguration.Enabled)
        {
            services.AddHostedService<CsobPaymentReconciliationWorker>();
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
