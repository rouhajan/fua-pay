using System.Security.Claims;
using System.Text;

using FuaPay.Web.BuildingBlocks.Application;
using FuaPay.Web.BuildingBlocks.Auditing;
using FuaPay.Web.Modules.Access.Application;
using FuaPay.Web.Modules.Access.Domain;
using FuaPay.Web.Modules.Credits.Application;
using FuaPay.Web.Modules.Credits.Domain;
using FuaPay.Web.Modules.FinancialDocuments.Application;
using FuaPay.Web.Modules.FinancialDocuments.Domain;
using FuaPay.Web.Modules.Jobs.Application;
using FuaPay.Web.Modules.Payments.Application;
using FuaPay.Web.Modules.Payments.Domain;
using FuaPay.Web.Pages.Customer.Payments;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.RazorPages;

using CreditIndexModel = FuaPay.Web.Pages.Admin.Credit.IndexModel;
using CustomerCreditIndexModel =
    FuaPay.Web.Pages.Customer.Credit.IndexModel;
using CustomerPaymentsIndexModel =
    FuaPay.Web.Pages.Customer.Payments.IndexModel;

namespace FuaPay.Web.Tests.Pages;

public sealed class FinancialCommandPageTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CustomerCreditIndex_UsesActiveProviderAvailability(
        bool isAvailable)
    {
        var model = new CustomerCreditIndexModel(
            new EmptyCreditQueries(),
            new RecordingFinancialDocumentQueries(),
            new PaymentCreationAvailability(isAvailable));

        Assert.Equal(isAvailable, model.CanCreatePayment);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CustomerPaymentsIndex_PreservesHistoryAndUsesPaymentCreationAvailability(
        bool isAvailable)
    {
        var customerUserId = Guid.NewGuid();
        var payment = new PaymentListItem(
            Guid.NewGuid(),
            customerUserId,
            PaymentPurposeType.CreditTopUp,
            JobId: null,
            AmountMinorUnits: 50_000,
            PaymentProvider.Csob,
            PaymentStatus.Succeeded,
            ProviderReference: "pay1234567890",
            FailureReason: null,
            DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);
        var model = new CustomerPaymentsIndexModel(
            new FixedCustomerPaymentQueries(payment),
            new PaymentCreationAvailability(isAvailable))
        {
            PageContext = CreatePageContext(customerUserId)
        };

        await model.OnGetAsync();

        Assert.Equal(isAvailable, model.CanCreatePayment);
        Assert.Equal(payment, Assert.Single(model.Payments.Items));
    }

    [Fact]
    public void CustomerPaymentsIndex_RendersTopUpActionOnlyWhenCreationIsAvailable()
    {
        var source = ReadPageSource(
            "Customer",
            "Payments",
            "Index.cshtml");

        Assert.Contains(
            "@if (Model.CanCreatePayment)",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "asp-page=\"./CreateTopUp\"",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task CustomerCreditIndex_MapsCurrentPageManualTopUpsInOneOwnedBatch()
    {
        var ownerId = Guid.NewGuid();
        var manualOperationId = Guid.NewGuid();
        var cardWalletOperationId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        var cardWalletDocumentId = Guid.NewGuid();
        var movements = new RecordingCustomerCreditQueries(
            [
                Movement(manualOperationId, "Ruční dobití"),
                Movement(manualOperationId, "Duplicitní zdroj"),
                Movement(cardWalletOperationId, "Karetní dobití"),
                Movement(Guid.Empty, "Bez operace")
            ]);
        var documents = new RecordingFinancialDocumentQueries(
            new Dictionary<
                (FinancialDocumentSourceType SourceType, Guid SourceId),
                Guid>
            {
                [(FinancialDocumentSourceType.ManualCreditTopUp,
                    manualOperationId)] = documentId,
                [(FinancialDocumentSourceType.Payment,
                    cardWalletOperationId)] = cardWalletDocumentId
            });
        var model = new CustomerCreditIndexModel(
            movements,
            documents,
            new PaymentCreationAvailability(true))
        {
            PageContext = CreatePageContext(ownerId)
        };

        await model.OnGetAsync();

        Assert.Equal(ownerId, movements.LastOwnerId);
        Assert.Equal(1, documents.SourceLookupCalls);
        Assert.Equal(ownerId, documents.LastCustomerUserId);
        Assert.Equal(
            FinancialDocumentSourceType.ManualCreditTopUp,
            documents.LastSourceType);
        Assert.Equal(
            [manualOperationId, cardWalletOperationId],
            documents.LastSourceIds);
        Assert.Equal(
            documentId,
            model.FinancialDocumentIdsByOperationId[manualOperationId]);
        Assert.DoesNotContain(
            cardWalletOperationId,
            model.FinancialDocumentIdsByOperationId.Keys);
        Assert.DoesNotContain(
            Guid.Empty,
            model.FinancialDocumentIdsByOperationId.Keys);
    }

    [Fact]
    public async Task CustomerCreditIndex_EmptyPageUsesEmptyManualDocumentBatch()
    {
        var ownerId = Guid.NewGuid();
        var documents = new RecordingFinancialDocumentQueries();
        var model = new CustomerCreditIndexModel(
            new RecordingCustomerCreditQueries([]),
            documents,
            new PaymentCreationAvailability(true))
        {
            PageContext = CreatePageContext(ownerId)
        };

        await model.OnGetAsync();

        Assert.Equal(1, documents.SourceLookupCalls);
        Assert.Equal(ownerId, documents.LastCustomerUserId);
        Assert.Equal(
            FinancialDocumentSourceType.ManualCreditTopUp,
            documents.LastSourceType);
        Assert.Empty(documents.LastSourceIds);
        Assert.Empty(model.FinancialDocumentIdsByOperationId);
    }

    [Fact]
    public void CustomerCreditIndex_RendersMappedDocumentThroughSecureDownload()
    {
        var source = File.ReadAllText(
            Path.Combine(
                FindRepositoryRoot(),
                "src",
                "FuaPay.Web",
                "Pages",
                "Customer",
                "Credit",
                "Index.cshtml"),
            Encoding.UTF8);

        Assert.Contains(
            "Model.FinancialDocumentIdsByOperationId.TryGetValue",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "asp-page=\"/Customer/FinancialDocuments/Download\"",
            source,
            StringComparison.Ordinal);
        Assert.Contains("Doklad o úhradě", source, StringComparison.Ordinal);
        Assert.Contains("class=\"text-link\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TopUpGet_CreatesStableRequestIdForRenderedForm()
    {
        var repository = new NullPaymentRepository();
        var initiationRepository = new NullPaymentInitiationRepository();
        var provider = new DevelopmentPaymentProviderInitiator(
            new DevelopmentPaymentAvailability(true));
        var initiationService = new PaymentInitiationService(
            repository,
            initiationRepository,
            provider,
            new ImmediateTransaction(),
            TimeProvider.System,
            NullAuditTrail.Instance);
        var model = new CreateTopUpModel(
            new PaymentCreationService(
                repository,
                new NullJobQueries(),
                new NullJobPaymentCoordination(),
                new ImmediateTransaction(),
                TimeProvider.System,
                NullAuditTrail.Instance,
                new NullOrderNumberAllocator(),
                provider,
                initiationService),
            new PaymentCreationAvailability(true));

        model.OnGet();
        var renderedRequestId = model.CreationRequestId;

        Assert.NotEqual(Guid.Empty, renderedRequestId);
        Assert.Equal(renderedRequestId, model.CreationRequestId);
    }

    [Fact]
    public void TopUpGet_DisabledReturnsNotFoundWithoutRenderingForm()
    {
        var model = new CreateTopUpModel(
            UnusedDependency<PaymentCreationService>(),
            new PaymentCreationAvailability(false));

        var result = model.OnGet();

        Assert.IsType<Microsoft.AspNetCore.Mvc.NotFoundResult>(result);
        Assert.Equal(Guid.Empty, model.CreationRequestId);
    }

    [Fact]
    public async Task TopUpPost_DisabledRejectsBeforePaymentCreationService()
    {
        var customerUserId = Guid.NewGuid();
        var model = new CreateTopUpModel(
            UnusedDependency<PaymentCreationService>(),
            new PaymentCreationAvailability(false))
        {
            PageContext = CreatePageContext(customerUserId),
            CreationRequestId = Guid.NewGuid(),
            AmountCrowns = 500m
        };

        var result = await model.OnPostAsync();

        Assert.IsType<Microsoft.AspNetCore.Mvc.NotFoundResult>(result);
    }

    [Fact]
    public async Task CreditAdjustmentGet_CreatesStableCommandIdForRenderedForm()
    {
        var model = new CreditIndexModel(
            new EmptyCreditQueries(),
            administration: null!,
            manualTopUps: null!,
            new EmptyAccessUserQueries());

        await model.OnGetAsync();
        var adjustmentCommandId = model.Adjustment.CommandId;
        var manualTopUpCommandId = model.ManualTopUp.CommandId;

        Assert.NotEqual(Guid.Empty, adjustmentCommandId);
        Assert.NotEqual(Guid.Empty, manualTopUpCommandId);
        Assert.NotEqual(adjustmentCommandId, manualTopUpCommandId);
    }

    [Fact]
    public async Task ManualTopUpPost_MissingCustomerOptionFailsClosed()
    {
        var model = new CreditIndexModel(
            new EmptyCreditQueries(),
            administration: null!,
            manualTopUps: null!,
            new EmptyAccessUserQueries());
        var input = new CreditIndexModel.ManualCreditTopUpInput
        {
            CommandId = Guid.NewGuid(),
            OwnerId = Guid.NewGuid(),
            AmountCrowns = 25m,
            Note = "Customer disappeared after validation"
        };

        var result = await model.OnPostManualTopUpAsync(input);

        Assert.IsType<PageResult>(result);
        Assert.False(model.ModelState.IsValid);
        Assert.Contains(
            model.ModelState.Keys,
            key => key.EndsWith(
                nameof(input.OwnerId),
                StringComparison.Ordinal));
    }

    private static CreditMovementListItem Movement(
        Guid operationId,
        string description) =>
        new(
            operationId,
            CreditMovementType.Credit,
            AmountMinorUnits: 2_500,
            BalanceAfterMinorUnits: 5_000,
            description,
            DateTimeOffset.UtcNow,
            Sequence: 1);

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

    private static T UnusedDependency<T>()
        where T : class =>
        (T)System.Runtime.CompilerServices.RuntimeHelpers
            .GetUninitializedObject(typeof(T));

    private static string ReadPageSource(params string[] relativePath)
    {
        return File.ReadAllText(
            Path.Combine(
                [
                    FindRepositoryRoot(),
                    "src",
                    "FuaPay.Web",
                    "Pages",
                    .. relativePath
                ]),
            Encoding.UTF8);
    }

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

    private sealed class NullPaymentRepository : IPaymentRepository
    {
        public Task<Payment?> FindByIdAsync(Guid paymentId, CancellationToken cancellationToken = default) =>
            Task.FromResult<Payment?>(null);

        public Task<Payment?> FindBlockingForJobAsync(Guid jobId, CancellationToken cancellationToken = default) =>
            Task.FromResult<Payment?>(null);

        public Task<Payment?> FindByProviderReferenceAsync(PaymentProvider provider, string providerReference, CancellationToken cancellationToken = default) =>
            Task.FromResult<Payment?>(null);

        public Task<Payment?> FindByCreationRequestIdAsync(Guid creationRequestId, CancellationToken cancellationToken = default) =>
            Task.FromResult<Payment?>(null);

        public Task AddAsync(Payment payment, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task AddPreparedAsync(Payment payment, PaymentInitiation initiation, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task SaveAsync(Payment payment, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FixedCustomerPaymentQueries : IPaymentQueries
    {
        private readonly PaymentListItem _payment;

        public FixedCustomerPaymentQueries(PaymentListItem payment)
        {
            _payment = payment;
        }

        public Task<PaymentPage> ListForCustomerAsync(
            Guid customerUserId,
            PaymentListFilter filter,
            PaymentPageRequest page,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new PaymentPage(
                _payment.CustomerUserId == customerUserId
                    ? [_payment]
                    : [],
                page.Offset,
                page.Limit,
                _payment.CustomerUserId == customerUserId ? 1 : 0));

        public Task<PaymentPage> ListForAdministrationAsync(
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
    }

    private sealed class NullPaymentInitiationRepository :
        IPaymentInitiationRepository
    {
        public Task<PaymentInitiation?> FindByPaymentIdAsync(
            Guid paymentId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<PaymentInitiation?>(null);

        public Task SaveAsync(
            PaymentInitiation initiation,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class NullOrderNumberAllocator :
        IPaymentOrderNumberAllocator
    {
        public Task<long> AllocateAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class ImmediateTransaction : IApplicationTransaction
    {
        public Task<T> ExecuteAsync<T>(
            Func<CancellationToken, Task<T>> operation,
            CancellationToken cancellationToken = default) =>
            operation(cancellationToken);
    }

    private sealed class NullJobPaymentCoordination :
        IJobPaymentCoordination
    {
        public Task<bool> LockJobAsync(
            Guid jobId,
            CancellationToken cancellationToken) =>
            Task.FromResult(false);

        public Task<bool> HasBlockingDirectPaymentAsync(
            Guid jobId,
            CancellationToken cancellationToken) =>
            Task.FromResult(false);
    }

    private sealed class NullJobQueries : IJobQueries
    {
        public Task<CustomerJobSummary> GetCustomerSummaryAsync(Guid customerUserId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<JobDetail?> FindForCustomerAsync(Guid customerUserId, Guid jobId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<JobPage<JobListItem>> ListForCustomerAsync(Guid customerUserId, JobListFilter filter, JobPageRequest page, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ManagementJobSummary> GetManagementSummaryAsync(JobManagementActor actor, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<JobDetail?> FindForManagementAsync(JobManagementActor actor, Guid jobId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<JobPage<JobListItem>> ListForManagementAsync(JobManagementActor actor, JobListFilter filter, JobPageRequest page, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class RecordingCustomerCreditQueries : ICreditQueries
    {
        private readonly IReadOnlyList<CreditMovementListItem> _movements;

        public RecordingCustomerCreditQueries(
            IReadOnlyList<CreditMovementListItem> movements)
        {
            _movements = movements;
        }

        public Guid? LastOwnerId { get; private set; }

        public Task<CreditAccountSummary?> FindAccountForOwnerAsync(
            Guid ownerId,
            CancellationToken cancellationToken = default)
        {
            LastOwnerId = ownerId;
            return Task.FromResult<CreditAccountSummary?>(null);
        }

        public Task<CreditMovementPage> ListMovementsForOwnerAsync(
            Guid ownerId,
            CreditMovementPageRequest page,
            CancellationToken cancellationToken = default)
        {
            LastOwnerId = ownerId;
            return Task.FromResult(new CreditMovementPage(
                _movements,
                page.Offset,
                page.Limit,
                _movements.Count));
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
    }

    private sealed class RecordingFinancialDocumentQueries :
        IFinancialDocumentQueries
    {
        private readonly IReadOnlyDictionary<
            (FinancialDocumentSourceType SourceType, Guid SourceId),
            Guid> _documentIds;

        public RecordingFinancialDocumentQueries(
            IReadOnlyDictionary<
                (FinancialDocumentSourceType SourceType, Guid SourceId),
                Guid>? documentIds = null)
        {
            _documentIds = documentIds ??
                new Dictionary<
                    (FinancialDocumentSourceType SourceType, Guid SourceId),
                    Guid>();
        }

        public int SourceLookupCalls { get; private set; }

        public Guid? LastCustomerUserId { get; private set; }

        public FinancialDocumentSourceType? LastSourceType { get; private set; }

        public IReadOnlyList<Guid> LastSourceIds { get; private set; } = [];

        public Task<IReadOnlyDictionary<Guid, Guid>>
            FindDocumentIdsBySourceForCustomerAsync(
                Guid customerUserId,
                FinancialDocumentSourceType sourceType,
                IEnumerable<Guid> sourceIds,
                CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SourceLookupCalls++;
            LastCustomerUserId = customerUserId;
            LastSourceType = sourceType;
            LastSourceIds = sourceIds.ToArray();

            IReadOnlyDictionary<Guid, Guid> result = LastSourceIds
                .Where(sourceId =>
                    _documentIds.ContainsKey((sourceType, sourceId)))
                .ToDictionary(
                    sourceId => sourceId,
                    sourceId => _documentIds[(sourceType, sourceId)]);

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

    private sealed class EmptyCreditQueries : ICreditQueries
    {
        public Task<CreditAdministrationMovementPage> ListAdministrationMovementsAsync(
            CreditAdministrationMovementFilter filter,
            CreditMovementPageRequest page,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new CreditAdministrationMovementPage([], page.Offset, page.Limit, 0));

        public Task<CreditAccountSummary?> FindAccountForOwnerAsync(Guid ownerId, CancellationToken cancellationToken = default) =>
            Task.FromResult<CreditAccountSummary?>(null);

        public Task<CreditMovementListItem?> FindMovementForOwnerAsync(
            Guid ownerId,
            Guid operationId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<CreditMovementListItem?>(null);

        public Task<CreditMovementPage> ListMovementsForOwnerAsync(Guid ownerId, CreditMovementPageRequest page, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class EmptyAccessUserQueries : IAccessUserQueries
    {
        public Task<AccessUserPage> ListAsync(AccessUserListRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AccessUserDetail?> FindDetailAsync(Guid userId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<AccessUserOption>> ListActiveCustomersAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AccessUserOption>>([]);

        public Task<IReadOnlyDictionary<Guid, AccessUserOption>> FindOptionsAsync(IEnumerable<Guid> userIds, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<Guid, AccessUserOption>>(new Dictionary<Guid, AccessUserOption>());

        public Task<bool> IsActiveAsync(Guid userId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> IsActiveCustomerAsync(Guid userId, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task<long> CountActiveUsersWithRoleAsync(AccessRole role, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
