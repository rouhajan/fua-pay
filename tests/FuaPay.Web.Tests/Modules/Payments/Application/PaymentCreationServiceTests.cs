using FuaPay.Web.BuildingBlocks.Application;
using FuaPay.Web.BuildingBlocks.Auditing;
using FuaPay.Web.BuildingBlocks.Domain;
using FuaPay.Web.Modules.Jobs.Application;
using FuaPay.Web.Modules.Jobs.Domain;
using FuaPay.Web.Modules.Payments.Application;
using FuaPay.Web.Modules.Payments.Domain;

namespace FuaPay.Web.Tests.Modules.Payments.Application;

public sealed class PaymentCreationServiceTests
{
    private static readonly DateTimeOffset CurrentTime =
        new(2026, 7, 29, 14, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task CreateCreditTopUpAsync_UsesProviderNeutralInitialization()
    {
        var harness = new CreationHarness();
        var customerUserId = Guid.NewGuid();
        var creationRequestId = Guid.NewGuid();

        var payment = await harness.Service.CreateCreditTopUpAsync(
            creationRequestId,
            customerUserId,
            new Money(50_000));

        Assert.Same(payment, harness.Payments.AddedPayment);
        Assert.Equal(customerUserId, payment.CustomerUserId);
        Assert.Equal(PaymentPurposeType.CreditTopUp, payment.PurposeType);
        Assert.Equal(creationRequestId, payment.CreationRequestId);
        Assert.Equal(PaymentProvider.Development, payment.Provider);
        Assert.Equal(PaymentStatus.Pending, payment.Status);
        Assert.StartsWith(
            "DEV-",
            Assert.IsType<string>(payment.ProviderReference));
        Assert.Equal(1, harness.Payments.AddPreparedCalls);
        Assert.Equal(1, harness.OrderNumbers.AllocateCalls);
        Assert.Equal(1, harness.Provider.InitializeCalls);

        var request = Assert.IsType<PaymentProviderInitializationRequest>(
            harness.Provider.LastRequest);
        Assert.Equal(payment.Id, request.PaymentId);
        Assert.Equal(
            PaymentProviderCorrelation.Encode(
                request.PaymentId,
                request.CorrelationId),
            request.CorrelationData);

        var initiation = Assert.IsType<PaymentInitiation>(
            harness.Initiations.Stored);
        Assert.Equal(PaymentInitiationState.Initialized, initiation.State);
        Assert.Equal(request.OrderNumber, initiation.OrderNumber);
    }

    [Theory]
    [InlineData(999)]
    [InlineData(10_000_001)]
    public async Task CreateCreditTopUpAsync_RejectsAmountOutsideLimits(
        long minorUnits)
    {
        var harness = new CreationHarness();

        await Assert.ThrowsAsync<PaymentAmountNotAllowedException>(
            () => harness.Service.CreateCreditTopUpAsync(
                Guid.NewGuid(),
                Guid.NewGuid(),
                new Money(minorUnits)));

        Assert.Equal(0, harness.OrderNumbers.AllocateCalls);
        Assert.Equal(0, harness.Provider.InitializeCalls);
    }

    [Fact]
    public async Task CreateJobPaymentAsync_UsesPublishedUnpaidJobPrice()
    {
        var customerUserId = Guid.NewGuid();
        var job = CreateJobDetail(customerUserId);
        var queries = new StubJobQueries { Job = job };
        var harness = new CreationHarness(jobQueries: queries);

        var payment = await harness.Service.CreateJobPaymentAsync(
            customerUserId,
            job.Id);

        Assert.Equal(PaymentPurposeType.Job, payment.PurposeType);
        Assert.Equal(job.Id, payment.JobId);
        Assert.Equal(job.PriceMinorUnits, payment.Amount.MinorUnits);
        Assert.Equal(1, queries.FindForCustomerCalls);
        Assert.Equal(1, harness.Provider.InitializeCalls);
    }

    [Fact]
    public async Task CreateJobPaymentAsync_ReturnsExistingOwnedPayment()
    {
        var customerUserId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var existing = new Payment(
            Guid.NewGuid(),
            customerUserId,
            PaymentPurposeType.Job,
            jobId,
            new Money(12_000),
            PaymentProvider.Development,
            CurrentTime);
        existing.MarkPending("DEV-EXISTING", CurrentTime);

        var queries = new StubJobQueries();
        var harness = new CreationHarness(jobQueries: queries);
        harness.Payments.OpenPayment = existing;

        var result = await harness.Service.CreateJobPaymentAsync(
            customerUserId,
            jobId);

        Assert.Same(existing, result);
        Assert.Equal(0, queries.FindForCustomerCalls);
        Assert.Equal(0, harness.Payments.AddPreparedCalls);
        Assert.Equal(0, harness.Provider.InitializeCalls);
    }

    [Fact]
    public async Task CreateCreditTopUpAsync_RejectsDisabledProviderForNewPayment()
    {
        var harness = new CreationHarness(providerEnabled: false);

        await Assert.ThrowsAsync<PaymentProviderUnavailableException>(
            () => harness.Service.CreateCreditTopUpAsync(
                Guid.NewGuid(),
                Guid.NewGuid(),
                new Money(50_000)));

        Assert.Equal(0, harness.OrderNumbers.AllocateCalls);
        Assert.Equal(0, harness.Payments.AddPreparedCalls);
    }

    [Fact]
    public async Task CreateCreditTopUpAsync_SameRequestReturnsOriginalPayment()
    {
        var audit = new RecordingAuditTrail();
        var harness = new CreationHarness(auditTrail: audit);
        var creationRequestId = Guid.NewGuid();
        var customerUserId = Guid.NewGuid();

        var first = await harness.Service.CreateCreditTopUpAsync(
            creationRequestId,
            customerUserId,
            new Money(50_000));
        var replay = await harness.Service.CreateCreditTopUpAsync(
            creationRequestId,
            customerUserId,
            new Money(50_000));

        Assert.Same(first, replay);
        Assert.Equal(1, harness.Payments.AddPreparedCalls);
        Assert.Equal(1, harness.Provider.InitializeCalls);
        Assert.Equal(4, audit.Entries.Count);
        Assert.Contains(
            audit.Entries,
            entry => entry.Action ==
                "payment.provider-initiation.candidate-observed");
    }

    [Fact]
    public async Task CreateCreditTopUpAsync_RetryAfterUnknownOutcomeIgnoresCurrentAvailability()
    {
        var initiations = new FakePaymentInitiationRepository();
        var payments = new FakePaymentRepository(initiations);
        var enabled = new CreationHarness(
            payments: payments,
            initiations: initiations,
            providerEnabled: true);
        var creationRequestId = Guid.NewGuid();
        var customerUserId = Guid.NewGuid();
        var created = await enabled.Service.CreateCreditTopUpAsync(
            creationRequestId,
            customerUserId,
            new Money(50_000));
        var disabled = new CreationHarness(
            payments: payments,
            initiations: initiations,
            providerEnabled: false);

        var replay = await disabled.Service.CreateCreditTopUpAsync(
            creationRequestId,
            customerUserId,
            new Money(50_000));

        Assert.Same(created, replay);
        Assert.Equal(1, payments.AddPreparedCalls);
        Assert.Equal(0, disabled.Provider.InitializeCalls);
    }

    [Fact]
    public async Task CreateCreditTopUpAsync_PreparedReplayResumesInitialization()
    {
        var initiations = new FakePaymentInitiationRepository();
        var payments = new FakePaymentRepository(initiations);
        var creationRequestId = Guid.NewGuid();
        var customerUserId = Guid.NewGuid();
        var payment = new Payment(
            Guid.NewGuid(),
            customerUserId,
            PaymentPurposeType.CreditTopUp,
            jobId: null,
            new Money(50_000),
            PaymentProvider.Development,
            CurrentTime,
            creationRequestId);
        var initiation = new PaymentInitiation(
            payment.Id,
            payment.Provider,
            42,
            Guid.NewGuid(),
            CurrentTime);
        payments.Seed(payment, initiation);
        var harness = new CreationHarness(
            payments: payments,
            initiations: initiations);

        var replay = await harness.Service.CreateCreditTopUpAsync(
            creationRequestId,
            customerUserId,
            new Money(50_000));

        Assert.Same(payment, replay);
        Assert.Equal(PaymentStatus.Pending, replay.Status);
        Assert.Equal(PaymentInitiationState.Initialized, initiation.State);
        Assert.Equal(1, harness.Provider.InitializeCalls);
        Assert.Equal(0, harness.OrderNumbers.AllocateCalls);
        Assert.Equal(0, payments.AddPreparedCalls);
    }

    [Fact]
    public async Task CreateCreditTopUpAsync_SameRequestWithDifferentPayloadConflicts()
    {
        var harness = new CreationHarness();
        var creationRequestId = Guid.NewGuid();
        var customerUserId = Guid.NewGuid();

        await harness.Service.CreateCreditTopUpAsync(
            creationRequestId,
            customerUserId,
            new Money(50_000));

        await Assert.ThrowsAsync<PaymentCreationRequestConflictException>(
            () => harness.Service.CreateCreditTopUpAsync(
                creationRequestId,
                customerUserId,
                new Money(50_001)));
        Assert.Equal(1, harness.Payments.AddPreparedCalls);
    }

    private static JobDetail CreateJobDetail(Guid customerUserId)
    {
        return new JobDetail(
            Guid.NewGuid(),
            "3D-2026-000001",
            Guid.NewGuid(),
            customerUserId,
            Guid.NewGuid(),
            ServiceType.ThreeDPrint,
            "Model",
            "Description",
            42_000,
            JobProductionStatus.Published,
            JobPaymentStatus.Unpaid,
            SettlementType: null,
            SettlementReferenceId: null,
            CurrentTime,
            CurrentTime,
            SettledAt: null,
            ProductionStartedAt: null,
            ReadyForPickupAt: null,
            CompletedAt: null,
            CancelledAt: null,
            Version: 1);
    }

    private sealed class CreationHarness
    {
        public CreationHarness(
            FakePaymentRepository? payments = null,
            FakePaymentInitiationRepository? initiations = null,
            IJobQueries? jobQueries = null,
            IAuditTrail? auditTrail = null,
            bool providerEnabled = true)
        {
            Initiations = initiations ?? new FakePaymentInitiationRepository();
            Payments = payments ?? new FakePaymentRepository(Initiations);
            OrderNumbers = new FixedOrderNumberAllocator();
            Provider = new RecordingDevelopmentProviderInitiator(
                providerEnabled);
            var audit = auditTrail ?? NullAuditTrail.Instance;
            var initiationService = new PaymentInitiationService(
                Payments,
                Initiations,
                Provider,
                new ImmediateTransaction(),
                new FixedTimeProvider(CurrentTime),
                audit);
            Service = new PaymentCreationService(
                Payments,
                jobQueries ?? new StubJobQueries(),
                new FixedTimeProvider(CurrentTime),
                audit,
                OrderNumbers,
                Provider,
                initiationService);
        }

        public PaymentCreationService Service { get; }

        public FakePaymentRepository Payments { get; }

        public FakePaymentInitiationRepository Initiations { get; }

        public FixedOrderNumberAllocator OrderNumbers { get; }

        public RecordingDevelopmentProviderInitiator Provider { get; }
    }

    private sealed class FakePaymentRepository : IPaymentRepository
    {
        private readonly FakePaymentInitiationRepository _initiations;

        public FakePaymentRepository(
            FakePaymentInitiationRepository initiations)
        {
            _initiations = initiations;
        }

        public Payment? OpenPayment { get; set; }

        public Payment? AddedPayment { get; private set; }

        public int AddPreparedCalls { get; private set; }

        public void Seed(
            Payment payment,
            PaymentInitiation initiation)
        {
            AddedPayment = payment;
            _initiations.Stored = initiation;
        }

        public Task<Payment?> FindByIdAsync(
            Guid paymentId,
            CancellationToken cancellationToken = default)
        {
            var payment = AddedPayment?.Id == paymentId
                ? AddedPayment
                : OpenPayment?.Id == paymentId
                    ? OpenPayment
                    : null;
            return Task.FromResult(payment);
        }

        public Task<Payment?> FindBlockingForJobAsync(
            Guid jobId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(OpenPayment);
        }

        public Task<Payment?> FindByProviderReferenceAsync(
            PaymentProvider provider,
            string providerReference,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<Payment?>(null);
        }

        public Task<Payment?> FindByCreationRequestIdAsync(
            Guid creationRequestId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(
                AddedPayment?.CreationRequestId == creationRequestId
                    ? AddedPayment
                    : null);
        }

        public Task AddAsync(
            Payment payment,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task AddPreparedAsync(
            Payment payment,
            PaymentInitiation initiation,
            CancellationToken cancellationToken = default)
        {
            AddPreparedCalls++;
            AddedPayment = payment;
            _initiations.Stored = initiation;
            return Task.CompletedTask;
        }

        public Task SaveAsync(
            Payment payment,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class FakePaymentInitiationRepository :
        IPaymentInitiationRepository
    {
        public PaymentInitiation? Stored { get; set; }

        public Task<PaymentInitiation?> FindByPaymentIdAsync(
            Guid paymentId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(
                Stored?.PaymentId == paymentId ? Stored : null);
        }

        public Task SaveAsync(
            PaymentInitiation initiation,
            CancellationToken cancellationToken = default)
        {
            Stored = initiation;
            return Task.CompletedTask;
        }
    }

    private sealed class FixedOrderNumberAllocator :
        IPaymentOrderNumberAllocator
    {
        public int AllocateCalls { get; private set; }

        public Task<long> AllocateAsync(
            CancellationToken cancellationToken = default)
        {
            AllocateCalls++;
            return Task.FromResult((long)AllocateCalls);
        }
    }

    private sealed class RecordingDevelopmentProviderInitiator :
        IPaymentProviderInitiator
    {
        private readonly DevelopmentPaymentAvailability _availability;

        public RecordingDevelopmentProviderInitiator(bool enabled)
        {
            _availability = new DevelopmentPaymentAvailability(enabled);
        }

        public PaymentProvider Provider => PaymentProvider.Development;

        public int InitializeCalls { get; private set; }

        public PaymentProviderInitializationRequest? LastRequest
        {
            get;
            private set;
        }

        public void EnsureAvailable() => _availability.EnsureEnabled();

        public Task<PaymentProviderInitializationResult> InitializeAsync(
            PaymentProviderInitializationRequest request,
            CancellationToken cancellationToken = default)
        {
            EnsureAvailable();
            InitializeCalls++;
            LastRequest = request;
            return Task.FromResult(
                new PaymentProviderInitializationResult(
                    Provider,
                    $"DEV-{request.PaymentId:N}".ToUpperInvariant(),
                    processUri: null));
        }

        public Task VerifyAsync(
            PaymentProviderInitializationResult candidate,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class ImmediateTransaction : IApplicationTransaction
    {
        public Task<T> ExecuteAsync<T>(
            Func<CancellationToken, Task<T>> operation,
            CancellationToken cancellationToken = default) =>
            operation(cancellationToken);
    }

    private sealed class RecordingAuditTrail : IAuditTrail
    {
        public List<AuditEntry> Entries { get; } = [];

        public void Stage(AuditEntry entry) => Entries.Add(entry);

        public Task WriteAsync(
            AuditEntry entry,
            CancellationToken cancellationToken = default)
        {
            Entries.Add(entry);
            return Task.CompletedTask;
        }
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _currentTime;

        public FixedTimeProvider(DateTimeOffset currentTime)
        {
            _currentTime = currentTime;
        }

        public override DateTimeOffset GetUtcNow() => _currentTime;
    }

    private sealed class StubJobQueries : IJobQueries
    {
        public JobDetail? Job { get; set; }

        public int FindForCustomerCalls { get; private set; }

        public Task<CustomerJobSummary> GetCustomerSummaryAsync(
            Guid customerUserId,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<JobDetail?> FindForCustomerAsync(
            Guid customerUserId,
            Guid jobId,
            CancellationToken cancellationToken = default)
        {
            FindForCustomerCalls++;
            return Task.FromResult(
                Job?.CustomerUserId == customerUserId && Job.Id == jobId
                    ? Job
                    : null);
        }

        public Task<JobPage<JobListItem>> ListForCustomerAsync(
            Guid customerUserId,
            JobListFilter filter,
            JobPageRequest page,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<ManagementJobSummary> GetManagementSummaryAsync(
            JobManagementActor actor,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<JobDetail?> FindForManagementAsync(
            JobManagementActor actor,
            Guid jobId,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<JobPage<JobListItem>> ListForManagementAsync(
            JobManagementActor actor,
            JobListFilter filter,
            JobPageRequest page,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }
}
