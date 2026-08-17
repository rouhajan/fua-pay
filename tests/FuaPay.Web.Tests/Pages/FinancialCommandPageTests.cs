using FuaPay.Web.BuildingBlocks.Application;
using FuaPay.Web.BuildingBlocks.Auditing;
using FuaPay.Web.Modules.Access.Application;
using FuaPay.Web.Modules.Access.Domain;
using FuaPay.Web.Modules.Credits.Application;
using FuaPay.Web.Modules.Jobs.Application;
using FuaPay.Web.Modules.Payments.Application;
using FuaPay.Web.Modules.Payments.Domain;
using FuaPay.Web.Pages.Customer.Payments;

using CreditIndexModel = FuaPay.Web.Pages.Admin.Credit.IndexModel;

namespace FuaPay.Web.Tests.Pages;

public sealed class FinancialCommandPageTests
{
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
                TimeProvider.System,
                NullAuditTrail.Instance,
                new NullOrderNumberAllocator(),
                provider,
                initiationService));

        model.OnGet();
        var renderedRequestId = model.CreationRequestId;

        Assert.NotEqual(Guid.Empty, renderedRequestId);
        Assert.Equal(renderedRequestId, model.CreationRequestId);
    }

    [Fact]
    public async Task CreditAdjustmentGet_CreatesStableCommandIdForRenderedForm()
    {
        var model = new CreditIndexModel(
            new EmptyCreditQueries(),
            administration: null!,
            new EmptyAccessUserQueries());

        await model.OnGetAsync();
        var renderedCommandId = model.CommandId;

        Assert.NotEqual(Guid.Empty, renderedCommandId);
        Assert.Equal(renderedCommandId, model.CommandId);
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

    private sealed class EmptyCreditQueries : ICreditQueries
    {
        public Task<CreditAdministrationMovementPage> ListAdministrationMovementsAsync(
            CreditAdministrationMovementFilter filter,
            CreditMovementPageRequest page,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new CreditAdministrationMovementPage([], page.Offset, page.Limit, 0));

        public Task<CreditAccountSummary?> FindAccountForOwnerAsync(Guid ownerId, CancellationToken cancellationToken = default) =>
            Task.FromResult<CreditAccountSummary?>(null);

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
            throw new NotSupportedException();

        public Task<long> CountActiveUsersWithRoleAsync(AccessRole role, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
