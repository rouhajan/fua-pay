using FuaPay.Web.BuildingBlocks.Auditing;
using FuaPay.Web.BuildingBlocks.Persistence;
using FuaPay.Web.Modules.Payments.Application;
using FuaPay.Web.Modules.Payments.Domain;
using FuaPay.Web.Modules.Payments.Infrastructure.Csob;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FuaPay.DatabaseTests;

public sealed class PaymentInitiationRacePersistenceTests :
    IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly DateTimeOffset CreatedAt =
        new(2026, 8, 14, 12, 0, 0, TimeSpan.Zero);
    private static readonly Uri TrustedProcessUri =
        new("https://example.test/process/payrace000001");

    private readonly WebApplicationFactory<Program> _factory;

    public PaymentInitiationRacePersistenceTests(
        WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Theory]
    [InlineData(LateEvidenceVariant.Same)]
    [InlineData(LateEvidenceVariant.ConflictingPayId)]
    [InlineData(LateEvidenceVariant.ConflictingProcessUri)]
    public async Task LateCandidateAndStaleWorker_PreserveFirstEvidenceAndRemainRecoverable(
        LateEvidenceVariant variant)
    {
        var coordinator = new RaceCoordinator();
        await using var factory = CreateCoordinatedFactory(coordinator);
        var payment = CreatePayment();
        var initiation = new PaymentInitiation(
            payment.Id,
            PaymentProvider.Csob,
            9_200_000_001L + (int)variant,
            Guid.NewGuid(),
            CreatedAt);
        var candidate = new PaymentProviderInitializationResult(
            PaymentProvider.Csob,
            "payrace000001",
            TrustedProcessUri);
        var provider = new LateCandidateProvider(candidate);
        var timeProvider = new MutableTimeProvider(
            CreatedAt.AddSeconds(1));

        try
        {
            using (var createScope = factory.Services.CreateScope())
            {
                await createScope.ServiceProvider
                    .GetRequiredService<IPaymentRepository>()
                    .AddPreparedAsync(payment, initiation);
            }

            using var responseScope = factory.Services.CreateScope();
            var responseServices = responseScope.ServiceProvider;
            var responseTask = new PaymentInitiationService(
                    responseServices.GetRequiredService<IPaymentRepository>(),
                    responseServices.GetRequiredService<
                        IPaymentInitiationRepository>(),
                    provider,
                    responseServices.GetRequiredService<
                        FuaPay.Web.BuildingBlocks.Application.IApplicationTransaction>(),
                    timeProvider,
                    responseServices.GetRequiredService<IAuditTrail>())
                .InitializeAsync(payment.Id);

            await provider.RequestReached.WaitAsync(
                TimeSpan.FromSeconds(10));

            using var staleScope = factory.Services.CreateScope();
            var staleTask = staleScope.ServiceProvider
                .GetRequiredService<ICsobPaymentRecoveryRepository>()
                .RegisterStaleInProgressAsync(
                    CreatedAt.AddSeconds(2),
                    CreatedAt.AddSeconds(2),
                    limit: 20);

            await coordinator.StaleAuditStaged.WaitAsync(
                TimeSpan.FromSeconds(10));
            timeProvider.SetUtcNow(CreatedAt.AddSeconds(3));
            provider.ReleaseResponse();
            await coordinator.CandidateAuditStaged.WaitAsync(
                TimeSpan.FromSeconds(10));
            coordinator.ReleaseStaleWorker();

            Assert.Equal(1, await staleTask);
            await Assert.ThrowsAsync<
                PaymentProviderInitializationUncertainException>(
                () => responseTask);
            Assert.Equal(1, provider.InitializeCalls);

            await ApplyAdditionalEvidenceAsync(
                factory,
                payment.Id,
                variant);

            using (var firstScheduleScope = factory.Services.CreateScope())
            using (var secondScheduleScope = factory.Services.CreateScope())
            {
                var scheduled = await Task.WhenAll(
                    firstScheduleScope.ServiceProvider
                        .GetRequiredService<ICsobPaymentRecoveryRepository>()
                        .ScheduleRecoverableUncertainAsync(
                            CreatedAt.AddSeconds(5),
                            limit: 20),
                    secondScheduleScope.ServiceProvider
                        .GetRequiredService<ICsobPaymentRecoveryRepository>()
                        .ScheduleRecoverableUncertainAsync(
                            CreatedAt.AddSeconds(5),
                            limit: 20));

                Assert.Equal(1, scheduled.Sum());
            }

            using (var auditScope = factory.Services.CreateScope())
            {
                var auditCount = await auditScope.ServiceProvider
                    .GetRequiredService<FuaPayDbContext>()
                    .Database.SqlQuery<int>(
                        $"""
                        SELECT COUNT(*)::int AS "Value"
                        FROM audit.events
                        WHERE
                            action =
                                'payment.provider-initiation.verification-scheduled'
                            AND entity_type = 'payment'
                            AND entity_id = {payment.Id.ToString()}
                        """)
                    .SingleAsync();

                Assert.Equal(1, auditCount);
            }

            using var verifyScope = factory.Services.CreateScope();
            var persistedPayment = Assert.IsType<Payment>(
                await verifyScope.ServiceProvider
                    .GetRequiredService<IPaymentRepository>()
                    .FindByIdAsync(payment.Id));
            var persistedInitiation = Assert.IsType<PaymentInitiation>(
                await verifyScope.ServiceProvider
                    .GetRequiredService<IPaymentInitiationRepository>()
                    .FindByPaymentIdAsync(payment.Id));
            var claim = Assert.Single(
                await verifyScope.ServiceProvider
                    .GetRequiredService<ICsobPaymentRecoveryRepository>()
                    .ClaimDueAsync(
                        CreatedAt.AddSeconds(5),
                        TimeSpan.FromMinutes(2),
                        limit: 20));

            Assert.Equal(PaymentStatus.Created, persistedPayment.Status);
            Assert.Equal(
                PaymentInitiationState.Uncertain,
                persistedInitiation.State);
            Assert.Equal(
                candidate.ProviderReference,
                persistedInitiation.ObservedProviderReference);
            Assert.Equal(
                TrustedProcessUri,
                persistedInitiation.ObservedProcessUri);
            Assert.Equal(payment.Id, claim.PaymentId);
            Assert.Equal(candidate.ProviderReference, claim.ProviderReference);
            Assert.Equal(1, provider.InitializeCalls);
        }
        finally
        {
            coordinator.ReleaseStaleWorker();
            await DeletePaymentAsync(factory, payment.Id);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task InitialRecoveryAuditFailure_RollsBackLifecycleTransition(
        bool staleInProgress)
    {
        var payment = CreatePayment();
        var initiation = new PaymentInitiation(
            payment.Id,
            PaymentProvider.Csob,
            staleInProgress ? 9_200_000_010L : 9_200_000_011L,
            Guid.NewGuid(),
            CreatedAt);
        var duplicateAudit = AuditEntry.ForProcess(
            "database-test",
            "test.audit-duplicate",
            "test",
            Guid.NewGuid().ToString(),
            "Audit row used to force transactional rollback.",
            CreatedAt);

        try
        {
            using (var createScope = _factory.Services.CreateScope())
            {
                await createScope.ServiceProvider
                    .GetRequiredService<IPaymentRepository>()
                    .AddPreparedAsync(payment, initiation);
            }

            using (var stateScope = _factory.Services.CreateScope())
            {
                var repository = stateScope.ServiceProvider
                    .GetRequiredService<IPaymentInitiationRepository>();
                var persisted = Assert.IsType<PaymentInitiation>(
                    await repository.FindByPaymentIdAsync(payment.Id));
                persisted.Begin(CreatedAt.AddSeconds(1));
                await repository.SaveAsync(persisted);

                if (!staleInProgress)
                {
                    persisted = Assert.IsType<PaymentInitiation>(
                        await repository.FindByPaymentIdAsync(payment.Id));
                    persisted.MarkUncertain(
                        "provider outcome unknown",
                        CreatedAt.AddSeconds(2));
                    await repository.SaveAsync(persisted);
                }
            }

            using (var auditScope = _factory.Services.CreateScope())
            {
                await auditScope.ServiceProvider
                    .GetRequiredService<IAuditTrail>()
                    .WriteAsync(duplicateAudit);
            }

            await using var failingFactory =
                CreateFailingAuditFactory(duplicateAudit);
            using (var recoveryScope = failingFactory.Services.CreateScope())
            {
                var repository = recoveryScope.ServiceProvider
                    .GetRequiredService<ICsobPaymentRecoveryRepository>();

                if (staleInProgress)
                {
                    await Assert.ThrowsAsync<DbUpdateException>(
                        () => repository.RegisterStaleInProgressAsync(
                            CreatedAt.AddSeconds(2),
                            CreatedAt.AddSeconds(3),
                            limit: 20));
                }
                else
                {
                    await Assert.ThrowsAsync<DbUpdateException>(
                        () => repository.RegisterUnrecoverableUncertainAsync(
                            CreatedAt.AddSeconds(3),
                            limit: 20));
                }
            }

            using var verifyScope = _factory.Services.CreateScope();
            var restored = Assert.IsType<PaymentInitiation>(
                await verifyScope.ServiceProvider
                    .GetRequiredService<IPaymentInitiationRepository>()
                    .FindByPaymentIdAsync(payment.Id));
            var recoveries = await verifyScope.ServiceProvider
                .GetRequiredService<IPaymentReconciliationQueries>()
                .ListOpenAsync(100);

            Assert.Equal(
                staleInProgress
                    ? PaymentInitiationState.InProgress
                    : PaymentInitiationState.Uncertain,
                restored.State);
            Assert.DoesNotContain(
                recoveries,
                item => item.PaymentId == payment.Id);
        }
        finally
        {
            using (var auditCleanupScope = _factory.Services.CreateScope())
            {
                await auditCleanupScope.ServiceProvider
                    .GetRequiredService<FuaPayDbContext>()
                    .Database.ExecuteSqlInterpolatedAsync(
                        $"DELETE FROM audit.events WHERE id = {duplicateAudit.Id}");
            }

            await DeletePaymentAsync(_factory, payment.Id);
        }
    }

    private WebApplicationFactory<Program> CreateCoordinatedFactory(
        RaceCoordinator coordinator)
    {
        return _factory.WithWebHostBuilder(
            builder => builder.ConfigureTestServices(
                services =>
                {
                    var descriptor = Assert.Single(
                        services,
                        item => item.ServiceType == typeof(IAuditTrail));
                    services.Remove(descriptor);
                    services.AddScoped<IAuditTrail>(
                        provider => new CoordinatingAuditTrail(
                            CreateOriginalAuditTrail(provider, descriptor),
                            coordinator));
                }));
    }

    private WebApplicationFactory<Program> CreateFailingAuditFactory(
        AuditEntry duplicateAudit)
    {
        return _factory.WithWebHostBuilder(
            builder => builder.ConfigureTestServices(
                services =>
                {
                    var descriptor = Assert.Single(
                        services,
                        item => item.ServiceType == typeof(IAuditTrail));
                    services.Remove(descriptor);
                    services.AddScoped<IAuditTrail>(
                        provider => new DuplicateAuditTrail(
                            CreateOriginalAuditTrail(provider, descriptor),
                            duplicateAudit));
                }));
    }

    private static IAuditTrail CreateOriginalAuditTrail(
        IServiceProvider provider,
        ServiceDescriptor descriptor)
    {
        if (descriptor.ImplementationInstance is IAuditTrail instance)
        {
            return instance;
        }

        if (descriptor.ImplementationFactory is not null)
        {
            return (IAuditTrail)descriptor.ImplementationFactory(provider);
        }

        return (IAuditTrail)ActivatorUtilities.CreateInstance(
            provider,
            descriptor.ImplementationType
                ?? throw new InvalidOperationException(
                    "Původní IAuditTrail registrace nemá implementaci."));
    }

    private static async Task ApplyAdditionalEvidenceAsync(
        WebApplicationFactory<Program> factory,
        Guid paymentId,
        LateEvidenceVariant variant)
    {
        using var scope = factory.Services.CreateScope();
        var repository = scope.ServiceProvider
            .GetRequiredService<IPaymentInitiationRepository>();
        var initiation = Assert.IsType<PaymentInitiation>(
            await repository.FindByPaymentIdAsync(paymentId));

        switch (variant)
        {
            case LateEvidenceVariant.Same:
                initiation.RecordObservedProviderResult(
                    "payrace000001",
                    TrustedProcessUri,
                    CreatedAt.AddSeconds(4));
                await repository.SaveAsync(initiation);
                break;
            case LateEvidenceVariant.ConflictingPayId:
                Assert.Throws<InvalidOperationException>(
                    () => initiation.RecordObservedProviderResult(
                        "payrace000002",
                        TrustedProcessUri,
                        CreatedAt.AddSeconds(4)));
                break;
            case LateEvidenceVariant.ConflictingProcessUri:
                Assert.Throws<InvalidOperationException>(
                    () => initiation.RecordObservedProviderResult(
                        "payrace000001",
                        new Uri(
                            "https://example.test/process/payrace-conflict"),
                        CreatedAt.AddSeconds(4)));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(variant));
        }
    }

    private static Payment CreatePayment() =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            PaymentPurposeType.CreditTopUp,
            jobId: null,
            new Money(50_000),
            PaymentProvider.Csob,
            CreatedAt,
            Guid.NewGuid());

    private static async Task DeletePaymentAsync(
        WebApplicationFactory<Program> factory,
        Guid paymentId)
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider
            .GetRequiredService<FuaPayDbContext>()
            .Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM payments.payments WHERE id = {paymentId}");
    }

    public enum LateEvidenceVariant
    {
        Same = 0,
        ConflictingPayId = 1,
        ConflictingProcessUri = 2
    }

    private sealed class LateCandidateProvider : IPaymentProviderInitiator
    {
        private readonly PaymentProviderInitializationResult _candidate;
        private readonly TaskCompletionSource _requestReached =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _releaseResponse =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public LateCandidateProvider(
            PaymentProviderInitializationResult candidate)
        {
            _candidate = candidate;
        }

        public PaymentProvider Provider => PaymentProvider.Csob;

        public Task RequestReached => _requestReached.Task;

        public int InitializeCalls { get; private set; }

        public void EnsureAvailable()
        {
        }

        public async Task<PaymentProviderInitializationResult> InitializeAsync(
            PaymentProviderInitializationRequest request,
            CancellationToken cancellationToken = default)
        {
            InitializeCalls++;
            _requestReached.TrySetResult();
            await _releaseResponse.Task.WaitAsync(cancellationToken);
            throw new PaymentProviderInitializationUncertainException(
                _candidate,
                "Test simulates a late signed candidate after the local timeout boundary.");
        }

        public Task VerifyAsync(
            PaymentProviderInitializationResult candidate,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException(
                "Uncertain initialization must not verify on the synchronous path.");

        public void ReleaseResponse() => _releaseResponse.TrySetResult();
    }

    private sealed class RaceCoordinator
    {
        private readonly TaskCompletionSource _staleAuditStaged =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _candidateAuditStaged =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly ManualResetEventSlim _releaseStale = new(false);

        public Task StaleAuditStaged => _staleAuditStaged.Task;

        public Task CandidateAuditStaged => _candidateAuditStaged.Task;

        public void OnStage(AuditEntry entry)
        {
            if (entry.Action == "payment.provider-initiation.stale")
            {
                _staleAuditStaged.TrySetResult();
                if (!_releaseStale.Wait(TimeSpan.FromSeconds(10)))
                {
                    throw new TimeoutException(
                        "Test neuvolnil stale worker v očekávaném čase.");
                }
            }
            else if (
                entry.Action ==
                "payment.provider-initiation.candidate-observed")
            {
                _candidateAuditStaged.TrySetResult();
            }
        }

        public void ReleaseStaleWorker() => _releaseStale.Set();
    }

    private sealed class CoordinatingAuditTrail : IAuditTrail
    {
        private readonly IAuditTrail _inner;
        private readonly RaceCoordinator _coordinator;

        public CoordinatingAuditTrail(
            IAuditTrail inner,
            RaceCoordinator coordinator)
        {
            _inner = inner;
            _coordinator = coordinator;
        }

        public void Stage(AuditEntry entry)
        {
            _coordinator.OnStage(entry);
            _inner.Stage(entry);
        }

        public Task WriteAsync(
            AuditEntry entry,
            CancellationToken cancellationToken = default) =>
            _inner.WriteAsync(entry, cancellationToken);
    }

    private sealed class DuplicateAuditTrail : IAuditTrail
    {
        private readonly IAuditTrail _inner;
        private readonly AuditEntry _duplicate;

        public DuplicateAuditTrail(
            IAuditTrail inner,
            AuditEntry duplicate)
        {
            _inner = inner;
            _duplicate = duplicate;
        }

        public void Stage(AuditEntry entry) => _inner.Stage(_duplicate);

        public Task WriteAsync(
            AuditEntry entry,
            CancellationToken cancellationToken = default) =>
            _inner.WriteAsync(_duplicate, cancellationToken);
    }

    private sealed class MutableTimeProvider : TimeProvider
    {
        private long _utcTicks;

        public MutableTimeProvider(DateTimeOffset initialValue)
        {
            SetUtcNow(initialValue);
        }

        public override DateTimeOffset GetUtcNow() =>
            new(Interlocked.Read(ref _utcTicks), TimeSpan.Zero);

        public void SetUtcNow(DateTimeOffset value) =>
            Interlocked.Exchange(ref _utcTicks, value.UtcTicks);
    }
}
