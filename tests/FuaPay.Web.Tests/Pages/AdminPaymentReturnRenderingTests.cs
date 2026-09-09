using System.Net;

using FuaPay.Web.Modules.Access.Application;
using FuaPay.Web.Modules.Access.Domain;
using FuaPay.Web.Modules.Access.Web;
using FuaPay.Web.Modules.Payments.Application;
using FuaPay.Web.Modules.Payments.Domain;
using FuaPay.Web.Tests.Testing;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace FuaPay.Web.Tests.Pages;

public sealed class AdminPaymentReturnRenderingTests :
    IClassFixture<ConfiguredWebApplicationFactory>
{
    private static readonly Guid PaymentId =
        Guid.Parse("071ff40b-e021-4184-851f-432d71065d4f");

    private readonly ConfiguredWebApplicationFactory _factory;

    public AdminPaymentReturnRenderingTests(
        ConfiguredWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Theory]
    [InlineData(
        SettlementReturnState.InProgress,
        SettlementReturnProviderAttemptState.InProgress)]
    [InlineData(
        SettlementReturnState.RequiresAttention,
        SettlementReturnProviderAttemptState.Uncertain)]
    public async Task ReloadedRecoveryReusesPersistedOperationIdentity(
        SettlementReturnState returnState,
        SettlementReturnProviderAttemptState attemptState)
    {
        var operationId = Guid.NewGuid();
        var settlementReturn = ReturnItem(
            operationId,
            returnState,
            attemptState);

        var first = await RenderAsync(settlementReturn);
        var reloaded = await RenderAsync(settlementReturn);

        foreach (var html in new[] { first, reloaded })
        {
            Assert.Contains(
                "data-return-action=\"recover\"",
                html,
                StringComparison.Ordinal);
            Assert.Contains(
                $"data-return-operation-id=\"{operationId}\"",
                html,
                StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(
                "data-return-action=\"new\"",
                html,
                StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task PaymentWithoutReturnOffersInitialReverse()
    {
        var html = await RenderAsync(settlementReturn: null);

        Assert.Contains(
            "data-return-action=\"new\"",
            html,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "data-return-action=\"recover\"",
            html,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(
        SettlementReturnState.Completed,
        SettlementReturnProviderAttemptState.Confirmed,
        "completed")]
    [InlineData(
        SettlementReturnState.RequiresAttention,
        SettlementReturnProviderAttemptState.Rejected,
        "refund-needed")]
    [InlineData(
        SettlementReturnState.Requested,
        SettlementReturnProviderAttemptState.Prepared,
        "attention")]
    public async Task ExistingTerminalOrInconsistentReturnOffersNoNewReverse(
        SettlementReturnState returnState,
        SettlementReturnProviderAttemptState attemptState,
        string expectedPresentation)
    {
        var html = await RenderAsync(
            ReturnItem(Guid.NewGuid(), returnState, attemptState));

        Assert.Contains(
            $"data-return-state=\"{expectedPresentation}\"",
            html,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "data-return-action=\"new\"",
            html,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "data-return-action=\"recover\"",
            html,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task MismatchedReverseAttemptIdentityFailsClosed()
    {
        var item = ReturnItem(
            Guid.NewGuid(),
            SettlementReturnState.RequiresAttention,
            SettlementReturnProviderAttemptState.Uncertain) with
        {
            ReverseAttemptId = Guid.NewGuid()
        };

        var html = await RenderAsync(item);

        Assert.Contains(
            "data-return-state=\"attention\"",
            html,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "data-return-action=\"new\"",
            html,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "data-return-action=\"recover\"",
            html,
            StringComparison.Ordinal);
    }

    private async Task<string> RenderAsync(
        SettlementReturnAdministrationItem? settlementReturn)
    {
        var session = new AccessSessionSnapshot(
            Guid.NewGuid(),
            "Test administrator",
            "admin@example.cz",
            AccessUserStatus.Active,
            [AccessRole.Admin]);
        var queries = new AdminPageQueries(settlementReturn);
        using var configuredFactory = _factory.WithWebHostBuilder(
            builder => builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IPaymentQueries>();
                services.AddSingleton<IPaymentQueries>(queries);
                services.RemoveAll<IAccessUserQueries>();
                services.AddSingleton<IAccessUserQueries>(queries);
                services.RemoveAll<IAccessSessionQueries>();
                services.AddSingleton<IAccessSessionQueries>(
                    new FixedAccessSessionQueries(session));
                services.RemoveAll<IPaymentReconciliationQueries>();
                services.AddSingleton<IPaymentReconciliationQueries>(queries);
                services.RemoveAll<ISettlementReturnQueries>();
                services.AddSingleton<ISettlementReturnQueries>(queries);
            }));
        var cookieOptions = configuredFactory.Services
            .GetRequiredService<
                IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = AccessClaimsPrincipalFactory.Create(
            session,
            CookieAuthenticationDefaults.AuthenticationScheme);
        var ticket = new AuthenticationTicket(
            principal,
            CookieAuthenticationDefaults.AuthenticationScheme);

        using var client = configuredFactory.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                BaseAddress = new Uri("https://localhost")
            });
        client.DefaultRequestHeaders.Add(
            "Cookie",
            $"{cookieOptions.Cookie.Name}=" +
            cookieOptions.TicketDataFormat.Protect(ticket));
        using var response = await client.GetAsync(
            "/Admin/Payments?view=admin");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }

    private static SettlementReturnAdministrationItem ReturnItem(
        Guid requestId,
        SettlementReturnState returnState,
        SettlementReturnProviderAttemptState attemptState) =>
        new(
            Guid.NewGuid(),
            requestId,
            SettlementReturnKind.CardJob,
            PaymentId,
            returnState,
            "Approved full return",
            requestId,
            PaymentProvider.Csob,
            attemptState);

    private sealed class AdminPageQueries :
        IPaymentQueries,
        IAccessUserQueries,
        IPaymentReconciliationQueries,
        ISettlementReturnQueries
    {
        private readonly SettlementReturnAdministrationItem? _return;

        public AdminPageQueries(
            SettlementReturnAdministrationItem? settlementReturn)
        {
            _return = settlementReturn;
        }

        public Task<PaymentPage> ListForAdministrationAsync(
            PaymentListFilter filter,
            PaymentPageRequest page,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new PaymentPage(
                [new PaymentListItem(
                    PaymentId,
                    Guid.NewGuid(),
                    PaymentPurposeType.Job,
                    Guid.NewGuid(),
                    12_500,
                    PaymentProvider.Csob,
                    PaymentStatus.Succeeded,
                    "test-pay-id",
                    null,
                    DateTimeOffset.UtcNow,
                    DateTimeOffset.UtcNow,
                    DateTimeOffset.UtcNow)],
                0,
                40,
                1));

        public Task<
            IReadOnlyDictionary<Guid, SettlementReturnAdministrationItem>>
            FindByOriginalPaymentIdsAsync(
                IEnumerable<Guid> originalPaymentIds,
                CancellationToken cancellationToken = default)
        {
            Assert.Contains(PaymentId, originalPaymentIds);
            IReadOnlyDictionary<Guid, SettlementReturnAdministrationItem>
                result = _return is null
                    ? new Dictionary<
                        Guid,
                        SettlementReturnAdministrationItem>()
                    : new Dictionary<
                        Guid,
                        SettlementReturnAdministrationItem>
                    {
                        [PaymentId] = _return
                    };
            return Task.FromResult(result);
        }

        public Task<IReadOnlyDictionary<Guid, AccessUserOption>>
            FindOptionsAsync(
                IEnumerable<Guid> userIds,
                CancellationToken cancellationToken = default) =>
            Task.FromResult<
                IReadOnlyDictionary<Guid, AccessUserOption>>(
                new Dictionary<Guid, AccessUserOption>());

        public Task<IReadOnlyList<PaymentReconciliationAdminItem>>
            ListOpenAsync(
                int limit,
                CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PaymentReconciliationAdminItem>>([]);

        public Task<PaymentPage> ListForCustomerAsync(
            Guid customerUserId,
            PaymentListFilter filter,
            PaymentPageRequest page,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<PaymentDetail?> FindForCustomerAsync(
            Guid customerUserId,
            Guid paymentId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<PaymentDetail?> FindForAdministrationAsync(
            Guid paymentId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AccessUserPage> ListAsync(
            AccessUserListRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AccessUserDetail?> FindDetailAsync(
            Guid userId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<AccessUserOption>> ListActiveCustomersAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> IsActiveAsync(
            Guid userId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> IsActiveCustomerAsync(
            Guid userId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<long> CountActiveUsersWithRoleAsync(
            AccessRole role,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FixedAccessSessionQueries : IAccessSessionQueries
    {
        private readonly AccessSessionSnapshot _session;

        public FixedAccessSessionQueries(AccessSessionSnapshot session)
        {
            _session = session;
        }

        public Task<AccessSessionSnapshot?> FindAsync(
            Guid userId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<AccessSessionSnapshot?>(_session);
    }
}
