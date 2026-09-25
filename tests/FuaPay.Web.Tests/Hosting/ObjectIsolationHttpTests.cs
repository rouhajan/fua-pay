using System.Net;

using FuaPay.Web.Modules.Access.Application;
using FuaPay.Web.Modules.Access.Domain;
using FuaPay.Web.Modules.Access.Web;
using FuaPay.Web.Modules.FinancialDocuments.Application;
using FuaPay.Web.Modules.FinancialDocuments.Domain;
using FuaPay.Web.Modules.Jobs.Application;
using FuaPay.Web.Modules.Jobs.Domain;
using FuaPay.Web.Modules.Payments.Application;
using FuaPay.Web.Modules.Payments.Domain;
using FuaPay.Web.Modules.ServiceUnits.Application;
using FuaPay.Web.Tests.Testing;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace FuaPay.Web.Tests.Hosting;

public sealed class ObjectIsolationHttpTests :
    IClassFixture<ConfiguredWebApplicationFactory>
{
    private readonly ConfiguredWebApplicationFactory _factory;

    public ObjectIsolationHttpTests(ConfiguredWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task CustomerCraftedForeignObjectUrlsReturnNotFoundWithoutEffects()
    {
        var session = CustomerSession();
        var sessionQueries = new FixedSessionQueries(session);
        var paymentQueries = new RecordingPaymentQueries();
        var jobQueries = new RecordingJobQueries();
        var documentQueries = new RecordingFinancialDocumentQueries();
        var settlement = new RecordingSettlementService();
        using var configuredFactory = _factory.WithWebHostBuilder(
            builder => builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IAccessSessionQueries>();
                services.AddSingleton<IAccessSessionQueries>(sessionQueries);
                services.RemoveAll<IPaymentQueries>();
                services.AddSingleton<IPaymentQueries>(paymentQueries);
                services.RemoveAll<IJobQueries>();
                services.AddSingleton<IJobQueries>(jobQueries);
                services.RemoveAll<IFinancialDocumentQueries>();
                services.AddSingleton<IFinancialDocumentQueries>(
                    documentQueries);
                services.RemoveAll<IPaymentSettlementService>();
                services.AddSingleton<IPaymentSettlementService>(settlement);
            }));
        using var client = CreateAuthenticatedClient(
            configuredFactory,
            session);
        var foreignPaymentId = Guid.NewGuid();
        var foreignDocumentId = Guid.NewGuid();
        var foreignJobId = Guid.NewGuid();

        var paths = new[]
        {
            $"/Customer/Payments/Details/{foreignPaymentId}",
            $"/Customer/Payments/Details/{foreignPaymentId}?handler=Status",
            $"/Customer/FinancialDocuments/{foreignDocumentId}/document.pdf",
            $"/Customer/Jobs/{foreignJobId}/receipt.pdf",
            $"/Customer/Jobs/Details/{foreignJobId}"
        };

        foreach (var path in paths)
        {
            using var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        Assert.Equal(2, paymentQueries.CustomerLookups.Count);
        Assert.All(paymentQueries.CustomerLookups, lookup =>
        {
            Assert.Equal(session.UserId, lookup.CustomerUserId);
            Assert.Equal(foreignPaymentId, lookup.ObjectId);
        });
        var documentLookup = Assert.Single(documentQueries.CustomerLookups);
        Assert.Equal(session.UserId, documentLookup.CustomerUserId);
        Assert.Equal(foreignDocumentId, documentLookup.ObjectId);
        Assert.Equal(2, jobQueries.CustomerLookups.Count);
        Assert.All(jobQueries.CustomerLookups, lookup =>
        {
            Assert.Equal(session.UserId, lookup.CustomerUserId);
            Assert.Equal(foreignJobId, lookup.ObjectId);
        });
        Assert.Equal(0, settlement.CallCount);
    }

    [Fact]
    public async Task RequesterCraftedForeignJobUrlUsesAssignedServiceUnitScope()
    {
        var assignedServiceUnitId = Guid.NewGuid();
        var session = new AccessSessionSnapshot(
            Guid.NewGuid(),
            "Requester A",
            "requester-a@example.cz",
            AccessUserStatus.Active,
            [AccessRole.Requester]);
        var jobQueries = new RecordingJobQueries();
        var serviceUnitQueries = new FixedRequesterServiceUnitQueries(
            session.UserId,
            assignedServiceUnitId);
        using var configuredFactory = _factory.WithWebHostBuilder(
            builder => builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IAccessSessionQueries>();
                services.AddSingleton<IAccessSessionQueries>(
                    new FixedSessionQueries(session));
                services.RemoveAll<IJobQueries>();
                services.AddSingleton<IJobQueries>(jobQueries);
                services.RemoveAll<IServiceUnitQueries>();
                services.AddSingleton<IServiceUnitQueries>(serviceUnitQueries);
            }));
        using var client = CreateAuthenticatedClient(
            configuredFactory,
            session);
        var foreignJobId = Guid.NewGuid();

        using var response = await client.GetAsync(
            $"/Management/Jobs/Details/{foreignJobId}?view=requester");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var lookup = Assert.Single(jobQueries.ManagementLookups);
        Assert.Equal(foreignJobId, lookup.ObjectId);
        Assert.Equal(session.UserId, lookup.Actor.UserId);
        Assert.Equal(
            JobManagementScope.AssignedServiceUnits,
            lookup.Actor.Scope);
        Assert.Equal(
            assignedServiceUnitId,
            Assert.Single(lookup.Actor.ServiceUnitIds));
        Assert.Equal(session.UserId, serviceUnitQueries.RequesterUserId);
    }

    private static AccessSessionSnapshot CustomerSession() =>
        new(
            Guid.NewGuid(),
            "Customer A",
            "customer-a@example.cz",
            AccessUserStatus.Active,
            [AccessRole.Customer]);

    private static HttpClient CreateAuthenticatedClient(
        WebApplicationFactory<Program> factory,
        AccessSessionSnapshot session)
    {
        var cookieOptions = factory.Services
            .GetRequiredService<
                IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(CookieAuthenticationDefaults.AuthenticationScheme);
        var ticket = new AuthenticationTicket(
            AccessClaimsPrincipalFactory.Create(
                session,
                CookieAuthenticationDefaults.AuthenticationScheme),
            CookieAuthenticationDefaults.AuthenticationScheme);
        var client = factory.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                BaseAddress = new Uri("https://localhost")
            });
        client.DefaultRequestHeaders.Add(
            "Cookie",
            $"{cookieOptions.Cookie.Name}=" +
            cookieOptions.TicketDataFormat.Protect(ticket));
        return client;
    }

    private sealed record ObjectLookup(Guid CustomerUserId, Guid ObjectId);

    private sealed record ManagementLookup(
        JobManagementActor Actor,
        Guid ObjectId);

    private sealed class FixedSessionQueries : IAccessSessionQueries
    {
        private readonly AccessSessionSnapshot _session;

        public FixedSessionQueries(AccessSessionSnapshot session)
        {
            _session = session;
        }

        public Task<AccessSessionSnapshot?> FindAsync(
            Guid userId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<AccessSessionSnapshot?>(
                userId == _session.UserId ? _session : null);
    }

    private sealed class RecordingPaymentQueries : IPaymentQueries
    {
        public List<ObjectLookup> CustomerLookups { get; } = [];

        public Task<PaymentDetail?> FindForCustomerAsync(
            Guid customerUserId,
            Guid paymentId,
            CancellationToken cancellationToken = default)
        {
            CustomerLookups.Add(new ObjectLookup(customerUserId, paymentId));
            return Task.FromResult<PaymentDetail?>(null);
        }

        public Task<PaymentPage> ListForCustomerAsync(
            Guid customerUserId,
            PaymentListFilter filter,
            PaymentPageRequest page,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<PaymentPage> ListForAdministrationAsync(
            PaymentListFilter filter,
            PaymentPageRequest page,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<PaymentDetail?> FindForAdministrationAsync(
            Guid paymentId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class RecordingJobQueries : IJobQueries
    {
        public List<ObjectLookup> CustomerLookups { get; } = [];

        public List<ManagementLookup> ManagementLookups { get; } = [];

        public Task<JobDetail?> FindForCustomerAsync(
            Guid customerUserId,
            Guid jobId,
            CancellationToken cancellationToken = default)
        {
            CustomerLookups.Add(new ObjectLookup(customerUserId, jobId));
            return Task.FromResult<JobDetail?>(null);
        }

        public Task<JobDetail?> FindForManagementAsync(
            JobManagementActor actor,
            Guid jobId,
            CancellationToken cancellationToken = default)
        {
            ManagementLookups.Add(new ManagementLookup(actor, jobId));
            return Task.FromResult<JobDetail?>(null);
        }

        public Task<CustomerJobSummary> GetCustomerSummaryAsync(
            Guid customerUserId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<JobPage<JobListItem>> ListForCustomerAsync(
            Guid customerUserId,
            JobListFilter filter,
            JobPageRequest page,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ManagementJobSummary> GetManagementSummaryAsync(
            JobManagementActor actor,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<JobPage<JobListItem>> ListForManagementAsync(
            JobManagementActor actor,
            JobListFilter filter,
            JobPageRequest page,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class RecordingFinancialDocumentQueries :
        IFinancialDocumentQueries
    {
        public List<ObjectLookup> CustomerLookups { get; } = [];

        public Task<FinancialDocument?> FindByIdForCustomerAsync(
            Guid documentId,
            Guid customerUserId,
            CancellationToken cancellationToken = default)
        {
            CustomerLookups.Add(new ObjectLookup(customerUserId, documentId));
            return Task.FromResult<FinancialDocument?>(null);
        }

        public Task<IReadOnlyDictionary<Guid, Guid>>
            FindDocumentIdsBySourceForCustomerAsync(
                Guid customerUserId,
                FinancialDocumentSourceType sourceType,
                IEnumerable<Guid> sourceIds,
                CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<FinancialDocument?> FindByIdForAdminAsync(
            Guid documentId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class RecordingSettlementService : IPaymentSettlementService
    {
        public int CallCount { get; private set; }

        public Task<bool> CompleteAsync(
            VerifiedPaymentConfirmation confirmation,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            throw new InvalidOperationException(
                "Foreign-object GET must not settle a payment.");
        }
    }

    private sealed class FixedRequesterServiceUnitQueries :
        IServiceUnitQueries
    {
        private readonly Guid _expectedRequesterUserId;
        private readonly ServiceUnitReadModel _serviceUnit;

        public FixedRequesterServiceUnitQueries(
            Guid expectedRequesterUserId,
            Guid serviceUnitId)
        {
            _expectedRequesterUserId = expectedRequesterUserId;
            _serviceUnit = new ServiceUnitReadModel(
                serviceUnitId,
                "REQTEST",
                "Requester test unit",
                ServiceType.ThreeDPrint);
        }

        public Guid RequesterUserId { get; private set; }

        public Task<IReadOnlyList<ServiceUnitReadModel>> ListForRequesterAsync(
            Guid userId,
            CancellationToken cancellationToken = default)
        {
            Assert.Equal(_expectedRequesterUserId, userId);
            RequesterUserId = userId;
            return Task.FromResult<IReadOnlyList<ServiceUnitReadModel>>(
                [_serviceUnit]);
        }

        public Task<IReadOnlyList<ServiceUnitAdministrationListItem>>
            ListAllAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<RequesterServiceUnitAssignmentReadModel>>
            ListAssignmentsForUserAsync(
                Guid userId,
                bool includeRevoked = false,
                CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ServiceUnitReadModel?> FindActiveAsync(
            Guid serviceUnitId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<ServiceUnitReadModel>> ListActiveAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
