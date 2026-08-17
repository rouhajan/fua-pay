using FuaPay.Web.BuildingBlocks.Auditing;
using FuaPay.Web.BuildingBlocks.Domain;
using FuaPay.Web.Modules.Payments.Application;
using FuaPay.Web.Modules.Payments.Domain;

namespace FuaPay.Web.Tests.Modules.Payments.Application;

public sealed class DevelopmentPaymentServiceTests
{
    private static readonly DateTimeOffset TestTime =
        new(2026, 8, 3, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task CompleteAsync_UsesProviderNeutralSettlementBoundary()
    {
        var payment = CreatePendingPayment(
            PaymentProvider.Development,
            "DEV-BOUNDARY");
        var settlement = new RecordingSettlementService();
        var service = CreateService(payment, settlement);

        var changed = await service.CompleteAsync(
            payment.CustomerUserId,
            payment.Id);

        Assert.True(changed);

        var confirmation = Assert.IsType<
            VerifiedPaymentConfirmation>(
            settlement.Confirmation);

        Assert.Equal(payment.Provider, confirmation.Provider);
        Assert.Equal(
            payment.ProviderReference,
            confirmation.ProviderReference);
        Assert.Equal(payment.Amount, confirmation.Amount);
    }

    [Fact]
    public async Task CompleteAsync_RejectsNonDevelopmentProvider()
    {
        var payment = CreatePendingPayment(
            PaymentProvider.Csob,
            "CSOB-BOUNDARY");
        var settlement = new RecordingSettlementService();
        var service = CreateService(payment, settlement);

        await Assert.ThrowsAsync<
            DevelopmentPaymentProviderMismatchException>(
            () => service.CompleteAsync(
                payment.CustomerUserId,
                payment.Id));

        Assert.Null(settlement.Confirmation);
    }

    private static DevelopmentPaymentService CreateService(
        Payment payment,
        IPaymentSettlementService settlementService)
    {
        return new DevelopmentPaymentService(
            new StubPaymentRepository(payment),
            settlementService,
            new FixedTimeProvider(TestTime),
            NullAuditTrail.Instance,
            new DevelopmentPaymentAvailability(true));
    }

    private static Payment CreatePendingPayment(
        PaymentProvider provider,
        string providerReference)
    {
        var payment = new Payment(
            Guid.NewGuid(),
            Guid.NewGuid(),
            PaymentPurposeType.CreditTopUp,
            jobId: null,
            new Money(25_000),
            provider,
            TestTime,
            Guid.NewGuid());

        payment.MarkPending(providerReference, TestTime);
        return payment;
    }

    private sealed class RecordingSettlementService :
        IPaymentSettlementService
    {
        public VerifiedPaymentConfirmation? Confirmation
        {
            get;
            private set;
        }

        public Task<bool> CompleteAsync(
            VerifiedPaymentConfirmation confirmation,
            CancellationToken cancellationToken = default)
        {
            Confirmation = confirmation;
            return Task.FromResult(true);
        }
    }

    private sealed class StubPaymentRepository : IPaymentRepository
    {
        private readonly Payment _payment;

        internal StubPaymentRepository(Payment payment)
        {
            _payment = payment;
        }

        public Task<Payment?> FindByIdAsync(
            Guid paymentId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<Payment?>(
                paymentId == _payment.Id
                    ? _payment
                    : null);
        }

        public Task<Payment?> FindBlockingForJobAsync(
            Guid jobId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<Payment?>(null);
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
            return Task.FromResult<Payment?>(null);
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
            throw new NotSupportedException();
        }

        public Task SaveAsync(
            Payment payment,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;

        internal FixedTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public override DateTimeOffset GetUtcNow()
        {
            return _utcNow;
        }
    }
}
