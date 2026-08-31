using System.Runtime.CompilerServices;
using System.Security.Claims;

using FuaPay.Web.BuildingBlocks.Domain;
using FuaPay.Web.Modules.Access.Application;
using FuaPay.Web.Modules.Access.Domain;
using FuaPay.Web.Modules.Credits.Application;
using FuaPay.Web.Modules.Jobs.Application;
using FuaPay.Web.Modules.Jobs.Domain;
using FuaPay.Web.Modules.Jobs.Web;
using FuaPay.Web.Modules.Payments.Application;
using FuaPay.Web.Modules.Receipts.Application;
using FuaPay.Web.Modules.ServiceUnits.Application;
using FuaPay.Web.Pages.Customer.Jobs;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.RazorPages;

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
            new StubCreditQueries(account),
            availabilityService,
            UnusedDependency<CreditJobPaymentService>(),
            new JobPresentationComposer(
                new EmptyAccessUserQueries(),
                new EmptyServiceUnitQueries()),
            UnusedDependency<PaymentCreationService>(),
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

    private static ReceiptConfiguration DisabledReceiptConfiguration() =>
        new(
            Enabled: false,
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
}
