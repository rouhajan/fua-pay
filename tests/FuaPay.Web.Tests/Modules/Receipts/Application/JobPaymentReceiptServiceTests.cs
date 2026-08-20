using FuaPay.Web.Modules.Access.Application;
using FuaPay.Web.Modules.Access.Domain;
using FuaPay.Web.Modules.Credits.Application;
using FuaPay.Web.Modules.Credits.Domain;
using FuaPay.Web.Modules.Jobs.Application;
using FuaPay.Web.Modules.Jobs.Domain;
using FuaPay.Web.Modules.Payments.Application;
using FuaPay.Web.Modules.Payments.Domain;
using FuaPay.Web.Modules.Receipts.Application;
using FuaPay.Web.Modules.ServiceUnits.Application;
using FuaPay.Web.Modules.ServiceUnits.Domain;

namespace FuaPay.Web.Tests.Modules.Receipts.Application;

public sealed class JobPaymentReceiptServiceTests
{
    private static readonly DateTimeOffset SettledAt =
        new(2026, 8, 19, 9, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task CreateForCustomerJobAsync_CreditSettlementBuildsReceipt()
    {
        var fixture = ReceiptFixture.Create(JobSettlementType.Credit);

        var receipt = await fixture.Service.CreateForCustomerJobAsync(
            fixture.CustomerUserId,
            fixture.JobId);

        Assert.NotNull(receipt);
        Assert.Equal(fixture.JobNumber, receipt.JobNumber);
        Assert.Equal("Kredit FUA Pay", receipt.SettlementMethod);
        Assert.Null(receipt.PaymentProvider);
        Assert.Null(receipt.ProviderReference);
        Assert.Equal(12_100, receipt.GrossAmountMinorUnits);
        Assert.Equal(10_000, receipt.TaxBaseMinorUnits);
        Assert.Equal(2_100, receipt.VatAmountMinorUnits);
        Assert.Equal(21, receipt.VatRatePercent);
        Assert.True(receipt.PreviewMode);
    }

    [Fact]
    public async Task CreateForCustomerJobAsync_DirectPaymentBuildsReceipt()
    {
        var fixture = ReceiptFixture.Create(
            JobSettlementType.DirectPayment);

        var receipt = await fixture.Service.CreateForCustomerJobAsync(
            fixture.CustomerUserId,
            fixture.JobId);

        Assert.NotNull(receipt);
        Assert.Equal("Přímá platba", receipt.SettlementMethod);
        Assert.Equal("ČSOB", receipt.PaymentProvider);
        Assert.Equal("csob-pay-id", receipt.ProviderReference);
        Assert.Equal(fixture.SettlementReferenceId, receipt.SettlementReferenceId);
    }

    [Fact]
    public async Task CreateForCustomerJobAsync_UnpaidJobReturnsNull()
    {
        var fixture = ReceiptFixture.Create(
            settlementType: null,
            paymentStatus: JobPaymentStatus.Unpaid);

        var receipt = await fixture.Service.CreateForCustomerJobAsync(
            fixture.CustomerUserId,
            fixture.JobId);

        Assert.Null(receipt);
    }

    [Fact]
    public async Task CreateForCustomerJobAsync_MissingCustomerJobReturnsNull()
    {
        var fixture = ReceiptFixture.Create(JobSettlementType.Credit);
        fixture.JobQueries.Job = null;

        var receipt = await fixture.Service.CreateForCustomerJobAsync(
            fixture.CustomerUserId,
            fixture.JobId);

        Assert.Null(receipt);
    }

    [Fact]
    public async Task CreateForCustomerJobAsync_DirectPaymentAmountMismatchFailsClosed()
    {
        var fixture = ReceiptFixture.Create(
            JobSettlementType.DirectPayment);
        fixture.PaymentQueries.Payment = fixture.PaymentQueries.Payment! with
        {
            AmountMinorUnits = 12_099
        };

        var exception = await Assert.ThrowsAsync<ReceiptConsistencyException>(
            () => fixture.Service.CreateForCustomerJobAsync(
                fixture.CustomerUserId,
                fixture.JobId));

        Assert.Contains(
            "přímá platba neodpovídá",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateForCustomerJobAsync_CreditReferenceMustBeDebit()
    {
        var fixture = ReceiptFixture.Create(JobSettlementType.Credit);
        fixture.CreditQueries.Movement = fixture.CreditQueries.Movement! with
        {
            Type = CreditMovementType.Credit
        };

        var exception = await Assert.ThrowsAsync<ReceiptConsistencyException>(
            () => fixture.Service.CreateForCustomerJobAsync(
                fixture.CustomerUserId,
                fixture.JobId));

        Assert.Contains(
            "kreditní debet",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateForCustomerJobAsync_CreditAmountMismatchFailsClosed()
    {
        var fixture = ReceiptFixture.Create(JobSettlementType.Credit);
        fixture.CreditQueries.Movement = fixture.CreditQueries.Movement! with
        {
            AmountMinorUnits = 12_099
        };

        var exception = await Assert.ThrowsAsync<ReceiptConsistencyException>(
            () => fixture.Service.CreateForCustomerJobAsync(
                fixture.CustomerUserId,
                fixture.JobId));

        Assert.Contains(
            "částka kreditního debetu",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateForCustomerJobAsync_CreditReferenceMustMatchJobId()
    {
        var fixture = ReceiptFixture.Create(JobSettlementType.Credit);
        fixture.JobQueries.Job = fixture.JobQueries.Job! with
        {
            SettlementReferenceId = Guid.NewGuid()
        };

        var exception = await Assert.ThrowsAsync<ReceiptConsistencyException>(
            () => fixture.Service.CreateForCustomerJobAsync(
                fixture.CustomerUserId,
                fixture.JobId));

        Assert.Contains(
            "reference kreditní úhrady",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateForCustomerJobAsync_CreditTimestampMismatchFailsClosed()
    {
        var fixture = ReceiptFixture.Create(JobSettlementType.Credit);
        fixture.CreditQueries.Movement = fixture.CreditQueries.Movement! with
        {
            RecordedAt = SettledAt.AddMinutes(1)
        };

        var exception = await Assert.ThrowsAsync<ReceiptConsistencyException>(
            () => fixture.Service.CreateForCustomerJobAsync(
                fixture.CustomerUserId,
                fixture.JobId));

        Assert.Contains(
            "čas kreditního debetu",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateForCustomerJobAsync_DirectPaymentRequiresProviderReference()
    {
        var fixture = ReceiptFixture.Create(
            JobSettlementType.DirectPayment);
        fixture.PaymentQueries.Payment = fixture.PaymentQueries.Payment! with
        {
            ProviderReference = null
        };

        var exception = await Assert.ThrowsAsync<ReceiptConsistencyException>(
            () => fixture.Service.CreateForCustomerJobAsync(
                fixture.CustomerUserId,
                fixture.JobId));

        Assert.Contains(
            "přímá platba neodpovídá",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateForCustomerJobAsync_DirectPaymentRequiresCompletionTime()
    {
        var fixture = ReceiptFixture.Create(
            JobSettlementType.DirectPayment);
        fixture.PaymentQueries.Payment = fixture.PaymentQueries.Payment! with
        {
            CompletedAt = null
        };

        var exception = await Assert.ThrowsAsync<ReceiptConsistencyException>(
            () => fixture.Service.CreateForCustomerJobAsync(
                fixture.CustomerUserId,
                fixture.JobId));

        Assert.Contains(
            "přímá platba neodpovídá",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateForCustomerJobAsync_DisabledReceiptsDoNotReadJob()
    {
        var fixture = ReceiptFixture.Create(
            JobSettlementType.Credit,
            receiptsEnabled: false);

        var receipt = await fixture.Service.CreateForCustomerJobAsync(
            fixture.CustomerUserId,
            fixture.JobId);

        Assert.Null(receipt);
        Assert.Equal(0, fixture.JobQueries.CustomerFindCount);
    }

    internal sealed class ReceiptFixture
    {
        private ReceiptFixture(
            Guid customerUserId,
            Guid jobId,
            Guid settlementReferenceId,
            string jobNumber,
            StubJobQueries jobQueries,
            StubCreditQueries creditQueries,
            StubPaymentQueries paymentQueries,
            JobPaymentReceiptService service)
        {
            CustomerUserId = customerUserId;
            JobId = jobId;
            SettlementReferenceId = settlementReferenceId;
            JobNumber = jobNumber;
            JobQueries = jobQueries;
            CreditQueries = creditQueries;
            PaymentQueries = paymentQueries;
            Service = service;
        }

        public Guid CustomerUserId { get; }
        public Guid JobId { get; }
        public Guid SettlementReferenceId { get; }
        public string JobNumber { get; }
        public StubJobQueries JobQueries { get; }
        public StubCreditQueries CreditQueries { get; }
        public StubPaymentQueries PaymentQueries { get; }
        public JobPaymentReceiptService Service { get; }

        public static ReceiptFixture Create(
            JobSettlementType? settlementType,
            JobPaymentStatus paymentStatus = JobPaymentStatus.Paid,
            bool receiptsEnabled = true)
        {
            var customerUserId = Guid.NewGuid();
            var creatorUserId = Guid.NewGuid();
            var serviceUnitId = Guid.NewGuid();
            var jobId = Guid.NewGuid();
            var settlementReferenceId =
                settlementType == JobSettlementType.Credit
                    ? jobId
                    : Guid.NewGuid();
            var jobNumber = NextJobNumber();

            var job = new JobDetail(
                jobId,
                jobNumber,
                serviceUnitId,
                customerUserId,
                creatorUserId,
                ServiceType.ThreeDPrint,
                "Testovací zakázka",
                "Popis",
                12_100,
                JobProductionStatus.Published,
                paymentStatus,
                settlementType,
                settlementType is null ? null : settlementReferenceId,
                SettledAt.AddDays(-1),
                SettledAt.AddHours(-2),
                settlementType is null ? null : SettledAt,
                null,
                null,
                null,
                null,
                1);

            var jobQueries = new StubJobQueries(job);
            var creditQueries = new StubCreditQueries
            {
                Movement = new CreditMovementListItem(
                    settlementReferenceId,
                    CreditMovementType.Debit,
                    12_100,
                    30_000,
                    "Úhrada testovací zakázky",
                    SettledAt,
                    3)
            };
            var paymentQueries = new StubPaymentQueries
            {
                Payment = new PaymentDetail(
                    settlementReferenceId,
                    customerUserId,
                    PaymentPurposeType.Job,
                    jobId,
                    12_100,
                    PaymentProvider.Csob,
                    PaymentStatus.Succeeded,
                    "csob-pay-id",
                    null,
                    SettledAt.AddMinutes(-2),
                    SettledAt,
                    SettledAt,
                    null,
                    2)
            };
            var accessQueries = new StubAccessUserQueries(
                new AccessUserOption(
                    customerUserId,
                    "Jan Testovací",
                    "jan@example.invalid"));
            var serviceUnitQueries = new StubServiceUnitQueries(
                new ServiceUnitAdministrationListItem(
                    serviceUnitId,
                    "3DT",
                    "3D tisk",
                    ServiceType.ThreeDPrint,
                    ServiceUnitStatus.Active,
                    SettledAt.AddYears(-1),
                    null,
                    1));
            var configuration = new ReceiptConfiguration(
                receiptsEnabled,
                PreviewMode: true,
                Issuer: new ReceiptIssuerConfiguration(
                    "Technická univerzita v Liberci",
                    "Fakulta umění a architektury",
                    "Studentská 1402/2",
                    "461 17 Liberec 1",
                    "Česká republika",
                    "00000000",
                    "CZ00000000",
                    "fua@tul.cz"),
                VatRatePercent: 21,
                LogoPath: "unused-in-service-test.png",
                RegularFontPath: null,
                BoldFontPath: null);

            var service = new JobPaymentReceiptService(
                jobQueries,
                creditQueries,
                paymentQueries,
                accessQueries,
                serviceUnitQueries,
                configuration);

            return new ReceiptFixture(
                customerUserId,
                jobId,
                settlementReferenceId,
                jobNumber,
                jobQueries,
                creditQueries,
                paymentQueries,
                service);
        }
    }

    internal sealed class StubJobQueries : IJobQueries
    {
        public StubJobQueries(JobDetail? job)
        {
            Job = job;
        }

        public JobDetail? Job { get; set; }
        public int CustomerFindCount { get; private set; }

        public Task<JobDetail?> FindForCustomerAsync(
            Guid customerUserId,
            Guid jobId,
            CancellationToken cancellationToken = default)
        {
            CustomerFindCount++;
            return Task.FromResult(
                Job is not null &&
                Job.CustomerUserId == customerUserId &&
                Job.Id == jobId
                    ? Job
                    : null);
        }

        public Task<CustomerJobSummary> GetCustomerSummaryAsync(Guid customerUserId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<JobPage<JobListItem>> ListForCustomerAsync(Guid customerUserId, JobListFilter filter, JobPageRequest page, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ManagementJobSummary> GetManagementSummaryAsync(JobManagementActor actor, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<JobDetail?> FindForManagementAsync(JobManagementActor actor, Guid jobId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<JobPage<JobListItem>> ListForManagementAsync(JobManagementActor actor, JobListFilter filter, JobPageRequest page, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    internal sealed class StubCreditQueries : ICreditQueries
    {
        public CreditMovementListItem? Movement { get; set; }

        public Task<CreditMovementListItem?> FindMovementForOwnerAsync(
            Guid ownerId,
            Guid operationId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(
                Movement?.OperationId == operationId
                    ? Movement
                    : null);

        public Task<CreditAdministrationMovementPage> ListAdministrationMovementsAsync(CreditAdministrationMovementFilter filter, CreditMovementPageRequest page, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CreditAccountSummary?> FindAccountForOwnerAsync(Guid ownerId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CreditMovementPage> ListMovementsForOwnerAsync(Guid ownerId, CreditMovementPageRequest page, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    internal sealed class StubPaymentQueries : IPaymentQueries
    {
        public PaymentDetail? Payment { get; set; }

        public Task<PaymentDetail?> FindForCustomerAsync(
            Guid customerUserId,
            Guid paymentId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(
                Payment is not null &&
                Payment.CustomerUserId == customerUserId &&
                Payment.Id == paymentId
                    ? Payment
                    : null);

        public Task<PaymentDetail?> FindForAdministrationAsync(Guid paymentId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PaymentPage> ListForCustomerAsync(Guid customerUserId, PaymentListFilter filter, PaymentPageRequest page, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PaymentPage> ListForAdministrationAsync(PaymentListFilter filter, PaymentPageRequest page, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class StubAccessUserQueries : IAccessUserQueries
    {
        private readonly AccessUserOption _user;

        public StubAccessUserQueries(AccessUserOption user)
        {
            _user = user;
        }

        public Task<IReadOnlyDictionary<Guid, AccessUserOption>> FindOptionsAsync(
            IEnumerable<Guid> userIds,
            CancellationToken cancellationToken = default)
        {
            var requested = userIds.ToHashSet();
            IReadOnlyDictionary<Guid, AccessUserOption> result =
                requested.Contains(_user.Id)
                    ? new Dictionary<Guid, AccessUserOption>
                    {
                        [_user.Id] = _user
                    }
                    : new Dictionary<Guid, AccessUserOption>();
            return Task.FromResult(result);
        }

        public Task<AccessUserPage> ListAsync(AccessUserListRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AccessUserDetail?> FindDetailAsync(Guid userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<AccessUserOption>> ListActiveCustomersAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> IsActiveAsync(Guid userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> IsActiveCustomerAsync(Guid userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<long> CountActiveUsersWithRoleAsync(AccessRole role, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class StubServiceUnitQueries : IServiceUnitQueries
    {
        private readonly ServiceUnitAdministrationListItem _serviceUnit;

        public StubServiceUnitQueries(
            ServiceUnitAdministrationListItem serviceUnit)
        {
            _serviceUnit = serviceUnit;
        }

        public Task<IReadOnlyList<ServiceUnitAdministrationListItem>> ListAllAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ServiceUnitAdministrationListItem>>(
                [_serviceUnit]);

        public Task<IReadOnlyList<RequesterServiceUnitAssignmentReadModel>> ListAssignmentsForUserAsync(Guid userId, bool includeRevoked = false, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ServiceUnitReadModel?> FindActiveAsync(Guid serviceUnitId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ServiceUnitReadModel>> ListActiveAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ServiceUnitReadModel>> ListForRequesterAsync(Guid userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
