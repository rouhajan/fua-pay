using System.Net;

using FuaPay.Web.BuildingBlocks.Domain;
using FuaPay.Web.Modules.Access.Application;
using FuaPay.Web.Modules.Access.Domain;
using FuaPay.Web.Modules.Access.Web;
using FuaPay.Web.Modules.Credits.Application;
using FuaPay.Web.Modules.Credits.Domain;
using FuaPay.Web.Modules.FinancialDocuments.Application;
using FuaPay.Web.Modules.FinancialDocuments.Domain;
using FuaPay.Web.Modules.Jobs.Application;
using FuaPay.Web.Modules.Jobs.Domain;
using FuaPay.Web.Modules.Payments.Application;
using FuaPay.Web.Modules.Payments.Domain;
using FuaPay.Web.Modules.ServiceUnits.Application;
using FuaPay.Web.Pages;
using FuaPay.Web.Tests.Testing;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace FuaPay.Web.Tests.Pages;

public sealed class CustomerCardPaymentAvailabilityRenderingTests :
    IClassFixture<ConfiguredWebApplicationFactory>
{
    private static readonly Guid CustomerUserId =
        Guid.Parse("d36eeb89-62dd-43a6-8bf5-482f667ad063");

    private static readonly Guid JobId =
        Guid.Parse("4af68f3e-4393-41d1-970b-bba82485359d");

    private static readonly Guid PaymentId =
        Guid.Parse("a2f52ef3-aa89-419b-91fd-876a1b23409c");

    private static readonly Guid DocumentId =
        Guid.Parse("e319c11a-68fa-4be7-84d4-97ee066eb156");

    private static readonly DateTimeOffset TestTime =
        new(2026, 9, 19, 10, 0, 0, TimeSpan.Zero);

    private static readonly Uri ProcessUri =
        new("https://gateway.example/pay/process");

    private readonly ConfiguredWebApplicationFactory _factory;

    public CustomerCardPaymentAvailabilityRenderingTests(
        ConfiguredWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task CreateTopUp_RendersCardPresentationAndAmountGuidance()
    {
        var html = WebUtility.HtmlDecode(await RenderAsync(
            "/Customer/Payments/CreateTopUp?view=customer",
            paymentCreationEnabled: true,
            new RenderingQueries()));

        Assert.Contains(
            "Minimální dobití:",
            html,
            StringComparison.Ordinal);
        Assert.Contains("10 Kč", html, StringComparison.Ordinal);
        Assert.Contains(
            "Doporučené maximum:",
            html,
            StringComparison.Ordinal);
        Assert.Contains("10 000 Kč", html, StringComparison.Ordinal);
        Assert.Contains("Platba kartou", html, StringComparison.Ordinal);
        Assert.Contains(
            "card-acceptance-marks__logo--visa",
            html,
            StringComparison.Ordinal);
        Assert.Contains(
            "card-acceptance-marks__logo--mastercard",
            html,
            StringComparison.Ordinal);
        Assert.Contains(
            "alt=\"Visa\"",
            html,
            StringComparison.Ordinal);
        Assert.Contains(
            "alt=\"Mastercard\"",
            html,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task JobDetails_WithCards_RendersCardAcceptanceMarks()
    {
        var job = PublishedUnpaidJob(priceMinorUnits: 70_000);
        var html = WebUtility.HtmlDecode(await RenderAsync(
            $"/Customer/Jobs/Details/{job.Id:D}?view=customer",
            paymentCreationEnabled: true,
            new RenderingQueries(
                job: job,
                creditBalanceMinorUnits: 100_000)));

        Assert.Contains("Zaplatit přímo", html, StringComparison.Ordinal);
        Assert.Contains(
            "card-acceptance-marks__logo--visa",
            html,
            StringComparison.Ordinal);
        Assert.Contains(
            "card-acceptance-marks__logo--mastercard",
            html,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PaymentsIndex_RendersHistoryAndAvailabilityControlledTopUp(
        bool paymentCreationEnabled)
    {
        var payment = PaymentListItem();
        var html = await RenderAsync(
            "/Customer/Payments?view=customer",
            paymentCreationEnabled,
            new RenderingQueries(paymentListItem: payment));

        Assert.Contains(
            payment.ProviderReference!,
            html,
            StringComparison.Ordinal);
        Assert.Contains("Detail", html, StringComparison.Ordinal);
        Assert.Equal(
            paymentCreationEnabled,
            html.Contains("Dobít kredit", StringComparison.Ordinal));
    }

    [Fact]
    public async Task JobDetails_SufficientCreditWithoutCards_RendersOnlyCreditPayment()
    {
        var job = PublishedUnpaidJob(priceMinorUnits: 70_000);
        var html = WebUtility.HtmlDecode(await RenderAsync(
            $"/Customer/Jobs/Details/{job.Id:D}?view=customer",
            paymentCreationEnabled: false,
            new RenderingQueries(
                job: job,
                creditBalanceMinorUnits: 100_000)));

        Assert.Contains("Zaplatit kreditem", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Zaplatit přímo", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Dobít kredit", html, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Vyberte kredit nebo přímou platbu",
            html,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "card-acceptance-marks",
            html,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task JobDetails_InsufficientCreditWithoutCards_KeepsDeficitWithoutCardActions()
    {
        const long priceMinorUnits = 70_000;
        const long availableCreditMinorUnits = 20_000;
        var job = PublishedUnpaidJob(priceMinorUnits);
        var html = WebUtility.HtmlDecode(await RenderAsync(
            $"/Customer/Jobs/Details/{job.Id:D}?view=customer",
            paymentCreationEnabled: false,
            new RenderingQueries(
                job: job,
                creditBalanceMinorUnits: availableCreditMinorUnits)));

        Assert.Contains("Cena zakázky", html, StringComparison.Ordinal);
        Assert.Contains("Váš kredit", html, StringComparison.Ordinal);
        Assert.Contains(
            DashboardDisplay.FormatMoney(priceMinorUnits),
            html,
            StringComparison.Ordinal);
        Assert.Contains(
            DashboardDisplay.FormatMoney(availableCreditMinorUnits),
            html,
            StringComparison.Ordinal);
        Assert.Contains(
            DashboardDisplay.FormatMoney(
                priceMinorUnits - availableCreditMinorUnits),
            html,
            StringComparison.Ordinal);
        Assert.Contains(
            "Pro úhradu kreditem chybí",
            html,
            StringComparison.Ordinal);
        Assert.DoesNotContain("Dobít kredit", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Zaplatit přímo", html, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Vyberte kredit nebo přímou platbu",
            html,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(PaymentStatus.Pending, PaymentPurposeType.CreditTopUp)]
    [InlineData(PaymentStatus.Succeeded, PaymentPurposeType.CreditTopUp)]
    [InlineData(PaymentStatus.Failed, PaymentPurposeType.CreditTopUp)]
    [InlineData(PaymentStatus.Failed, PaymentPurposeType.Job)]
    public async Task PaymentDetails_DisabledCardsRenderHistoryWithoutCardActions(
        PaymentStatus status,
        PaymentPurposeType purposeType)
    {
        var payment = PaymentDetail(status, purposeType);
        var job = purposeType == PaymentPurposeType.Job
            ? PublishedUnpaidJob(priceMinorUnits: payment.AmountMinorUnits)
            : null;
        var html = WebUtility.HtmlDecode(await RenderAsync(
            $"/Customer/Payments/Details/{payment.Id:D}?view=customer",
            paymentCreationEnabled: false,
            new RenderingQueries(
                paymentDetail: payment,
                job: job,
                financialDocumentId:
                    status == PaymentStatus.Succeeded
                        ? DocumentId
                        : null)));

        Assert.Contains(
            payment.ProviderReference!,
            html,
            StringComparison.Ordinal);
        Assert.Contains(
            PaymentDisplay.StatusLabel(payment.Status),
            html,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Pokračovat na zabezpečenou platební bránu",
            html,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Zkusit přímou platbu znovu",
            html,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Nové dobití kreditu",
            html,
            StringComparison.Ordinal);

        if (status == PaymentStatus.Pending)
        {
            Assert.Contains(
                "Obnovit stav ručně",
                html,
                StringComparison.Ordinal);
        }

        if (status == PaymentStatus.Succeeded)
        {
            Assert.Contains("Doklad o úhradě", html, StringComparison.Ordinal);
            Assert.Contains(
                DocumentId.ToString(),
                html,
                StringComparison.OrdinalIgnoreCase);
        }
    }

    private async Task<string> RenderAsync(
        string path,
        bool paymentCreationEnabled,
        RenderingQueries queries)
    {
        var session = new AccessSessionSnapshot(
            CustomerUserId,
            "Test customer",
            "customer@example.test",
            AccessUserStatus.Active,
            [AccessRole.Customer]);
        using var configuredFactory = _factory.WithWebHostBuilder(
            builder => builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<PaymentCreationAvailability>();
                services.AddSingleton(
                    new PaymentCreationAvailability(
                        paymentCreationEnabled));
                services.RemoveAll<IAccessSessionQueries>();
                services.AddSingleton<IAccessSessionQueries>(
                    new FixedAccessSessionQueries(session));
                services.RemoveAll<IAccessUserQueries>();
                services.AddSingleton<IAccessUserQueries>(queries);
                services.RemoveAll<IServiceUnitQueries>();
                services.AddSingleton<IServiceUnitQueries>(queries);
                services.RemoveAll<IPaymentQueries>();
                services.AddSingleton<IPaymentQueries>(queries);
                services.RemoveAll<IJobQueries>();
                services.AddSingleton<IJobQueries>(queries);
                services.RemoveAll<ICreditQueries>();
                services.AddSingleton<ICreditQueries>(queries);
                services.RemoveAll<IFinancialDocumentQueries>();
                services.AddSingleton<IFinancialDocumentQueries>(queries);
                services.RemoveAll<ICreditAvailabilityRepository>();
                services.AddSingleton<ICreditAvailabilityRepository>(
                    new NoBlockingCreditAvailabilityRepository());
                services.RemoveAll<IPaymentProviderInitiator>();
                services.AddSingleton<IPaymentProviderInitiator>(
                    new ExternalProcessUriProvider());
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

        using var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }

    private static PaymentListItem PaymentListItem() =>
        new(
            PaymentId,
            CustomerUserId,
            PaymentPurposeType.CreditTopUp,
            JobId: null,
            AmountMinorUnits: 50_000,
            PaymentProvider.Csob,
            PaymentStatus.Succeeded,
            ProviderReference: "history-pay-reference",
            FailureReason: null,
            TestTime.AddMinutes(-5),
            TestTime,
            TestTime);

    private static PaymentDetail PaymentDetail(
        PaymentStatus status,
        PaymentPurposeType purposeType) =>
        new(
            PaymentId,
            CustomerUserId,
            purposeType,
            purposeType == PaymentPurposeType.Job ? JobId : null,
            AmountMinorUnits: 70_000,
            PaymentProvider.Csob,
            status,
            ProviderReference: "historical-pay-reference",
            FailureReason:
                status == PaymentStatus.Failed
                    ? "Platba nebyla potvrzena."
                    : null,
            TestTime.AddMinutes(-5),
            TestTime,
            CompletedAt:
                status == PaymentStatus.Pending ? null : TestTime,
            ProcessUri.AbsoluteUri,
            Version: 1);

    private static JobDetail PublishedUnpaidJob(long priceMinorUnits) =>
        new(
            JobId,
            "3D-2026-000042",
            Guid.NewGuid(),
            CustomerUserId,
            CustomerUserId,
            ServiceType.ThreeDPrint,
            "Rendering test job",
            "Customer payment rendering test",
            priceMinorUnits,
            JobProductionStatus.Published,
            JobPaymentStatus.Unpaid,
            SettlementType: null,
            SettlementReferenceId: null,
            TestTime.AddDays(-1),
            TestTime.AddHours(-1),
            SettledAt: null,
            ProductionStartedAt: null,
            ReadyForPickupAt: null,
            CompletedAt: null,
            CancelledAt: null,
            Version: 1);

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
            Task.FromResult<AccessSessionSnapshot?>(
                userId == _session.UserId ? _session : null);
    }

    private sealed class NoBlockingCreditAvailabilityRepository :
        ICreditAvailabilityRepository
    {
        public Task<Money> GetTotalBlockingAmountAsync(
            Guid creditAccountId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Money.Zero);
    }

    private sealed class ExternalProcessUriProvider :
        IPaymentProviderInitiator
    {
        public PaymentProvider Provider => PaymentProvider.Csob;

        public void EnsureAvailable() =>
            throw new NotSupportedException();

        public Task<PaymentProviderInitializationResult> InitializeAsync(
            PaymentProviderInitializationRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task VerifyAsync(
            PaymentProviderInitializationResult candidate,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Uri? ResolveTrustedProcessUri(
            PaymentProvider provider,
            string? providerReference,
            string? processUri) =>
            ProcessUri;
    }

    private sealed class RenderingQueries :
        IPaymentQueries,
        IJobQueries,
        ICreditQueries,
        IFinancialDocumentQueries,
        IAccessUserQueries,
        IServiceUnitQueries
    {
        private readonly PaymentListItem? _paymentListItem;
        private readonly PaymentDetail? _paymentDetail;
        private readonly JobDetail? _job;
        private readonly long _creditBalanceMinorUnits;
        private readonly Guid? _financialDocumentId;

        public RenderingQueries(
            PaymentListItem? paymentListItem = null,
            PaymentDetail? paymentDetail = null,
            JobDetail? job = null,
            long creditBalanceMinorUnits = 0,
            Guid? financialDocumentId = null)
        {
            _paymentListItem = paymentListItem;
            _paymentDetail = paymentDetail;
            _job = job;
            _creditBalanceMinorUnits = creditBalanceMinorUnits;
            _financialDocumentId = financialDocumentId;
        }

        public Task<PaymentPage> ListForCustomerAsync(
            Guid customerUserId,
            PaymentListFilter filter,
            PaymentPageRequest page,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new PaymentPage(
                _paymentListItem is not null &&
                _paymentListItem.CustomerUserId == customerUserId
                    ? [_paymentListItem]
                    : [],
                page.Offset,
                page.Limit,
                _paymentListItem is not null ? 1 : 0));

        Task<PaymentDetail?> IPaymentQueries.FindForCustomerAsync(
            Guid customerUserId,
            Guid paymentId,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                _paymentDetail?.CustomerUserId == customerUserId &&
                _paymentDetail.Id == paymentId
                    ? _paymentDetail
                    : null);

        Task<JobDetail?> IJobQueries.FindForCustomerAsync(
            Guid customerUserId,
            Guid jobId,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                _job?.CustomerUserId == customerUserId &&
                _job.Id == jobId
                    ? _job
                    : null);

        public Task<CreditAccountSummary?> FindAccountForOwnerAsync(
            Guid ownerId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<CreditAccountSummary?>(
                ownerId == CustomerUserId
                    ? new CreditAccountSummary(
                        Guid.NewGuid(),
                        ownerId,
                        _creditBalanceMinorUnits,
                        Version: 1)
                    : null);

        public Task<IReadOnlyDictionary<Guid, Guid>>
            FindDocumentIdsBySourceForCustomerAsync(
                Guid customerUserId,
                FinancialDocumentSourceType sourceType,
                IEnumerable<Guid> sourceIds,
                CancellationToken cancellationToken = default)
        {
            IReadOnlyDictionary<Guid, Guid> result =
                customerUserId == CustomerUserId &&
                sourceType == FinancialDocumentSourceType.Payment &&
                _paymentDetail is not null &&
                _financialDocumentId.HasValue &&
                sourceIds.Contains(_paymentDetail.Id)
                    ? new Dictionary<Guid, Guid>
                    {
                        [_paymentDetail.Id] = _financialDocumentId.Value
                    }
                    : new Dictionary<Guid, Guid>();
            return Task.FromResult(result);
        }

        public Task<IReadOnlyDictionary<Guid, AccessUserOption>>
            FindOptionsAsync(
                IEnumerable<Guid> userIds,
                CancellationToken cancellationToken = default) =>
            Task.FromResult<
                IReadOnlyDictionary<Guid, AccessUserOption>>(
                new Dictionary<Guid, AccessUserOption>());

        public Task<IReadOnlyList<ServiceUnitAdministrationListItem>>
            ListAllAsync(
                CancellationToken cancellationToken = default) =>
            Task.FromResult<
                IReadOnlyList<ServiceUnitAdministrationListItem>>([]);

        public Task<PaymentPage> ListForAdministrationAsync(
            PaymentListFilter filter,
            PaymentPageRequest page,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<PaymentDetail?> FindForAdministrationAsync(
            Guid paymentId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

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

        public Task<JobDetail?> FindForManagementAsync(
            JobManagementActor actor,
            Guid jobId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<JobPage<JobListItem>> ListForManagementAsync(
            JobManagementActor actor,
            JobListFilter filter,
            JobPageRequest page,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<CreditAdministrationMovementPage>
            ListAdministrationMovementsAsync(
                CreditAdministrationMovementFilter filter,
                CreditMovementPageRequest page,
                CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<CreditMovementListItem?> FindMovementForOwnerAsync(
            Guid ownerId,
            Guid operationId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<CreditMovementPage> ListMovementsForOwnerAsync(
            Guid ownerId,
            CreditMovementPageRequest page,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<FinancialDocument?> FindByIdForCustomerAsync(
            Guid documentId,
            Guid customerUserId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<FinancialDocument?> FindByIdForAdminAsync(
            Guid documentId,
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

        public Task<IReadOnlyList<AccessUserOption>>
            ListActiveCustomersAsync(
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

        public Task<IReadOnlyList<ServiceUnitReadModel>>
            ListForRequesterAsync(
                Guid userId,
                CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
