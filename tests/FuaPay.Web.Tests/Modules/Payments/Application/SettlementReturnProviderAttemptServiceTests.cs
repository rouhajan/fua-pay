using FuaPay.Web.Modules.Payments.Application;
using FuaPay.Web.Modules.Payments.Domain;

namespace FuaPay.Web.Tests.Modules.Payments.Application;

public sealed class SettlementReturnProviderAttemptServiceTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 29, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CreateCommand_DoesNotAcceptProviderOrProviderReference()
    {
        var properties = typeof(CreateSettlementReturnProviderAttemptCommand)
            .GetProperties()
            .Select(property => property.Name)
            .ToArray();

        Assert.Equal(
            ["AttemptId", "SettlementReturnId", "Operation"],
            properties);
    }

    [Fact]
    public async Task CreateAsync_DerivesProviderIdentityFromOriginalPayment()
    {
        var fixture = new Fixture(completePayment: false);
        fixture.Payment.MarkPending("  authoritative-reference  ", Now);
        fixture.Payment.Complete(Now);

        var result = await fixture.Service.CreateAsync(
            fixture.Command(SettlementReturnProviderOperation.Reverse));

        Assert.True(result.Created);
        Assert.Equal(PaymentProvider.Csob, result.Attempt.Provider);
        Assert.Equal(
            "authoritative-reference",
            result.Attempt.ProviderReference);
        Assert.Equal(
            SettlementReturnProviderAttemptState.Prepared,
            result.Attempt.State);
    }

    [Fact]
    public async Task CreateAsync_CreditJobOrTerminalReturnIsRejected()
    {
        var creditFixture = new Fixture(
            kind: SettlementReturnKind.CreditJob);
        var terminalFixture = new Fixture();
        terminalFixture.SettlementReturn.Begin(Now);
        terminalFixture.SettlementReturn.Complete(Now);

        await Assert.ThrowsAsync<
            SettlementReturnProviderAttemptNotAllowedException>(
                () => creditFixture.Service.CreateAsync(
                    creditFixture.Command(
                        SettlementReturnProviderOperation.Refund)));
        await Assert.ThrowsAsync<
            SettlementReturnProviderAttemptNotAllowedException>(
                () => terminalFixture.Service.CreateAsync(
                    terminalFixture.Command(
                        SettlementReturnProviderOperation.Refund)));
    }

    [Fact]
    public async Task CreateAsync_UnsettledPaymentIsRejected()
    {
        var fixture = new Fixture(completePayment: false);

        await Assert.ThrowsAsync<
            SettlementReturnProviderAttemptNotAllowedException>(
                () => fixture.Service.CreateAsync(
                    fixture.Command(
                        SettlementReturnProviderOperation.Refund)));

        Assert.Empty(fixture.AttemptRepository.Stored);
    }

    [Theory]
    [InlineData(SourceMismatch.Customer)]
    [InlineData(SourceMismatch.Amount)]
    [InlineData(SourceMismatch.CardJobPurpose)]
    [InlineData(SourceMismatch.CardJobJobId)]
    [InlineData(SourceMismatch.CardTopUpPurposeAndJob)]
    public async Task CreateAsync_MismatchedOriginalPaymentIsRejected(
        SourceMismatch mismatch)
    {
        var fixture = mismatch switch
        {
            SourceMismatch.Customer => new Fixture(
                settlementReturnCustomerUserId: Guid.NewGuid()),
            SourceMismatch.Amount => new Fixture(
                settlementReturnAmount: new Money(12_346)),
            SourceMismatch.CardJobPurpose => new Fixture(
                kind: SettlementReturnKind.CardJob,
                paymentPurposeType: PaymentPurposeType.CreditTopUp),
            SourceMismatch.CardJobJobId => new Fixture(
                kind: SettlementReturnKind.CardJob,
                paymentJobId: Guid.NewGuid()),
            SourceMismatch.CardTopUpPurposeAndJob => new Fixture(
                paymentPurposeType: PaymentPurposeType.Job),
            _ => throw new ArgumentOutOfRangeException(nameof(mismatch))
        };

        await Assert.ThrowsAsync<
            SettlementReturnProviderAttemptNotAllowedException>(
                () => fixture.Service.CreateAsync(
                    fixture.Command(
                        SettlementReturnProviderOperation.Refund)));

        Assert.Empty(fixture.AttemptRepository.Stored);
    }

    [Fact]
    public async Task CreateAsync_SameAttemptReplaysButConflictIsRejected()
    {
        var fixture = new Fixture();
        var command = fixture.Command(
            SettlementReturnProviderOperation.Reverse);

        var created = await fixture.Service.CreateAsync(command);
        var replayed = await fixture.Service.CreateAsync(command);

        Assert.True(created.Created);
        Assert.False(replayed.Created);
        Assert.Same(created.Attempt, replayed.Attempt);
        await Assert.ThrowsAsync<
            SettlementReturnProviderAttemptConflictException>(
                () => fixture.Service.CreateAsync(
                    command with
                    {
                        Operation =
                            SettlementReturnProviderOperation.Refund
                    }));
    }

    [Fact]
    public async Task CreateAsync_UncertainAttemptBlocksAnotherAttempt()
    {
        var fixture = new Fixture();
        var first = await fixture.Service.CreateAsync(
            fixture.Command(SettlementReturnProviderOperation.Reverse));
        await fixture.Service.BeginAsync(first.Attempt.Id);
        await fixture.Service.MarkUncertainAsync(
            first.Attempt.Id,
            "connection timeout");

        var exception = await Assert.ThrowsAsync<
            SettlementReturnProviderAttemptAlreadyActiveException>(
                () => fixture.Service.CreateAsync(
                    fixture.Command(
                        SettlementReturnProviderOperation.Refund,
                        Guid.NewGuid())));

        Assert.Equal(first.Attempt.Id, exception.ActiveAttemptId);
        Assert.Single(fixture.AttemptRepository.Stored);
    }

    [Fact]
    public async Task CreateAsync_SequentialReverseThenRefundPreservesHistory()
    {
        var fixture = new Fixture();
        var reverse = await fixture.Service.CreateAsync(
            fixture.Command(SettlementReturnProviderOperation.Reverse));
        await fixture.Service.BeginAsync(reverse.Attempt.Id);
        await fixture.Service.RejectAsync(
            reverse.Attempt.Id,
            "payment was already settled");

        var refund = await fixture.Service.CreateAsync(
            fixture.Command(
                SettlementReturnProviderOperation.Refund,
                Guid.NewGuid()));
        var history = await fixture.AttemptRepository
            .ListBySettlementReturnIdAsync(
                fixture.SettlementReturn.Id);

        Assert.Equal(2, history.Count);
        Assert.Equal(
            SettlementReturnProviderOperation.Reverse,
            history[0].Operation);
        Assert.Equal(
            SettlementReturnProviderAttemptState.Rejected,
            history[0].State);
        Assert.Equal(
            SettlementReturnProviderOperation.Refund,
            history[1].Operation);
        Assert.Equal(refund.Attempt.Id, history[1].Id);
    }

    [Fact]
    public async Task CreateAsync_ConfirmedAttemptPermanentlyClosesSequence()
    {
        var fixture = new Fixture();
        var confirmed = await fixture.Service.CreateAsync(
            fixture.Command(SettlementReturnProviderOperation.Reverse));
        await fixture.Service.BeginAsync(confirmed.Attempt.Id);
        await fixture.Service.ConfirmAsync(confirmed.Attempt.Id);

        await Assert.ThrowsAsync<
            SettlementReturnProviderAttemptNotAllowedException>(
                () => fixture.Service.CreateAsync(
                    fixture.Command(
                        SettlementReturnProviderOperation.Refund,
                        Guid.NewGuid())));

        Assert.Single(fixture.AttemptRepository.Stored);
    }

    [Fact]
    public async Task CreateAsync_ConcurrentActiveConstraintIsTranslated()
    {
        var fixture = new Fixture();
        var winning = new SettlementReturnProviderAttempt(
            Guid.NewGuid(),
            fixture.SettlementReturn.Id,
            fixture.Payment.Provider,
            SettlementReturnProviderOperation.Reverse,
            fixture.Payment.ProviderReference!,
            Now);
        fixture.AttemptRepository.ConcurrentActiveOnAdd = winning;

        var exception = await Assert.ThrowsAsync<
            SettlementReturnProviderAttemptAlreadyActiveException>(
                () => fixture.Service.CreateAsync(
                    fixture.Command(
                        SettlementReturnProviderOperation.Refund)));

        Assert.Equal(winning.Id, exception.ActiveAttemptId);
    }

    [Fact]
    public async Task MarkUncertainAsync_RestartReadCannotReturnToPrepared()
    {
        var fixture = new Fixture();
        var created = await fixture.Service.CreateAsync(
            fixture.Command(SettlementReturnProviderOperation.Refund));
        await fixture.Service.BeginAsync(created.Attempt.Id);
        await fixture.Service.MarkUncertainAsync(
            created.Attempt.Id,
            "response was not received");

        var reloaded = await fixture.AttemptRepository.FindByIdAsync(
            created.Attempt.Id);

        Assert.NotNull(reloaded);
        Assert.Equal(
            SettlementReturnProviderAttemptState.Uncertain,
            reloaded.State);
        Assert.Throws<
            InvalidSettlementReturnProviderAttemptStateTransitionException>(
                () => reloaded.Begin(Now));
    }

    private sealed class Fixture
    {
        public Fixture(
            SettlementReturnKind kind = SettlementReturnKind.CardTopUp,
            bool completePayment = true,
            Guid? settlementReturnCustomerUserId = null,
            Money? settlementReturnAmount = null,
            PaymentPurposeType? paymentPurposeType = null,
            Guid? paymentJobId = null)
        {
            var customerUserId = Guid.NewGuid();
            var amount = new Money(12_345);
            Guid? settlementReturnJobId =
                kind is SettlementReturnKind.CardJob or
                    SettlementReturnKind.CreditJob
                    ? Guid.NewGuid()
                    : null;
            var purposeType = paymentPurposeType ??
                (kind == SettlementReturnKind.CardJob
                    ? PaymentPurposeType.Job
                    : PaymentPurposeType.CreditTopUp);
            Guid? resolvedPaymentJobId =
                purposeType == PaymentPurposeType.Job
                ? paymentJobId ?? settlementReturnJobId ?? Guid.NewGuid()
                : null;

            Payment = new Payment(
                Guid.NewGuid(),
                customerUserId,
                purposeType,
                resolvedPaymentJobId,
                amount,
                PaymentProvider.Csob,
                Now,
                purposeType == PaymentPurposeType.CreditTopUp
                    ? Guid.NewGuid()
                    : null);

            if (completePayment)
            {
                Payment.MarkPending("authoritative-reference", Now);
                Payment.Complete(Now);
            }

            SettlementReturn = new SettlementReturn(
                Guid.NewGuid(),
                Guid.NewGuid(),
                kind,
                kind == SettlementReturnKind.CreditJob
                    ? null
                    : Payment.Id,
                settlementReturnJobId,
                settlementReturnCustomerUserId ?? Payment.CustomerUserId,
                Guid.NewGuid(),
                settlementReturnAmount ?? Payment.Amount,
                "Administrative reason",
                Now);

            AttemptRepository = new FakeAttemptRepository();
            Service = new SettlementReturnProviderAttemptService(
                AttemptRepository,
                new FakeReturnRepository(SettlementReturn),
                new FakePaymentRepository(Payment),
                new FixedTimeProvider(Now));
        }

        public Payment Payment { get; }

        public SettlementReturn SettlementReturn { get; }

        public FakeAttemptRepository AttemptRepository { get; }

        public SettlementReturnProviderAttemptService Service { get; }

        public CreateSettlementReturnProviderAttemptCommand Command(
            SettlementReturnProviderOperation operation,
            Guid? attemptId = null) =>
            new(
                attemptId ?? Guid.NewGuid(),
                SettlementReturn.Id,
                operation);
    }

    public enum SourceMismatch
    {
        Customer,
        Amount,
        CardJobPurpose,
        CardJobJobId,
        CardTopUpPurposeAndJob
    }

    private sealed class FakeAttemptRepository :
        ISettlementReturnProviderAttemptRepository
    {
        public List<SettlementReturnProviderAttempt> Stored { get; } = [];

        public SettlementReturnProviderAttempt? ConcurrentActiveOnAdd
        {
            get;
            set;
        }

        public Task<SettlementReturnProviderAttempt?> FindByIdAsync(
            Guid attemptId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(
                Stored.SingleOrDefault(item => item.Id == attemptId));

        public Task<SettlementReturnProviderAttempt?>
            FindActiveBySettlementReturnIdAsync(
                Guid settlementReturnId,
                CancellationToken cancellationToken = default)
        {
            var stored = Stored.SingleOrDefault(item =>
                item.SettlementReturnId == settlementReturnId &&
                item.IsActive);

            return Task.FromResult(stored);
        }

        public Task<IReadOnlyList<SettlementReturnProviderAttempt>>
            ListBySettlementReturnIdAsync(
                Guid settlementReturnId,
                CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<
                SettlementReturnProviderAttempt>>(
                    Stored
                        .Where(item =>
                            item.SettlementReturnId == settlementReturnId)
                        .ToArray());

        public Task AddAsync(
            SettlementReturnProviderAttempt attempt,
            CancellationToken cancellationToken = default)
        {
            if (ConcurrentActiveOnAdd is not null)
            {
                Stored.Add(ConcurrentActiveOnAdd);
                throw new SettlementReturnProviderAttemptAlreadyActiveException(
                    attempt.SettlementReturnId);
            }

            Stored.Add(attempt);
            return Task.CompletedTask;
        }

        public Task SaveAsync(
            SettlementReturnProviderAttempt attempt,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class FakeReturnRepository : ISettlementReturnRepository
    {
        private readonly SettlementReturn _settlementReturn;

        public FakeReturnRepository(SettlementReturn settlementReturn)
        {
            _settlementReturn = settlementReturn;
        }

        public Task<SettlementReturn?> FindByIdAsync(
            Guid settlementReturnId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<SettlementReturn?>(
                settlementReturnId == _settlementReturn.Id
                    ? _settlementReturn
                    : null);

        public Task<SettlementReturn?> FindByRequestIdAsync(
            Guid requestId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<SettlementReturn?>(null);

        public Task<SettlementReturn?> FindByOriginalPaymentIdAsync(
            Guid originalPaymentId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<SettlementReturn?>(null);

        public Task<SettlementReturn?> FindByJobIdAsync(
            Guid jobId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<SettlementReturn?>(null);

        public Task AddAsync(
            SettlementReturn settlementReturn,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task SaveAsync(
            SettlementReturn settlementReturn,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class FakePaymentRepository : IPaymentRepository
    {
        private readonly Payment _payment;

        public FakePaymentRepository(Payment payment)
        {
            _payment = payment;
        }

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
            Task.CompletedTask;

        public Task AddPreparedAsync(
            Payment payment,
            PaymentInitiation initiation,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task SaveAsync(
            Payment payment,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now;

        public FixedTimeProvider(DateTimeOffset now)
        {
            _now = now;
        }

        public override DateTimeOffset GetUtcNow() => _now;
    }
}
