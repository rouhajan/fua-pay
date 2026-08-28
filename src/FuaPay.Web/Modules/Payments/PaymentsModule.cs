using FuaPay.Web.Modules.Jobs.Application;
using FuaPay.Web.Modules.Payments.Application;
using FuaPay.Web.Modules.Payments.Domain;
using FuaPay.Web.Modules.Payments.Infrastructure.Persistence;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FuaPay.Web.Modules.Payments;

public static class PaymentsModule
{
    public static IServiceCollection AddPaymentsModule(
        this IServiceCollection services,
        PaymentProvider activeProvider,
        bool developmentPaymentUiEnabled = false)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (
            activeProvider == PaymentProvider.Unknown ||
            !Enum.IsDefined(activeProvider))
        {
            throw new ArgumentOutOfRangeException(nameof(activeProvider));
        }

        if (
            developmentPaymentUiEnabled &&
            activeProvider != PaymentProvider.Development)
        {
            throw new InvalidOperationException(
                "Development payment UI lze povolit pouze s Development providerem.");
        }

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton(
            new DevelopmentPaymentAvailability(
                developmentPaymentUiEnabled));
        services.AddScoped<PaymentCreationService>();
        services.AddScoped<PaymentInitiationService>();
        services.AddScoped<PaymentSettlementService>();
        services.AddScoped<SettlementReturnRegistrationService>();
        services.AddScoped<IPaymentSettlementService>(
            provider => provider.GetRequiredService<
                PaymentSettlementService>());
        services.AddScoped<DevelopmentPaymentService>();
        if (activeProvider == PaymentProvider.Development)
        {
            if (services.Any(
                    descriptor =>
                        descriptor.ServiceType ==
                        typeof(IPaymentProviderInitiator)))
            {
                throw new InvalidOperationException(
                    "Je nakonfigurováno více aktivních payment provider initiatorů.");
            }

            services.AddScoped<DevelopmentPaymentProviderInitiator>();
            services.AddScoped<IPaymentProviderInitiator>(
                provider => provider.GetRequiredService<
                    DevelopmentPaymentProviderInitiator>());
        }
        services.AddScoped<IPaymentRepository, EfPaymentRepository>();
        services.AddScoped<
            ISettlementReturnRepository,
            EfSettlementReturnRepository>();
        services.AddScoped<
            IJobPaymentCoordination,
            EfJobPaymentCoordination>();
        services.AddScoped<
            IPaymentInitiationRepository,
            EfPaymentInitiationRepository>();
        services.AddScoped<
            IPaymentOrderNumberAllocator,
            EfPaymentOrderNumberAllocator>();
        services.AddScoped<IPaymentQueries, EfPaymentQueries>();

        return services;
    }
}
