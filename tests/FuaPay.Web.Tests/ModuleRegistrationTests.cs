using FuaPay.Web.BuildingBlocks.Auditing;
using FuaPay.Web.BuildingBlocks.Notifications;
using FuaPay.Web.Development;
using FuaPay.Web.Modules.Access;
using FuaPay.Web.Modules.Access.Application;
using FuaPay.Web.Modules.Access.Development;
using FuaPay.Web.Modules.Access.Web;
using FuaPay.Web.Modules.Audit;
using FuaPay.Web.Modules.Audit.Application;
using FuaPay.Web.Modules.Credits;
using FuaPay.Web.Modules.Credits.Application;
using FuaPay.Web.Modules.Jobs;
using FuaPay.Web.Modules.Jobs.Application;
using FuaPay.Web.Modules.Notifications;
using FuaPay.Web.Modules.Notifications.Application;
using FuaPay.Web.Modules.Payments;
using FuaPay.Web.Modules.Payments.Application;
using FuaPay.Web.Modules.Payments.Domain;
using FuaPay.Web.Modules.Payments.Infrastructure.Csob;
using FuaPay.Web.Modules.Reporting;
using FuaPay.Web.Modules.Reporting.Application;
using FuaPay.Web.Modules.ServiceUnits;
using FuaPay.Web.Modules.ServiceUnits.Application;

using Microsoft.Extensions.DependencyInjection;

namespace FuaPay.Web.Tests;

public sealed class ModuleRegistrationTests
{
    [Fact]
    public void AddModules_ReturnsSameServiceCollection()
    {
        var services = new ServiceCollection();

        var result = services
            .AddAuditModule()
            .AddAccessModule()
            .AddCreditsModule()
            .AddJobsModule()
            .AddServiceUnitsModule()
            .AddPaymentsModule(PaymentProvider.Development)
            .AddNotificationsModule()
            .AddReportingModule()
            .AddDevelopmentData(enabled: false);

        Assert.Same(services, result);
    }

    [Fact]
    public void AddDevelopmentData_RegistersServicesOnlyWhenEnabled()
    {
        var disabledServices = new ServiceCollection();
        var enabledServices = new ServiceCollection();
        var combinedServices = new ServiceCollection();

        disabledServices.AddDevelopmentData(enabled: false);
        enabledServices.AddDevelopmentData(enabled: true);
        combinedServices
            .AddAccessModule(developmentSignInEnabled: true)
            .AddDevelopmentData(enabled: true);

        Assert.DoesNotContain(
            disabledServices,
            descriptor =>
                descriptor.ServiceType ==
                typeof(DevelopmentDataSeeder));

        Assert.Contains(
            enabledServices,
            descriptor =>
                descriptor.ServiceType ==
                typeof(DevelopmentDataSeeder));

        Assert.Contains(
            enabledServices,
            descriptor =>
                descriptor.ServiceType ==
                typeof(IDevelopmentDataResetter));

        Assert.DoesNotContain(
            disabledServices,
            descriptor =>
                descriptor.ServiceType ==
                typeof(DevelopmentSignInService));

        Assert.Contains(
            enabledServices,
            descriptor =>
                descriptor.ServiceType ==
                typeof(DevelopmentSignInService));

        Assert.Single(
            combinedServices,
            descriptor =>
                descriptor.ServiceType ==
                typeof(DevelopmentSignInService));
    }

    [Fact]
    public void AddAccessModule_RegistersDevelopmentSignInOnlyWhenEnabled()
    {
        var disabledServices = new ServiceCollection();
        var enabledServices = new ServiceCollection();

        disabledServices.AddAccessModule();
        enabledServices.AddAccessModule(
            developmentSignInEnabled: true);

        Assert.DoesNotContain(
            disabledServices,
            descriptor =>
                descriptor.ServiceType ==
                typeof(DevelopmentSignInService));

        Assert.Contains(
            enabledServices,
            descriptor =>
                descriptor.ServiceType ==
                typeof(DevelopmentSignInService));
    }


    [Fact]
    public void AddJobsModule_RegistersQueriesRepositoryAndNumberAllocator()
    {
        var services = new ServiceCollection();

        services.AddJobsModule();

        Assert.Contains(
            services,
            descriptor =>
                descriptor.ServiceType == typeof(IJobQueries));

        Assert.Contains(
            services,
            descriptor =>
                descriptor.ServiceType == typeof(IJobRepository));

        Assert.Contains(
            services,
            descriptor =>
                descriptor.ServiceType == typeof(IJobNumberAllocator));
    }

    [Fact]
    public void AddServiceUnitsModule_RegistersQueriesAndRepositories()
    {
        var services = new ServiceCollection();

        services.AddServiceUnitsModule();

        Assert.Contains(
            services,
            descriptor =>
                descriptor.ServiceType ==
                typeof(IServiceUnitQueries));

        Assert.Contains(
            services,
            descriptor =>
                descriptor.ServiceType ==
                typeof(IServiceUnitRepository));

        Assert.Contains(
            services,
            descriptor =>
                descriptor.ServiceType ==
                typeof(IRequesterServiceUnitAssignmentRepository));
    }

    [Fact]
    public void AddCreditsModule_RegistersCreditQueries()
    {
        var services = new ServiceCollection();

        services.AddCreditsModule();

        Assert.Contains(
            services,
            descriptor =>
                descriptor.ServiceType ==
                typeof(ICreditQueries));
    }
    [Fact]
    public void AddAccessModule_RegistersAdministrationAndQueries()
    {
        var services = new ServiceCollection();

        services.AddAccessModule();

        Assert.Contains(
            services,
            descriptor =>
                descriptor.ServiceType ==
                typeof(IAccessUserQueries));

        Assert.Contains(
            services,
            descriptor =>
                descriptor.ServiceType ==
                typeof(IAccessSessionQueries));

        Assert.Contains(
            services,
            descriptor =>
                descriptor.ServiceType ==
                typeof(AccessSessionSynchronizer));

        Assert.Contains(
            services,
            descriptor =>
                descriptor.ServiceType ==
                typeof(AccessUserAdministrationService));
        Assert.Contains(
            services,
            descriptor =>
                descriptor.ServiceType ==
                typeof(ExternalIdentityAdministrationService));
        Assert.Contains(
            services,
            descriptor =>
                descriptor.ServiceType ==
                typeof(IExternalIdentityLinkRepository));
        Assert.Contains(
            services,
            descriptor =>
                descriptor.ServiceType ==
                typeof(IAccessAdministrationLock));
    }

    [Fact]
    public void AddPaymentsModule_RegistersPaymentServicesAndQueries()
    {
        var services = new ServiceCollection();

        services.AddPaymentsModule(PaymentProvider.Development);

        Assert.Contains(
            services,
            descriptor =>
                descriptor.ServiceType == typeof(IPaymentQueries));

        Assert.Contains(
            services,
            descriptor =>
                descriptor.ServiceType == typeof(IPaymentRepository));

        Assert.Contains(
            services,
            descriptor =>
                descriptor.ServiceType == typeof(PaymentCreationService));

        Assert.Contains(
            services,
            descriptor =>
                descriptor.ServiceType == typeof(PaymentInitiationService));

        Assert.Contains(
            services,
            descriptor =>
                descriptor.ServiceType == typeof(IPaymentInitiationRepository));

        Assert.Contains(
            services,
            descriptor =>
                descriptor.ServiceType == typeof(IPaymentOrderNumberAllocator));

        Assert.Contains(
            services,
            descriptor =>
                descriptor.ServiceType == typeof(IPaymentProviderInitiator));

        Assert.Contains(
            services,
            descriptor =>
                descriptor.ServiceType == typeof(PaymentSettlementService));

        Assert.Contains(
            services,
            descriptor =>
                descriptor.ServiceType ==
                typeof(IPaymentSettlementService));

        Assert.Contains(
            services,
            descriptor =>
                descriptor.ServiceType == typeof(DevelopmentPaymentService));

        var availability = Assert.Single(
            services,
            descriptor =>
                descriptor.ServiceType ==
                typeof(DevelopmentPaymentAvailability));
        Assert.False(
            Assert.IsType<DevelopmentPaymentAvailability>(
                availability.ImplementationInstance).IsEnabled);
    }

    [Fact]
    public void AddCsobPaymentGateway_DisabledKeepsReadModelWithoutExternalClient()
    {
        var services = new ServiceCollection();
        var configuration = new CsobGatewayConfiguration(
            false,
            CsobGatewayConfiguration.SandboxApiBaseUri,
            string.Empty,
            string.Empty,
            string.Empty,
            new Uri("https://localhost/payments/csob/return"),
            900,
            TimeSpan.FromSeconds(30));

        var result = services.AddCsobPaymentGateway(configuration);

        Assert.Same(services, result);
        var availability = Assert.Single(
            services,
            descriptor =>
                descriptor.ServiceType ==
                typeof(CsobGatewayAvailability));
        Assert.False(
            Assert.IsType<CsobGatewayAvailability>(
                availability.ImplementationInstance).IsEnabled);
        Assert.DoesNotContain(
            services,
            descriptor =>
                descriptor.ServiceType == typeof(ICsobGatewayClient));
        Assert.DoesNotContain(
            services,
            descriptor =>
                descriptor.ServiceType ==
                typeof(ICsobPaymentReconciliationService));
        Assert.Contains(
            services,
            descriptor =>
                descriptor.ServiceType ==
                typeof(IPaymentReconciliationQueries));
        Assert.Contains(
            services,
            descriptor =>
                descriptor.ServiceType ==
                typeof(ICsobPaymentRecoveryRepository));
    }

    [Fact]
    public void AddCsobPaymentGateway_EnabledRegistersProtocolBoundary()
    {
        var services = new ServiceCollection();
        var configuration = new CsobGatewayConfiguration(
            true,
            CsobGatewayConfiguration.SandboxApiBaseUri,
            "M1MIPS0000",
            "unused-private-key",
            "unused-public-key",
            new Uri("https://shop.example.com/payments/csob/return"),
            900,
            TimeSpan.FromSeconds(30));

        services.AddCsobPaymentGateway(configuration);

        Assert.Contains(
            services,
            descriptor =>
                descriptor.ServiceType == typeof(ICsobGatewayClient));
        Assert.Contains(
            services,
            descriptor =>
                descriptor.ServiceType == typeof(ICsobGatewaySignature));
        Assert.Contains(
            services,
            descriptor =>
                descriptor.ServiceType ==
                typeof(ICsobPaymentReconciliationService));
        Assert.Contains(
            services,
            descriptor =>
                descriptor.ServiceType ==
                typeof(CsobPaymentReconciliationService));
        Assert.Contains(
            services,
            descriptor =>
                descriptor.ServiceType == typeof(TimeProvider));
        Assert.Contains(
            services,
            descriptor =>
                descriptor.ServiceType ==
                typeof(ICsobPaymentRecoveryScheduler));
        Assert.DoesNotContain(
            services,
            descriptor =>
                descriptor.ServiceType ==
                typeof(IPaymentProviderInitiator));
    }

    [Fact]
    public void PaymentProviderSelection_DevelopmentResolvesExactlyOneDevelopmentInitiator()
    {
        var services = new ServiceCollection();
        services.AddPaymentsModule(
            PaymentProvider.Development,
            developmentPaymentUiEnabled: true);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var initiator = scope.ServiceProvider
            .GetRequiredService<IPaymentProviderInitiator>();

        Assert.IsType<DevelopmentPaymentProviderInitiator>(initiator);
        Assert.Single(
            services,
            descriptor =>
                descriptor.ServiceType ==
                typeof(IPaymentProviderInitiator));
    }

    [Fact]
    public void PaymentProviderSelection_CsobResolvesExactlyOneCsobInitiator()
    {
        var services = new ServiceCollection();
        var configuration = CreateEnabledCsobConfiguration();
        services.AddLogging();
        services.AddPaymentsModule(PaymentProvider.Csob);
        services.AddCsobPaymentGateway(
            configuration,
            activateProviderInitiator: true);
        services.AddScoped<ICsobGatewayClient, StubCsobGatewayClient>();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var initiator = scope.ServiceProvider
            .GetRequiredService<IPaymentProviderInitiator>();

        Assert.IsType<CsobPaymentProviderInitiator>(initiator);
        Assert.Single(
            services,
            descriptor =>
                descriptor.ServiceType ==
                typeof(IPaymentProviderInitiator));
    }

    [Fact]
    public void PaymentProviderSelection_ConflictingRegistrationsFailImmediately()
    {
        var services = new ServiceCollection();
        services.AddPaymentsModule(PaymentProvider.Development);

        Assert.Throws<InvalidOperationException>(
            () => services.AddCsobPaymentGateway(
                CreateEnabledCsobConfiguration(),
                activateProviderInitiator: true));
    }

    [Fact]
    public void PaymentProviderSelection_ReverseConflictingRegistrationsFailImmediately()
    {
        var services = new ServiceCollection();
        services.AddCsobPaymentGateway(
            CreateEnabledCsobConfiguration(),
            activateProviderInitiator: true);

        Assert.Throws<InvalidOperationException>(
            () => services.AddPaymentsModule(
                PaymentProvider.Development));
    }

    private static CsobGatewayConfiguration CreateEnabledCsobConfiguration() =>
        new(
            true,
            CsobGatewayConfiguration.SandboxApiBaseUri,
            "M1MIPS0000",
            "unused-private-key",
            "unused-public-key",
            new Uri("https://shop.example.com/payments/csob/return"),
            900,
            TimeSpan.FromSeconds(30));

    private sealed class StubCsobGatewayClient : ICsobGatewayClient
    {
        public Task<CsobEchoResult> EchoAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<CsobPaymentInitResult> InitializeAsync(
            CsobPaymentInit payment,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<CsobPaymentStatusResult> GetStatusAsync(
            string payId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    [Fact]
    public void AddCsobPaymentGateway_EnabledReconciliationRegistersWorker()
    {
        var services = new ServiceCollection();
        var configuration = new CsobGatewayConfiguration(
            true,
            CsobGatewayConfiguration.SandboxApiBaseUri,
            "M1MIPS0000",
            "unused-private-key",
            "unused-public-key",
            new Uri("https://shop.example.com/payments/csob/return"),
            900,
            TimeSpan.FromSeconds(30));
        var reconciliation = new CsobReconciliationConfiguration(
            Enabled: true,
            PollInterval: TimeSpan.FromSeconds(15),
            PendingMinimumAge: TimeSpan.FromSeconds(15),
            LeaseDuration: TimeSpan.FromMinutes(3),
            BaseBackoff: TimeSpan.FromSeconds(15),
            MaximumBackoff: TimeSpan.FromMinutes(3),
            MaximumAttempts: 12,
            BatchSize: 20);

        services.AddCsobPaymentGateway(configuration, reconciliation);

        Assert.Contains(
            services,
            descriptor =>
                descriptor.ServiceType ==
                typeof(CsobPaymentRecoveryProcessor));
        Assert.Contains(
            services,
            descriptor =>
                descriptor.ServiceType ==
                typeof(Microsoft.Extensions.Hosting.IHostedService) &&
                descriptor.ImplementationType ==
                typeof(CsobPaymentReconciliationWorker));
    }

    [Fact]
    public void AddReportingModule_RegistersCsvExportService()
    {
        var services = new ServiceCollection();

        services.AddReportingModule();

        Assert.Contains(
            services,
            descriptor =>
                descriptor.ServiceType ==
                typeof(AdministrationCsvExportService));
    }

    [Fact]
    public void AddAuditModule_RegistersTrailAndQueries()
    {
        var services = new ServiceCollection();

        services.AddAuditModule();

        Assert.Contains(
            services,
            descriptor => descriptor.ServiceType == typeof(IAuditTrail));
        Assert.Contains(
            services,
            descriptor => descriptor.ServiceType == typeof(IAuditQueries));
    }

    [Fact]
    public void AddNotificationsModule_RegistersOutboxAndQueries()
    {
        var services = new ServiceCollection();

        services.AddNotificationsModule();

        Assert.Contains(
            services,
            descriptor => descriptor.ServiceType == typeof(INotificationOutbox));
        Assert.Contains(
            services,
            descriptor => descriptor.ServiceType == typeof(INotificationQueries));
    }

}
