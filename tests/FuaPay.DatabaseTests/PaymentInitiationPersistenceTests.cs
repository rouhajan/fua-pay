using FuaPay.Web.BuildingBlocks.Application;
using FuaPay.Web.BuildingBlocks.Auditing;
using FuaPay.Web.BuildingBlocks.Persistence;
using FuaPay.Web.Modules.Payments.Application;
using FuaPay.Web.Modules.Payments.Domain;
using FuaPay.Web.Modules.Payments.Infrastructure.Csob;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FuaPay.DatabaseTests;

public sealed class PaymentInitiationPersistenceTests :
    IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly DateTimeOffset CreatedAt =
        new(2026, 8, 13, 16, 0, 0, TimeSpan.Zero);

    private readonly WebApplicationFactory<Program> _factory;

    public PaymentInitiationPersistenceTests(
        WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task AddPreparedAsync_PersistsPaymentAndInitiationTogether()
    {
        var payment = CreatePayment();
        var initiation = CreateInitiation(payment, 9_000_000_001L);

        try
        {
            using (var createScope = _factory.Services.CreateScope())
            {
                await createScope.ServiceProvider
                    .GetRequiredService<IPaymentRepository>()
                    .AddPreparedAsync(payment, initiation);
            }

            using var verifyScope = _factory.Services.CreateScope();
            var persistedPayment = Assert.IsType<Payment>(
                await verifyScope.ServiceProvider
                    .GetRequiredService<IPaymentRepository>()
                    .FindByIdAsync(payment.Id));
            var persistedInitiation = Assert.IsType<PaymentInitiation>(
                await verifyScope.ServiceProvider
                    .GetRequiredService<IPaymentInitiationRepository>()
                    .FindByPaymentIdAsync(payment.Id));

            Assert.Equal(PaymentStatus.Created, persistedPayment.Status);
            Assert.Equal(
                PaymentInitiationState.Prepared,
                persistedInitiation.State);
            Assert.Equal(
                initiation.OrderNumber,
                persistedInitiation.OrderNumber);
            Assert.Equal(
                initiation.CorrelationId,
                persistedInitiation.CorrelationId);
        }
        finally
        {
            await DeletePaymentAsync(payment.Id);
        }
    }

    [Fact]
    public async Task SaveAsync_UncertainWithSameTimestampObservation_RoundTrips()
    {
        var payment = CreatePayment(PaymentProvider.Csob);
        var initiation = CreateInitiation(payment, 9_000_000_003L);
        var processUri = new Uri(
            "https://example.test/payment/process/recovery");

        try
        {
            using (var createScope = _factory.Services.CreateScope())
            {
                await createScope.ServiceProvider
                    .GetRequiredService<IPaymentRepository>()
                    .AddPreparedAsync(payment, initiation);
            }

            using (var updateScope = _factory.Services.CreateScope())
            {
                var repository = updateScope.ServiceProvider
                    .GetRequiredService<IPaymentInitiationRepository>();
                var persisted = Assert.IsType<PaymentInitiation>(
                    await repository.FindByPaymentIdAsync(payment.Id));

                persisted.Begin(CreatedAt.AddSeconds(1));
                await repository.SaveAsync(persisted);

                persisted = Assert.IsType<PaymentInitiation>(
                    await repository.FindByPaymentIdAsync(payment.Id));
                persisted.MarkUncertain(
                    "local commit outcome unknown",
                    CreatedAt.AddSeconds(2),
                    "PAY-OBSERVED",
                    processUri);
                await repository.SaveAsync(persisted);
            }

            using var verifyScope = _factory.Services.CreateScope();
            var restored = Assert.IsType<PaymentInitiation>(
                await verifyScope.ServiceProvider
                    .GetRequiredService<IPaymentInitiationRepository>()
                    .FindByPaymentIdAsync(payment.Id));

            Assert.Equal(PaymentInitiationState.Uncertain, restored.State);
            Assert.Equal(
                "PAY-OBSERVED",
                restored.ObservedProviderReference);
            Assert.Equal(processUri, restored.ObservedProcessUri);
            Assert.Equal(CreatedAt.AddSeconds(2), restored.FinishedAt);
            Assert.Equal(restored.FinishedAt, restored.UpdatedAt);
        }
        finally
        {
            await DeletePaymentAsync(payment.Id);
        }
    }

    [Fact]
    public async Task SaveAsync_UncertainWithoutObservation_RoundTrips()
    {
        var payment = CreatePayment(PaymentProvider.Csob);
        var initiation = CreateInitiation(payment, 9_000_000_006L);

        try
        {
            using (var createScope = _factory.Services.CreateScope())
            {
                await createScope.ServiceProvider
                    .GetRequiredService<IPaymentRepository>()
                    .AddPreparedAsync(payment, initiation);
            }

            using (var updateScope = _factory.Services.CreateScope())
            {
                var repository = updateScope.ServiceProvider
                    .GetRequiredService<IPaymentInitiationRepository>();
                var persisted = Assert.IsType<PaymentInitiation>(
                    await repository.FindByPaymentIdAsync(payment.Id));
                persisted.Begin(CreatedAt.AddSeconds(1));
                await repository.SaveAsync(persisted);

                persisted = Assert.IsType<PaymentInitiation>(
                    await repository.FindByPaymentIdAsync(payment.Id));
                persisted.MarkUncertain(
                    "provider outcome unknown",
                    CreatedAt.AddSeconds(2));
                await repository.SaveAsync(persisted);
            }

            using var verifyScope = _factory.Services.CreateScope();
            var restored = Assert.IsType<PaymentInitiation>(
                await verifyScope.ServiceProvider
                    .GetRequiredService<IPaymentInitiationRepository>()
                    .FindByPaymentIdAsync(payment.Id));

            Assert.Equal(PaymentInitiationState.Uncertain, restored.State);
            Assert.Null(restored.ObservedProviderReference);
            Assert.Null(restored.ObservedProcessUri);
            Assert.Equal(CreatedAt.AddSeconds(2), restored.FinishedAt);
            Assert.Equal(restored.FinishedAt, restored.UpdatedAt);
        }
        finally
        {
            await DeletePaymentAsync(payment.Id);
        }
    }

    [Fact]
    public async Task SaveAsync_UncertainWithLaterObservation_PreservesFinishedAt()
    {
        var payment = CreatePayment(PaymentProvider.Csob);
        var initiation = CreateInitiation(payment, 9_000_000_007L);
        var processUri = new Uri(
            "https://example.test/payment/process/late-observation");

        try
        {
            using (var createScope = _factory.Services.CreateScope())
            {
                await createScope.ServiceProvider
                    .GetRequiredService<IPaymentRepository>()
                    .AddPreparedAsync(payment, initiation);
            }

            using (var updateScope = _factory.Services.CreateScope())
            {
                var repository = updateScope.ServiceProvider
                    .GetRequiredService<IPaymentInitiationRepository>();
                var persisted = Assert.IsType<PaymentInitiation>(
                    await repository.FindByPaymentIdAsync(payment.Id));
                persisted.Begin(CreatedAt.AddSeconds(1));
                await repository.SaveAsync(persisted);

                persisted = Assert.IsType<PaymentInitiation>(
                    await repository.FindByPaymentIdAsync(payment.Id));
                persisted.MarkUncertain(
                    "provider outcome unknown",
                    CreatedAt.AddSeconds(2));
                await repository.SaveAsync(persisted);

                persisted = Assert.IsType<PaymentInitiation>(
                    await repository.FindByPaymentIdAsync(payment.Id));
                persisted.RecordObservedProviderResult(
                    "paylateobs00001",
                    processUri,
                    CreatedAt.AddSeconds(3));
                await repository.SaveAsync(persisted);
            }

            using var verifyScope = _factory.Services.CreateScope();
            var restored = Assert.IsType<PaymentInitiation>(
                await verifyScope.ServiceProvider
                    .GetRequiredService<IPaymentInitiationRepository>()
                    .FindByPaymentIdAsync(payment.Id));

            Assert.Equal(PaymentInitiationState.Uncertain, restored.State);
            Assert.Equal("paylateobs00001", restored.ObservedProviderReference);
            Assert.Equal(processUri, restored.ObservedProcessUri);
            Assert.Equal(CreatedAt.AddSeconds(2), restored.FinishedAt);
            Assert.Equal(CreatedAt.AddSeconds(3), restored.UpdatedAt);
        }
        finally
        {
            await DeletePaymentAsync(payment.Id);
        }
    }

    [Fact]
    public async Task FindByPaymentIdAsync_UncertainWithoutObservationAndLaterUpdatedAt_FailsClosed()
    {
        var payment = CreatePayment(PaymentProvider.Csob);
        var initiation = CreateInitiation(payment, 9_000_000_008L);

        try
        {
            using (var createScope = _factory.Services.CreateScope())
            {
                await createScope.ServiceProvider
                    .GetRequiredService<IPaymentRepository>()
                    .AddPreparedAsync(payment, initiation);
            }

            using (var updateScope = _factory.Services.CreateScope())
            {
                var repository = updateScope.ServiceProvider
                    .GetRequiredService<IPaymentInitiationRepository>();
                var persisted = Assert.IsType<PaymentInitiation>(
                    await repository.FindByPaymentIdAsync(payment.Id));
                persisted.Begin(CreatedAt.AddSeconds(1));
                await repository.SaveAsync(persisted);

                persisted = Assert.IsType<PaymentInitiation>(
                    await repository.FindByPaymentIdAsync(payment.Id));
                persisted.MarkUncertain(
                    "provider outcome unknown",
                    CreatedAt.AddSeconds(2));
                await repository.SaveAsync(persisted);
            }

            using (var corruptScope = _factory.Services.CreateScope())
            {
                await corruptScope.ServiceProvider
                    .GetRequiredService<FuaPayDbContext>()
                    .Database.ExecuteSqlInterpolatedAsync(
                        $"""
                        UPDATE payments.payment_initiations
                        SET updated_at = {CreatedAt.AddSeconds(3)}
                        WHERE payment_id = {payment.Id}
                        """);
            }

            using var verifyScope = _factory.Services.CreateScope();
            await Assert.ThrowsAsync<InvalidDataException>(
                () => verifyScope.ServiceProvider
                    .GetRequiredService<IPaymentInitiationRepository>()
                    .FindByPaymentIdAsync(payment.Id));
        }
        finally
        {
            await DeletePaymentAsync(payment.Id);
        }
    }

    [Fact]
    public async Task RegisterStaleInProgressAsync_OlderObservationKeepsTimestampsConsistent()
    {
        var payment = CreatePayment(PaymentProvider.Csob);
        var initiation = CreateInitiation(payment, 9_000_000_009L);

        try
        {
            using (var createScope = _factory.Services.CreateScope())
            {
                await createScope.ServiceProvider
                    .GetRequiredService<IPaymentRepository>()
                    .AddPreparedAsync(payment, initiation);
            }

            using (var beginScope = _factory.Services.CreateScope())
            {
                var repository = beginScope.ServiceProvider
                    .GetRequiredService<IPaymentInitiationRepository>();
                var persisted = Assert.IsType<PaymentInitiation>(
                    await repository.FindByPaymentIdAsync(payment.Id));
                persisted.Begin(CreatedAt.AddSeconds(10));
                await repository.SaveAsync(persisted);
            }

            using (var staleScope = _factory.Services.CreateScope())
            {
                var changed = await staleScope.ServiceProvider
                    .GetRequiredService<ICsobPaymentRecoveryRepository>()
                    .RegisterStaleInProgressAsync(
                        CreatedAt.AddSeconds(20),
                        CreatedAt.AddSeconds(5),
                        limit: 20);
                Assert.Equal(1, changed);
            }

            using var verifyScope = _factory.Services.CreateScope();
            var restored = Assert.IsType<PaymentInitiation>(
                await verifyScope.ServiceProvider
                    .GetRequiredService<IPaymentInitiationRepository>()
                    .FindByPaymentIdAsync(payment.Id));

            Assert.Equal(PaymentInitiationState.Uncertain, restored.State);
            Assert.Equal(CreatedAt.AddSeconds(10), restored.FinishedAt);
            Assert.Equal(restored.FinishedAt, restored.UpdatedAt);
        }
        finally
        {
            await DeletePaymentAsync(payment.Id);
        }
    }

    [Fact]
    public async Task SaveAsync_ConcurrentInitiationUpdateIsRejected()
    {
        var payment = CreatePayment();
        var initiation = CreateInitiation(payment, 9_000_000_002L);

        try
        {
            using (var createScope = _factory.Services.CreateScope())
            {
                await createScope.ServiceProvider
                    .GetRequiredService<IPaymentRepository>()
                    .AddPreparedAsync(payment, initiation);
            }

            using var firstScope = _factory.Services.CreateScope();
            using var secondScope = _factory.Services.CreateScope();
            var firstRepository = firstScope.ServiceProvider
                .GetRequiredService<IPaymentInitiationRepository>();
            var secondRepository = secondScope.ServiceProvider
                .GetRequiredService<IPaymentInitiationRepository>();
            var first = Assert.IsType<PaymentInitiation>(
                await firstRepository.FindByPaymentIdAsync(payment.Id));
            var second = Assert.IsType<PaymentInitiation>(
                await secondRepository.FindByPaymentIdAsync(payment.Id));

            first.Begin(CreatedAt.AddSeconds(1));
            await firstRepository.SaveAsync(first);

            second.Begin(CreatedAt.AddSeconds(2));
            await Assert.ThrowsAsync<PaymentConcurrencyException>(
                () => secondRepository.SaveAsync(second));
        }
        finally
        {
            await DeletePaymentAsync(payment.Id);
        }
    }

    [Fact]
    public async Task InitializeAsync_StatusVerificationSeesCandidateInNewScope()
    {
        var payment = CreatePayment(PaymentProvider.Csob);
        var initiation = CreateInitiation(payment, 9_000_000_004L);
        var candidate = new PaymentProviderInitializationResult(
            PaymentProvider.Csob,
            "paydurable001",
            new Uri("https://example.test/process/paydurable001"));

        try
        {
            using (var createScope = _factory.Services.CreateScope())
            {
                await createScope.ServiceProvider
                    .GetRequiredService<IPaymentRepository>()
                    .AddPreparedAsync(payment, initiation);
            }

            var provider = new DatabaseObservingProvider(
                _factory,
                payment.Id,
                candidate);

            using (var serviceScope = _factory.Services.CreateScope())
            {
                var services = serviceScope.ServiceProvider;
                var service = new PaymentInitiationService(
                    services.GetRequiredService<IPaymentRepository>(),
                    services.GetRequiredService<IPaymentInitiationRepository>(),
                    provider,
                    services.GetRequiredService<IApplicationTransaction>(),
                    new FixedTimeProvider(CreatedAt.AddSeconds(10)),
                    services.GetRequiredService<IAuditTrail>());

                await service.InitializeAsync(payment.Id);
            }

            Assert.Equal(1, provider.VerifyCalls);

            using var verifyScope = _factory.Services.CreateScope();
            var persistedPayment = Assert.IsType<Payment>(
                await verifyScope.ServiceProvider
                    .GetRequiredService<IPaymentRepository>()
                    .FindByIdAsync(payment.Id));
            var persistedInitiation = Assert.IsType<PaymentInitiation>(
                await verifyScope.ServiceProvider
                    .GetRequiredService<IPaymentInitiationRepository>()
                    .FindByPaymentIdAsync(payment.Id));

            Assert.Equal(PaymentStatus.Pending, persistedPayment.Status);
            Assert.Equal(candidate.ProviderReference, persistedPayment.ProviderReference);
            Assert.Equal(
                PaymentInitiationState.Initialized,
                persistedInitiation.State);
        }
        finally
        {
            await DeletePaymentAsync(payment.Id);
        }
    }

    [Fact]
    public async Task LateCandidateAfterStaleCheckpoint_IsIdempotentAndRejectsConflict()
    {
        var payment = CreatePayment(PaymentProvider.Csob);
        var initiation = CreateInitiation(payment, 9_000_000_005L);
        var observedAt = CreatedAt.AddMinutes(2);

        try
        {
            using (var createScope = _factory.Services.CreateScope())
            {
                await createScope.ServiceProvider
                    .GetRequiredService<IPaymentRepository>()
                    .AddPreparedAsync(payment, initiation);
            }

            using (var beginScope = _factory.Services.CreateScope())
            {
                var repository = beginScope.ServiceProvider
                    .GetRequiredService<IPaymentInitiationRepository>();
                var persisted = Assert.IsType<PaymentInitiation>(
                    await repository.FindByPaymentIdAsync(payment.Id));
                persisted.Begin(CreatedAt.AddSeconds(1));
                await repository.SaveAsync(persisted);
            }

            using (var staleScope = _factory.Services.CreateScope())
            {
                Assert.Equal(
                    1,
                    await staleScope.ServiceProvider
                        .GetRequiredService<ICsobPaymentRecoveryRepository>()
                        .RegisterStaleInProgressAsync(
                            CreatedAt.AddSeconds(2),
                            CreatedAt.AddMinutes(1),
                            limit: 20));
            }

            using (var observationScope = _factory.Services.CreateScope())
            {
                var repository = observationScope.ServiceProvider
                    .GetRequiredService<IPaymentInitiationRepository>();
                var persisted = Assert.IsType<PaymentInitiation>(
                    await repository.FindByPaymentIdAsync(payment.Id));
                persisted.RecordObservedProviderResult(
                    "paylate000001",
                    new Uri("https://example.test/process/paylate000001"),
                    observedAt);
                await repository.SaveAsync(persisted);

                persisted = Assert.IsType<PaymentInitiation>(
                    await repository.FindByPaymentIdAsync(payment.Id));
                persisted.RecordObservedProviderResult(
                    "paylate000001",
                    new Uri("https://example.test/process/paylate000001"),
                    observedAt.AddSeconds(1));
                await repository.SaveAsync(persisted);

                persisted = Assert.IsType<PaymentInitiation>(
                    await repository.FindByPaymentIdAsync(payment.Id));
                Assert.Throws<InvalidOperationException>(
                    () => persisted.RecordObservedProviderResult(
                        "payconflict01",
                        processUri: null,
                        observedAt.AddSeconds(2)));
            }

            using var verifyScope = _factory.Services.CreateScope();
            var restored = Assert.IsType<PaymentInitiation>(
                await verifyScope.ServiceProvider
                    .GetRequiredService<IPaymentInitiationRepository>()
                    .FindByPaymentIdAsync(payment.Id));
            Assert.Equal("paylate000001", restored.ObservedProviderReference);
        }
        finally
        {
            await DeletePaymentAsync(payment.Id);
        }
    }

    private static Payment CreatePayment(
        PaymentProvider provider = PaymentProvider.Development)
    {
        return new Payment(
            Guid.NewGuid(),
            Guid.NewGuid(),
            PaymentPurposeType.CreditTopUp,
            jobId: null,
            new Money(50_000),
            provider,
            CreatedAt,
            Guid.NewGuid());
    }

    private static PaymentInitiation CreateInitiation(
        Payment payment,
        long orderNumber)
    {
        return new PaymentInitiation(
            payment.Id,
            payment.Provider,
            orderNumber,
            Guid.NewGuid(),
            CreatedAt);
    }

    private async Task DeletePaymentAsync(Guid paymentId)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider
            .GetRequiredService<FuaPayDbContext>();

        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            DELETE FROM payments.payments
            WHERE id = {paymentId}
            """);
    }

    private sealed class DatabaseObservingProvider :
        IPaymentProviderInitiator
    {
        private readonly WebApplicationFactory<Program> _factory;
        private readonly Guid _paymentId;
        private readonly PaymentProviderInitializationResult _candidate;

        public DatabaseObservingProvider(
            WebApplicationFactory<Program> factory,
            Guid paymentId,
            PaymentProviderInitializationResult candidate)
        {
            _factory = factory;
            _paymentId = paymentId;
            _candidate = candidate;
        }

        public PaymentProvider Provider => PaymentProvider.Csob;

        public int VerifyCalls { get; private set; }

        public void EnsureAvailable()
        {
        }

        public Task<PaymentProviderInitializationResult> InitializeAsync(
            PaymentProviderInitializationRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_candidate);

        public async Task VerifyAsync(
            PaymentProviderInitializationResult candidate,
            CancellationToken cancellationToken = default)
        {
            VerifyCalls++;
            using var scope = _factory.Services.CreateScope();
            var persisted = Assert.IsType<PaymentInitiation>(
                await scope.ServiceProvider
                    .GetRequiredService<IPaymentInitiationRepository>()
                    .FindByPaymentIdAsync(_paymentId, cancellationToken));

            Assert.Equal(PaymentInitiationState.Uncertain, persisted.State);
            Assert.Equal(
                candidate.ProviderReference,
                persisted.ObservedProviderReference);
        }
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;

        public FixedTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public override DateTimeOffset GetUtcNow() => _utcNow;
    }
}
