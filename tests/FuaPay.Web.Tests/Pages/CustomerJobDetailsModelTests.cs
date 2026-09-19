using System.Runtime.CompilerServices;
using System.Security.Claims;

using FuaPay.Web.BuildingBlocks.Application;
using FuaPay.Web.BuildingBlocks.Auditing;
using FuaPay.Web.BuildingBlocks.Domain;
using FuaPay.Web.BuildingBlocks.Notifications;
using FuaPay.Web.Modules.Access.Application;
using FuaPay.Web.Modules.Access.Domain;
using FuaPay.Web.Modules.Credits.Application;
using FuaPay.Web.Modules.FinancialDocuments.Application;
using FuaPay.Web.Modules.FinancialDocuments.Domain;
using FuaPay.Web.Modules.Jobs.Application;
using FuaPay.Web.Modules.Jobs.Domain;
using FuaPay.Web.Modules.Jobs.Web;
using FuaPay.Web.Modules.Payments.Application;
using FuaPay.Web.Modules.Receipts.Application;
using FuaPay.Web.Modules.ServiceUnits.Application;
using FuaPay.Web.Pages.Customer.Jobs;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace FuaPay.Web.Tests.Pages;

public sealed class CustomerJobDetailsModelTests
{
    [Fact]
    public async Task OnGetAsync_UsesAvailableCreditInsteadOfLedgerBalance()
    {
        var customerUserId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var serviceUnitId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        var job = new JobDetail(
            Id: jobId,
            Number: "3D-2026-000001",
            ServiceUnitId: serviceUnitId,
            CustomerUserId: customerUserId,
            CreatedByUserId: customerUserId,
            ServiceType: ServiceType.ThreeDPrint,
            Title: "Testovací zakázka",
            Description: "Test dostupného kreditu",
            PriceMinorUnits: 700,
            ProductionStatus: JobProductionStatus.Published,
            PaymentStatus: JobPaymentStatus.Unpaid,
            SettlementType: null,
            SettlementReferenceId: null,
            CreatedAt: now.AddMinutes(-1),
            PublishedAt: now,
            SettledAt: null,
            ProductionStartedAt: null,
            ReadyForPickupAt: null,
            CompletedAt: null,
            CancelledAt: null,
            Version: 1);

        var account = new CreditAccountSummary(
            accountId,
            customerUserId,
            BalanceMinorUnits: 1_000,
            Version: 1);

        var availabilityService = new CreditAvailabilityService(
            new StubCreditAvailabilityRepository(
                new Money(450)));

        var model = new DetailsModel(
            new StubJobQueries(job),
            new StubFinancialDocumentQueries(),
            new StubCreditQueries(account),
            availabilityService,
            UnusedDependency<CreditJobPaymentService>(),
            new JobPresentationComposer(
                new EmptyAccessUserQueries(),
                new EmptyServiceUnitQueries()),
            UnusedDependency<PaymentCreationService>(),
            new PaymentCreationAvailability(true),
            DisabledReceiptConfiguration())
        {
            PageContext = CreatePageContext(customerUserId)
        };

        var result = await model.OnGetAsync(jobId);

        Assert.IsType<PageResult>(result);

        var options = Assert.IsType<CustomerJobPaymentOptions>(
            model.PaymentOptions);

        Assert.Equal(550, options.CreditBalanceMinorUnits);
        Assert.False(options.HasSufficientCredit);
        Assert.Equal(150, options.MissingCreditMinorUnits);
    }

    [Theory]
    [InlineData(1_000, false, true)]
    [InlineData(100, false, false)]
    [InlineData(1_000, true, true)]
    [InlineData(100, true, false)]
    public async Task OnGetAsync_CardAvailabilityDoesNotChangeCreditPaymentOptions(
        long availableCreditMinorUnits,
        bool cardPaymentsEnabled,
        bool hasSufficientCredit)
    {
        var customerUserId = Guid.NewGuid();
        var job = CreatePublishedUnpaidJob(customerUserId);
        var account = new CreditAccountSummary(
            Guid.NewGuid(),
            customerUserId,
            availableCreditMinorUnits,
            Version: 1);
        var model = new DetailsModel(
            new StubJobQueries(job),
            new StubFinancialDocumentQueries(),
            new StubCreditQueries(account),
            new CreditAvailabilityService(
                new StubCreditAvailabilityRepository(Money.Zero)),
            UnusedDependency<CreditJobPaymentService>(),
            new JobPresentationComposer(
                new EmptyAccessUserQueries(),
                new EmptyServiceUnitQueries()),
            UnusedDependency<PaymentCreationService>(),
            new PaymentCreationAvailability(cardPaymentsEnabled),
            DisabledReceiptConfiguration())
        {
            PageContext = CreatePageContext(customerUserId)
        };

        var result = await model.OnGetAsync(job.Id);

        Assert.IsType<PageResult>(result);
        Assert.Equal(cardPaymentsEnabled, model.CanCreateCardPayment);
        Assert.Equal(
            hasSufficientCredit,
            Assert.IsType<CustomerJobPaymentOptions>(
                model.PaymentOptions).HasSufficientCredit);
    }

    [Fact]
    public async Task CreateDirectPaymentPost_DisabledReturnsNotFoundBeforeCreationService()
    {
        var customerUserId = Guid.NewGuid();
        var job = CreatePublishedUnpaidJob(customerUserId);
        var model = new DetailsModel(
            new StubJobQueries(job),
            new StubFinancialDocumentQueries(),
            new StubCreditQueries(new CreditAccountSummary(
                Guid.NewGuid(),
                customerUserId,
                BalanceMinorUnits: 1_000,
                Version: 1)),
            new CreditAvailabilityService(
                new StubCreditAvailabilityRepository(Money.Zero)),
            UnusedDependency<CreditJobPaymentService>(),
            new JobPresentationComposer(
                new EmptyAccessUserQueries(),
                new EmptyServiceUnitQueries()),
            UnusedDependency<PaymentCreationService>(),
            new PaymentCreationAvailability(false),
            DisabledReceiptConfiguration())
        {
            PageContext = CreatePageContext(customerUserId)
        };

        var result = await model.OnPostCreateDirectPaymentAsync(job.Id);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task PayCreditPost_RemainsAvailableWhenCardPaymentsAreDisabled()
    {
        var customerUserId = Guid.NewGuid();
        var job = CreatePublishedUnpaidJob(customerUserId);
        var pageContext = CreatePageContext(customerUserId);
        var creditPaymentService = new CreditJobPaymentService(
            new UnusedJobRepository(),
            new UnusedJobPaymentCoordination(),
            UnusedDependency<CreditService>(),
            new SuccessfulCreditPaymentTransaction(),
            NullAuditTrail.Instance,
            NullNotificationOutbox.Instance);
        var model = new DetailsModel(
            new StubJobQueries(job),
            new StubFinancialDocumentQueries(),
            new StubCreditQueries(new CreditAccountSummary(
                Guid.NewGuid(),
                customerUserId,
                BalanceMinorUnits: 1_000,
                Version: 1)),
            new CreditAvailabilityService(
                new StubCreditAvailabilityRepository(Money.Zero)),
            creditPaymentService,
            new JobPresentationComposer(
                new EmptyAccessUserQueries(),
                new EmptyServiceUnitQueries()),
            UnusedDependency<PaymentCreationService>(),
            new PaymentCreationAvailability(false),
            DisabledReceiptConfiguration())
        {
            PageContext = pageContext,
            TempData = new TempDataDictionary(
                pageContext.HttpContext,
                new MemoryTempDataProvider())
        };

        var result = await model.OnPostPayCreditAsync(job.Id);

        Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal(
            "Zakázka byla uhrazena kreditem.",
            model.TempData["StatusMessage"]);
    }

    [Fact]
    public void DetailsPage_GatesOnlyCardActionsOnPaymentCreationAvailability()
    {
        var pageSource = File.ReadAllText(
            Path.Combine(
                FindRepositoryRoot(),
                "src",
                "FuaPay.Web",
                "Pages",
                "Customer",
                "Jobs",
                "Details.cshtml"));
        var modelSource = File.ReadAllText(
            Path.Combine(
                FindRepositoryRoot(),
                "src",
                "FuaPay.Web",
                "Pages",
                "Customer",
                "Jobs",
                "Details.cshtml.cs"));

        Assert.Contains(
            "else if (Model.CanCreateCardPayment)",
            pageSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "@if (Model.CanCreateCardPayment)",
            pageSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "asp-page-handler=\"PayCredit\"",
            pageSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "asp-page-handler=\"CreateDirectPayment\"",
            pageSource,
            StringComparison.Ordinal);

        var payCreditStart = modelSource.IndexOf(
            "OnPostPayCreditAsync",
            StringComparison.Ordinal);
        var loadStart = modelSource.IndexOf(
            "private async Task<bool> LoadAsync",
            StringComparison.Ordinal);
        Assert.True(payCreditStart >= 0);
        Assert.True(loadStart > payCreditStart);
        Assert.DoesNotContain(
            "PaymentCreationAvailability",
            modelSource[payCreditStart..loadStart],
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "CanCreateCardPayment",
            modelSource[payCreditStart..loadStart],
            StringComparison.Ordinal);
    }


    [Fact]
    public async Task OnGetAsync_DirectPaymentWithCanonicalDocumentPrefersFinancialDocument()
    {
        var customerUserId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var job = CreatePaidDirectJob(
            customerUserId,
            jobId,
            paymentId,
            now);
        var model = CreatePaidDirectModel(
            job,
            new StubFinancialDocumentQueries(
                customerUserId,
                paymentId,
                documentId),
            receiptsEnabled: true);

        var result = await model.OnGetAsync(jobId);

        Assert.IsType<PageResult>(result);
        Assert.Equal(documentId, model.FinancialDocumentId);
        Assert.False(model.CanDownloadReceipt);
    }

    [Fact]
    public async Task OnGetAsync_HistoricalDirectPaymentWithoutDocumentKeepsLegacyReceipt()
    {
        var customerUserId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var job = CreatePaidDirectJob(
            customerUserId,
            jobId,
            paymentId,
            now);
        var model = CreatePaidDirectModel(
            job,
            new StubFinancialDocumentQueries(),
            receiptsEnabled: true);

        var result = await model.OnGetAsync(jobId);

        Assert.IsType<PageResult>(result);
        Assert.Null(model.FinancialDocumentId);
        Assert.True(model.CanDownloadReceipt);
    }

    [Fact]
    public void DetailsPage_PrefersCanonicalDocumentBeforeLegacyReceipt()
    {
        var source = File.ReadAllText(
            Path.Combine(
                FindRepositoryRoot(),
                "src",
                "FuaPay.Web",
                "Pages",
                "Customer",
                "Jobs",
                "Details.cshtml"));

        var canonical = source.IndexOf(
            "Model.FinancialDocumentId.HasValue",
            StringComparison.Ordinal);
        var legacy = source.IndexOf(
            "else if (Model.CanDownloadReceipt)",
            StringComparison.Ordinal);

        Assert.True(canonical >= 0);
        Assert.True(legacy > canonical);
        Assert.Contains(
            "asp-page=\"/Customer/FinancialDocuments/Download\"",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "asp-page=\"./Receipt\"",
            source,
            StringComparison.Ordinal);
    }

    private static PageContext CreatePageContext(Guid customerUserId)
    {
        var principal = new ClaimsPrincipal(
            new ClaimsIdentity(
                [
                    new Claim(
                        ClaimTypes.NameIdentifier,
                        customerUserId.ToString()),
                    new Claim(
                        ClaimTypes.Role,
                        AccessRole.Customer.ToString())
                ],
                authenticationType: "test"));

        return new PageContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = principal
            }
        };
    }


    private static DetailsModel CreatePaidDirectModel(
        JobDetail job,
        IFinancialDocumentQueries financialDocumentQueries,
        bool receiptsEnabled)
    {
        var account = new CreditAccountSummary(
            Guid.NewGuid(),
            job.CustomerUserId,
            BalanceMinorUnits: 0,
            Version: 1);

        return new DetailsModel(
            new StubJobQueries(job),
            financialDocumentQueries,
            new StubCreditQueries(account),
            new CreditAvailabilityService(
                new StubCreditAvailabilityRepository(new Money(0))),
            UnusedDependency<CreditJobPaymentService>(),
            new JobPresentationComposer(
                new EmptyAccessUserQueries(),
                new EmptyServiceUnitQueries()),
            UnusedDependency<PaymentCreationService>(),
            new PaymentCreationAvailability(true),
            ReceiptConfiguration(receiptsEnabled))
        {
            PageContext = CreatePageContext(job.CustomerUserId)
        };
    }

    private static JobDetail CreatePaidDirectJob(
        Guid customerUserId,
        Guid jobId,
        Guid paymentId,
        DateTimeOffset now) =>
        new(
            Id: jobId,
            Number: "3D-2026-000002",
            ServiceUnitId: Guid.NewGuid(),
            CustomerUserId: customerUserId,
            CreatedByUserId: customerUserId,
            ServiceType: ServiceType.ThreeDPrint,
            Title: "Uhrazená zakázka",
            Description: "Test precedence dokladu",
            PriceMinorUnits: 1_000,
            ProductionStatus: JobProductionStatus.Published,
            PaymentStatus: JobPaymentStatus.Paid,
            SettlementType: JobSettlementType.DirectPayment,
            SettlementReferenceId: paymentId,
            CreatedAt: now.AddHours(-2),
            PublishedAt: now.AddHours(-1),
            SettledAt: now,
            ProductionStartedAt: null,
            ReadyForPickupAt: null,
            CompletedAt: null,
            CancelledAt: null,
            Version: 1);

    private static JobDetail CreatePublishedUnpaidJob(
        Guid customerUserId) =>
        new(
            Id: Guid.NewGuid(),
            Number: "3D-2026-000003",
            ServiceUnitId: Guid.NewGuid(),
            CustomerUserId: customerUserId,
            CreatedByUserId: customerUserId,
            ServiceType: ServiceType.ThreeDPrint,
            Title: "Neuhrazená zakázka",
            Description: "Test dostupnosti plateb",
            PriceMinorUnits: 700,
            ProductionStatus: JobProductionStatus.Published,
            PaymentStatus: JobPaymentStatus.Unpaid,
            SettlementType: null,
            SettlementReferenceId: null,
            CreatedAt: DateTimeOffset.UtcNow.AddHours(-1),
            PublishedAt: DateTimeOffset.UtcNow,
            SettledAt: null,
            ProductionStartedAt: null,
            ReadyForPickupAt: null,
            CompletedAt: null,
            CancelledAt: null,
            Version: 1);

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "FuaPay.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("FuaPay.slnx was not found.");
    }

    private static ReceiptConfiguration DisabledReceiptConfiguration() =>
        ReceiptConfiguration(enabled: false);

    private static ReceiptConfiguration ReceiptConfiguration(bool enabled) =>
        new(
            Enabled: enabled,
            PreviewMode: false,
            Issuer: new ReceiptIssuerConfiguration(
                LegalName: string.Empty,
                UnitName: string.Empty,
                AddressLine1: string.Empty,
                AddressLine2: string.Empty,
                Country: string.Empty,
                RegistrationNumber: string.Empty,
                VatNumber: string.Empty,
                ContactEmail: string.Empty),
            VatRatePercent: 21,
            LogoPath: string.Empty,
            RegularFontPath: null,
            BoldFontPath: null);

    private static T UnusedDependency<T>()
        where T : class =>
        (T)RuntimeHelpers.GetUninitializedObject(typeof(T));


    private sealed class StubFinancialDocumentQueries :
        IFinancialDocumentQueries
    {
        private readonly Guid? _customerUserId;
        private readonly Guid? _sourceId;
        private readonly Guid? _documentId;

        public StubFinancialDocumentQueries()
        {
        }

        public StubFinancialDocumentQueries(
            Guid customerUserId,
            Guid sourceId,
            Guid documentId)
        {
            _customerUserId = customerUserId;
            _sourceId = sourceId;
            _documentId = documentId;
        }

        public Task<IReadOnlyDictionary<Guid, Guid>>
            FindDocumentIdsBySourceForCustomerAsync(
                Guid customerUserId,
                FinancialDocumentSourceType sourceType,
                IEnumerable<Guid> sourceIds,
                CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var requested = sourceIds.ToHashSet();

            IReadOnlyDictionary<Guid, Guid> result =
                _customerUserId == customerUserId &&
                sourceType == FinancialDocumentSourceType.Payment &&
                _sourceId.HasValue &&
                _documentId.HasValue &&
                requested.Contains(_sourceId.Value)
                    ? new Dictionary<Guid, Guid>
                    {
                        [_sourceId.Value] = _documentId.Value
                    }
                    : new Dictionary<Guid, Guid>();

            return Task.FromResult(result);
        }

        public Task<FinancialDocument?> FindByIdForCustomerAsync(
            Guid documentId,
            Guid customerUserId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<FinancialDocument?> FindByIdForAdminAsync(
            Guid documentId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class StubJobQueries : IJobQueries
    {
        private readonly JobDetail _job;

        public StubJobQueries(JobDetail job)
        {
            _job = job;
        }

        public Task<JobDetail?> FindForCustomerAsync(
            Guid customerUserId,
            Guid jobId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult<JobDetail?>(
                customerUserId == _job.CustomerUserId &&
                jobId == _job.Id
                    ? _job
                    : null);
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
    }

    private sealed class StubCreditQueries : ICreditQueries
    {
        private readonly CreditAccountSummary _account;

        public StubCreditQueries(CreditAccountSummary account)
        {
            _account = account;
        }

        public Task<CreditAccountSummary?> FindAccountForOwnerAsync(
            Guid ownerId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult<CreditAccountSummary?>(
                ownerId == _account.OwnerId
                    ? _account
                    : null);
        }

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
    }

    private sealed class StubCreditAvailabilityRepository :
        ICreditAvailabilityRepository
    {
        private readonly Money _blocking;

        public StubCreditAvailabilityRepository(Money blocking)
        {
            _blocking = blocking;
        }

        public Task<Money> GetTotalBlockingAmountAsync(
            Guid creditAccountId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_blocking);
        }
    }

    private sealed class EmptyAccessUserQueries : IAccessUserQueries
    {
        public Task<IReadOnlyDictionary<Guid, AccessUserOption>>
            FindOptionsAsync(
                IEnumerable<Guid> userIds,
                CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult<
                IReadOnlyDictionary<Guid, AccessUserOption>>(
                new Dictionary<Guid, AccessUserOption>());
        }

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
    }

    private sealed class EmptyServiceUnitQueries : IServiceUnitQueries
    {
        public Task<IReadOnlyList<ServiceUnitAdministrationListItem>>
            ListAllAsync(
                CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult<
                IReadOnlyList<ServiceUnitAdministrationListItem>>([]);
        }

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

    private sealed class SuccessfulCreditPaymentTransaction :
        IApplicationTransaction
    {
        public Task<T> ExecuteAsync<T>(
            Func<CancellationToken, Task<T>> operation,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Equal(typeof(bool), typeof(T));
            return Task.FromResult((T)(object)true);
        }
    }

    private sealed class UnusedJobRepository : IJobRepository
    {
        public Task<Job?> FindByIdAsync(
            Guid jobId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task AddAsync(
            Job job,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task SaveAsync(
            Job job,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class UnusedJobPaymentCoordination :
        IJobPaymentCoordination
    {
        public Task<bool> LockJobAsync(
            Guid jobId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> HasBlockingDirectPaymentAsync(
            Guid jobId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class MemoryTempDataProvider : ITempDataProvider
    {
        private Dictionary<string, object> _values = [];

        public IDictionary<string, object> LoadTempData(
            HttpContext context) =>
            new Dictionary<string, object>(_values);

        public void SaveTempData(
            HttpContext context,
            IDictionary<string, object> values)
        {
            _values = new Dictionary<string, object>(values);
        }
    }
}
