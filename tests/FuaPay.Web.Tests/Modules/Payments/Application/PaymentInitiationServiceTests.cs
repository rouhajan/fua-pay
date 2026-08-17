using FuaPay.Web.BuildingBlocks.Application;
using FuaPay.Web.BuildingBlocks.Auditing;
using FuaPay.Web.BuildingBlocks.Domain;
using FuaPay.Web.Modules.Payments.Application;
using FuaPay.Web.Modules.Payments.Domain;

namespace FuaPay.Web.Tests.Modules.Payments.Application;

public sealed class PaymentInitiationServiceTests
{
    private static readonly DateTimeOffset TestTime =
        new(2026, 8, 13, 16, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task InitializeAsync_PersistsProviderCorrelationAndPendingState()
    {
        var payment = CreatePayment(PaymentProvider.Csob);
        var initiation = CreateInitiation(payment);
        var payments = new StubPaymentRepository(payment);
        var initiations = new StubInitiationRepository(initiation);
        var processUri = new Uri("https://example.test/payment/process/123");
        var provider = new StubProviderInitiator(
            PaymentProvider.Csob,
            new PaymentProviderInitializationResult(
                PaymentProvider.Csob,
                "PAY-123",
                processUri));
        var service = CreateService(
            payments,
            initiations,
            provider);

        var outcome = await service.InitializeAsync(payment.Id);

        Assert.Same(payment, outcome.Payment);
        Assert.Equal(PaymentStatus.Pending, payment.Status);
        Assert.Equal("PAY-123", payment.ProviderReference);
        Assert.Equal(PaymentInitiationState.Initialized, initiation.State);
        Assert.Equal(processUri, initiation.ProcessUri);
        Assert.Equal(processUri, outcome.ProcessUri);
        Assert.Equal(1, provider.InitializeCalls);
        Assert.Equal(1, provider.VerifyCalls);
        Assert.Equal(1, payments.SaveCalls);
        Assert.Equal(3, initiations.SaveCalls);

        var request = Assert.IsType<PaymentProviderInitializationRequest>(
            provider.LastRequest);
        Assert.Equal(initiation.OrderNumber, request.OrderNumber);
        Assert.Equal(
            PaymentProviderCorrelation.Encode(
                payment.Id,
                initiation.CorrelationId),
            request.CorrelationData);
    }

    [Fact]
    public async Task InitializeAsync_ProviderFailureLeavesPaymentCreatedAndMarksUncertain()
    {
        var payment = CreatePayment(PaymentProvider.Csob);
        var initiation = CreateInitiation(payment);
        var payments = new StubPaymentRepository(payment);
        var initiations = new StubInitiationRepository(initiation);
        var provider = new StubProviderInitiator(
            PaymentProvider.Csob,
            new HttpRequestException("gateway unavailable"));
        var service = CreateService(
            payments,
            initiations,
            provider);

        await Assert.ThrowsAsync<HttpRequestException>(
            () => service.InitializeAsync(payment.Id));

        Assert.Equal(PaymentStatus.Created, payment.Status);
        Assert.Null(payment.ProviderReference);
        Assert.Equal(PaymentInitiationState.Uncertain, initiation.State);
        Assert.NotNull(initiation.LastError);
        Assert.Equal(1, provider.InitializeCalls);
        Assert.Equal(0, payments.SaveCalls);
        Assert.Equal(2, initiations.SaveCalls);
    }

    [Fact]
    public async Task InitializeIfPreparedAsync_UncertainStateDoesNotReplayProviderCall()
    {
        var payment = CreatePayment(PaymentProvider.Csob);
        var initiation = CreateInitiation(payment);
        initiation.Begin(TestTime);
        initiation.MarkUncertain("ambiguous", TestTime);
        var payments = new StubPaymentRepository(payment);
        var initiations = new StubInitiationRepository(initiation);
        var provider = new StubProviderInitiator(
            PaymentProvider.Csob,
            new InvalidOperationException("must not be called"));
        var service = CreateService(
            payments,
            initiations,
            provider);

        var outcome = await service.InitializeIfPreparedAsync(payment);

        Assert.Same(payment, outcome.Payment);
        Assert.Null(outcome.ProcessUri);
        Assert.Equal(0, provider.InitializeCalls);
        Assert.Equal(PaymentInitiationState.Uncertain, initiation.State);
    }

    [Fact]
    public async Task InitializeIfPreparedAsync_InProgressAfterCrashDoesNotReplayProviderCall()
    {
        var payment = CreatePayment(PaymentProvider.Csob);
        var initiation = CreateInitiation(payment);
        initiation.Begin(TestTime);
        var provider = new StubProviderInitiator(
            PaymentProvider.Csob,
            new InvalidOperationException("must not be called"));
        var service = CreateService(
            new StubPaymentRepository(payment),
            new StubInitiationRepository(initiation),
            provider);

        var outcome = await service.InitializeIfPreparedAsync(payment);

        Assert.Same(payment, outcome.Payment);
        Assert.Equal(PaymentInitiationState.InProgress, initiation.State);
        Assert.Equal(0, provider.InitializeCalls);
    }

    [Fact]
    public async Task InitializeAsync_LateProviderResultAfterStaleDispositionIsAppliedOnce()
    {
        var payment = CreatePayment(PaymentProvider.Csob);
        var initiation = CreateInitiation(payment);
        var processUri = new Uri("https://example.test/process/late");
        var provider = new StubProviderInitiator(
            PaymentProvider.Csob,
            new PaymentProviderInitializationResult(
                PaymentProvider.Csob,
                "PAY-LATE",
                processUri),
            () => initiation.MarkUncertain(
                "stale in-progress",
                TestTime));
        var service = CreateService(
            new StubPaymentRepository(payment),
            new StubInitiationRepository(initiation),
            provider);

        var outcome = await service.InitializeAsync(payment.Id);

        Assert.Equal(1, provider.InitializeCalls);
        Assert.Equal(PaymentStatus.Pending, payment.Status);
        Assert.Equal("PAY-LATE", payment.ProviderReference);
        Assert.Equal(PaymentInitiationState.Initialized, initiation.State);
        Assert.Equal(processUri, outcome.ProcessUri);
    }

    [Fact]
    public async Task InitializeAsync_LateConflictingPayIdIsNotOverwritten()
    {
        var payment = CreatePayment(PaymentProvider.Csob);
        var initiation = CreateInitiation(payment);
        var provider = new StubProviderInitiator(
            PaymentProvider.Csob,
            new PaymentProviderInitializationResult(
                PaymentProvider.Csob,
                "PAY-LATE",
                new Uri("https://example.test/process/late")),
            () => initiation.MarkUncertain(
                "stale in-progress",
                TestTime,
                "PAY-OTHER",
                new Uri("https://example.test/process/other")));
        var service = CreateService(
            new StubPaymentRepository(payment),
            new StubInitiationRepository(initiation),
            provider);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.InitializeAsync(payment.Id));

        Assert.Equal(PaymentStatus.Created, payment.Status);
        Assert.Null(payment.ProviderReference);
        Assert.Equal("PAY-OTHER", initiation.ObservedProviderReference);
        Assert.Equal(PaymentInitiationState.Uncertain, initiation.State);
    }

    [Fact]
    public async Task InitializeAsync_UncertainProbeFailurePersistsKnownCandidate()
    {
        var payment = CreatePayment(PaymentProvider.Csob);
        var initiation = CreateInitiation(payment);
        var observed = new PaymentProviderInitializationResult(
            PaymentProvider.Csob,
            "PAY-KNOWN",
            new Uri("https://example.test/process/known"));
        var provider = new StubProviderInitiator(
            PaymentProvider.Csob,
            new PaymentProviderInitializationUncertainException(
                observed,
                "status probe failed"));
        var service = CreateService(
            new StubPaymentRepository(payment),
            new StubInitiationRepository(initiation),
            provider);

        await Assert.ThrowsAsync<
            PaymentProviderInitializationUncertainException>(
            () => service.InitializeAsync(payment.Id));

        Assert.Equal(PaymentInitiationState.Uncertain, initiation.State);
        Assert.Equal("PAY-KNOWN", initiation.ObservedProviderReference);
        Assert.Equal(observed.ProcessUri, initiation.ObservedProcessUri);
        Assert.Equal(PaymentStatus.Created, payment.Status);
    }

    [Fact]
    public async Task InitializeAsync_PersistsCandidateBeforeStatusVerification()
    {
        var payment = CreatePayment(PaymentProvider.Csob);
        var initiation = CreateInitiation(payment);
        var initiations = new StubInitiationRepository(initiation);
        var candidate = new PaymentProviderInitializationResult(
            PaymentProvider.Csob,
            "PAY-DURABLE",
            new Uri("https://example.test/process/durable"));
        var provider = new StubProviderInitiator(
            PaymentProvider.Csob,
            candidate,
            beforeVerify: () =>
            {
                Assert.Equal(2, initiations.SaveCalls);
                Assert.Equal(
                    PaymentInitiationState.Uncertain,
                    initiation.State);
                Assert.Equal(
                    candidate.ProviderReference,
                    initiation.ObservedProviderReference);
            },
            verifyException: new HttpRequestException("status unavailable"));
        var service = CreateService(
            new StubPaymentRepository(payment),
            initiations,
            provider);

        await Assert.ThrowsAsync<HttpRequestException>(
            () => service.InitializeAsync(payment.Id));

        Assert.Equal(1, provider.InitializeCalls);
        Assert.Equal(1, provider.VerifyCalls);
        Assert.Equal(PaymentStatus.Created, payment.Status);
        Assert.Equal(
            PaymentInitiationState.Uncertain,
            initiation.State);
        Assert.Equal("PAY-DURABLE", initiation.ObservedProviderReference);

        var replay = await service.InitializeIfPreparedAsync(payment);

        Assert.Same(payment, replay.Payment);
        Assert.Equal(1, provider.InitializeCalls);
        Assert.Equal(1, provider.VerifyCalls);
    }

    [Fact]
    public async Task InitializeIfPreparedAsync_InitializedStateReturnsPersistedProcessUri()
    {
        var payment = CreatePayment(PaymentProvider.Csob);
        var processUri = new Uri("https://example.test/process/PAY-456");
        payment.MarkPending("PAY-456", TestTime);
        var initiation = CreateInitiation(payment);
        initiation.Begin(TestTime);
        initiation.Complete(TestTime, processUri);
        var payments = new StubPaymentRepository(payment);
        var initiations = new StubInitiationRepository(initiation);
        var provider = new StubProviderInitiator(
            PaymentProvider.Csob,
            new InvalidOperationException("must not be called"));
        var service = CreateService(
            payments,
            initiations,
            provider);

        var outcome = await service.InitializeIfPreparedAsync(payment);

        Assert.Same(payment, outcome.Payment);
        Assert.Equal(processUri, outcome.ProcessUri);
        Assert.Equal(0, provider.InitializeCalls);
    }

    private static PaymentInitiationService CreateService(
        IPaymentRepository payments,
        IPaymentInitiationRepository initiations,
        IPaymentProviderInitiator provider)
    {
        return new PaymentInitiationService(
            payments,
            initiations,
            provider,
            new ImmediateTransaction(),
            new FixedTimeProvider(TestTime),
            NullAuditTrail.Instance);
    }

    private static Payment CreatePayment(PaymentProvider provider)
    {
        return new Payment(
            Guid.NewGuid(),
            Guid.NewGuid(),
            PaymentPurposeType.CreditTopUp,
            jobId: null,
            new Money(25_000),
            provider,
            TestTime,
            Guid.NewGuid());
    }

    private static PaymentInitiation CreateInitiation(Payment payment)
    {
        return new PaymentInitiation(
            payment.Id,
            payment.Provider,
            12345,
            Guid.NewGuid(),
            TestTime);
    }

    private sealed class StubPaymentRepository : IPaymentRepository
    {
        private readonly Payment _payment;

        public StubPaymentRepository(Payment payment)
        {
            _payment = payment;
        }

        public int SaveCalls { get; private set; }

        public Task<Payment?> FindByIdAsync(
            Guid paymentId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<Payment?>(
                paymentId == _payment.Id ? _payment : null);

        public Task<Payment?> FindBlockingForJobAsync(
            Guid jobId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<Payment?>(null);

        public Task<Payment?> FindByProviderReferenceAsync(
            PaymentProvider provider,
            string providerReference,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<Payment?>(null);

        public Task<Payment?> FindByCreationRequestIdAsync(
            Guid creationRequestId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<Payment?>(null);

        public Task AddAsync(
            Payment payment,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task AddPreparedAsync(
            Payment payment,
            PaymentInitiation initiation,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task SaveAsync(
            Payment payment,
            CancellationToken cancellationToken = default)
        {
            SaveCalls++;
            return Task.CompletedTask;
        }
    }

    private sealed class StubInitiationRepository :
        IPaymentInitiationRepository
    {
        private readonly PaymentInitiation _initiation;

        public StubInitiationRepository(PaymentInitiation initiation)
        {
            _initiation = initiation;
        }

        public int SaveCalls { get; private set; }

        public Task<PaymentInitiation?> FindByPaymentIdAsync(
            Guid paymentId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<PaymentInitiation?>(
                paymentId == _initiation.PaymentId
                    ? _initiation
                    : null);

        public Task SaveAsync(
            PaymentInitiation initiation,
            CancellationToken cancellationToken = default)
        {
            Assert.Same(_initiation, initiation);
            SaveCalls++;
            return Task.CompletedTask;
        }
    }

    private sealed class StubProviderInitiator : IPaymentProviderInitiator
    {
        private readonly PaymentProviderInitializationResult? _result;
        private readonly Exception? _exception;
        private readonly Action? _beforeReturn;
        private readonly Action? _beforeVerify;
        private readonly Exception? _verifyException;

        public StubProviderInitiator(
            PaymentProvider provider,
            PaymentProviderInitializationResult result,
            Action? beforeReturn = null,
            Action? beforeVerify = null,
            Exception? verifyException = null)
        {
            Provider = provider;
            _result = result;
            _beforeReturn = beforeReturn;
            _beforeVerify = beforeVerify;
            _verifyException = verifyException;
        }

        public StubProviderInitiator(
            PaymentProvider provider,
            Exception exception)
        {
            Provider = provider;
            _exception = exception;
        }

        public PaymentProvider Provider { get; }

        public int InitializeCalls { get; private set; }

        public int VerifyCalls { get; private set; }

        public PaymentProviderInitializationRequest? LastRequest
        {
            get;
            private set;
        }

        public void EnsureAvailable()
        {
        }

        public Task<PaymentProviderInitializationResult> InitializeAsync(
            PaymentProviderInitializationRequest request,
            CancellationToken cancellationToken = default)
        {
            InitializeCalls++;
            LastRequest = request;

            if (_exception is not null)
            {
                return Task.FromException<PaymentProviderInitializationResult>(
                    _exception);
            }

            _beforeReturn?.Invoke();

            return Task.FromResult(
                _result ?? throw new InvalidOperationException());
        }

        public Task VerifyAsync(
            PaymentProviderInitializationResult candidate,
            CancellationToken cancellationToken = default)
        {
            VerifyCalls++;
            _beforeVerify?.Invoke();

            return _verifyException is null
                ? Task.CompletedTask
                : Task.FromException(_verifyException);
        }
    }

    private sealed class ImmediateTransaction : IApplicationTransaction
    {
        public Task<T> ExecuteAsync<T>(
            Func<CancellationToken, Task<T>> operation,
            CancellationToken cancellationToken = default) =>
            operation(cancellationToken);
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _time;

        public FixedTimeProvider(DateTimeOffset time)
        {
            _time = time;
        }

        public override DateTimeOffset GetUtcNow() => _time;
    }
}
