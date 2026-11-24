using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;

using FuaPay.Web.BuildingBlocks.Application;
using FuaPay.Web.BuildingBlocks.Auditing;
using FuaPay.Web.BuildingBlocks.Persistence;
using FuaPay.Web.Modules.Access.Application;
using FuaPay.Web.Modules.Access.Domain;
using FuaPay.Web.Modules.Credits.Application;
using FuaPay.Web.Modules.Credits.Domain;
using FuaPay.Web.Modules.FinancialDocuments.Application;
using FuaPay.Web.Modules.FinancialDocuments.Domain;
using FuaPay.Web.Modules.Jobs.Application;
using FuaPay.Web.Modules.Jobs.Domain;
using FuaPay.Web.Modules.Payments.Application;
using FuaPay.Web.Modules.Payments.Domain;
using FuaPay.Web.Modules.Payments.Infrastructure.Csob;
using FuaPay.Web.Modules.ServiceUnits.Application;
using FuaPay.Web.Modules.ServiceUnits.Domain;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using Xunit.Abstractions;

namespace FuaPay.DatabaseTests;

public sealed class CsobInitializationReconciliationRacePersistenceTests :
    IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly DateTimeOffset AttemptAt =
        new(2026, 11, 18, 11, 25, 17, TimeSpan.Zero);
    private static readonly TimeSpan BarrierTimeout = TimeSpan.FromSeconds(10);
    private static readonly CsobReconciliationConfiguration Reconciliation = new(
        Enabled: true,
        PollInterval: TimeSpan.FromSeconds(15),
        PendingMinimumAge: TimeSpan.FromMinutes(30),
        LeaseDuration: TimeSpan.FromMinutes(3),
        BaseBackoff: TimeSpan.FromSeconds(15),
        MaximumBackoff: TimeSpan.FromMinutes(3),
        MaximumAttempts: 14,
        BatchSize: 20);
    private static readonly CsobGatewayConfiguration Gateway = new(
        Enabled: true,
        ApiBaseUri: new Uri("https://csob.example.test/"),
        MerchantId: "M1MIPS0000",
        PrivateKeyPath: string.Empty,
        GatewayPublicKeyPath: string.Empty,
        ReturnUri: new Uri("https://fuapay.example.test/payments/csob/return"),
        PaymentTtlSeconds: 900,
        RequestTimeout: TimeSpan.FromSeconds(30));

    private readonly WebApplicationFactory<Program> _factory;
    private readonly ITestOutputHelper _output;

    public CsobInitializationReconciliationRacePersistenceTests(
        WebApplicationFactory<Program> factory,
        ITestOutputHelper output)
    {
        _factory = factory;
        _output = output;
    }

    [Theory]
    [InlineData(PaymentPurposeType.CreditTopUp)]
    [InlineData(PaymentPurposeType.Job)]
    public Task ConcurrentWebInitialization_WithSamePayId_KeepsRecoveryScheduled(
        PaymentPurposeType purpose) =>
        RunRaceAsync(purpose, conflictingStatusPayId: false);

    [Fact]
    public Task ConcurrentWebInitialization_WithConflictingSignedPayId_RequiresAttention() =>
        RunRaceAsync(PaymentPurposeType.CreditTopUp, conflictingStatusPayId: true);

    [Fact]
    public Task WebCommitsBetweenRecoveryReads_WithSamePayId_KeepsRecoveryScheduled() =>
        RunRaceAsync(
            PaymentPurposeType.Job,
            conflictingStatusPayId: false,
            completeBetweenRecoveryReads: true);

    [Fact]
    public Task WebCommitsBetweenPreStatusReads_WithSamePayId_KeepsRecoveryScheduled() =>
        RunUncoveredRaceAsync(PaymentPurposeType.Job, gatewayPaymentStatus: 1, PaymentStatus.Pending);

    [Theory]
    [InlineData(PaymentPurposeType.CreditTopUp, 7)]
    [InlineData(PaymentPurposeType.Job, 8)]
    public Task WebCommitsBeforePaidStatus_ReturnsExactlyOneSettlement(
        PaymentPurposeType purpose,
        int gatewayPaymentStatus) =>
        RunUncoveredRaceAsync(purpose, gatewayPaymentStatus, PaymentStatus.Succeeded);

    [Fact]
    public Task WebCommitsBeforeAuthoritativeExpiry_ExpiresWithoutFinancialEffects() =>
        RunUncoveredRaceAsync(PaymentPurposeType.Job, gatewayPaymentStatus: 6, PaymentStatus.Expired);

    [Theory]
    [InlineData("missing-reference", 1)]
    [InlineData("different-reference", 1)]
    [InlineData("different-provider", 1)]
    [InlineData("different-payment", 1)]
    [InlineData("still-mixed", 2)]
    public async Task RejectsInvalidPreStatusBinding(string conflict, int paymentReads)
    {
        var payment = CreateBindingTestPayment();
        const string payId = "bindingtest";
        using var scope = _factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        var reads = new RecoveryReadBarrier(
            services.GetRequiredService<IPaymentRepository>(),
            services.GetRequiredService<IPaymentInitiationRepository>(),
            services.GetRequiredService<FuaPayDbContext>().Database,
            payment.Id);
        var initiationView = new BindingTestInitiationRepository(reads)
        {
            ReadOverride = CreateBindingTestInitiation(payment, payId, conflict)
        };
        using var gateway = new SignedGatewayHandler(payId, payId, allowInitialization: false);
        gateway.ReleaseStatusResponse();
        using var http = new HttpClient(gateway, disposeHandler: false) { BaseAddress = Gateway.ApiBaseUri };
        var reconciliation = CreateBindingTestReconciliation(services, http, gateway, initiationView, reads);

        try
        {
            await AddPreparedPaymentAsync(payment);
            await Assert.ThrowsAsync<CsobPaymentRequiresAttentionException>(
                () => reconciliation.ReconcileAsync(payment.Id, payId));
            Assert.Equal(paymentReads, reads.PaymentReadCount);
            Assert.Equal(0, gateway.StatusRequests);
            Assert.Equal(0, gateway.InitializationRequests);
            await AssertPaymentAsync(services, payment.Id, PaymentStatus.Created,
                PaymentInitiationState.Prepared, providerReference: null, observedReference: null);
            await AssertSettlementEffectsAsync(services, payment);
        }
        finally
        {
            await DeleteScenarioAsync(payment, useEfForPayment: true);
        }
    }

    [Theory]
    [InlineData(0, 7, "still-uncertain")]
    [InlineData(0, 7, "different-reference")]
    [InlineData(0, 7, "different-provider")]
    [InlineData(0, 8, "different-payment")]
    [InlineData(130, 6, "different-provider")]
    [InlineData(130, 6, "already-succeeded")]
    [InlineData(0, 2, "different-reference")]
    [InlineData(0, 4, "different-provider")]
    [InlineData(0, 3, "different-payment")]
    [InlineData(0, 6, "inconsistent-initiation")]
    [InlineData(0, 3, "already-succeeded")]
    [InlineData(0, 6, "already-succeeded")]
    public async Task RejectsSignedStatusWithIncompatibleCurrentBinding(
        int resultCode,
        int gatewayStatus,
        string conflict)
    {
        var payment = CreateBindingTestPayment();
        var payId = payment.Id.ToString("N")[..15];
        using var scope = _factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        var initiationView = new BindingTestInitiationRepository(
            services.GetRequiredService<IPaymentInitiationRepository>());
        using var gateway = new SignedGatewayHandler(payId, payId, allowInitialization: false, resultCode, gatewayStatus);
        using var http = new HttpClient(gateway, disposeHandler: false) { BaseAddress = Gateway.ApiBaseUri };
        using var cancellation = new CancellationTokenSource();
        var reconciliation = CreateBindingTestReconciliation(services, http, gateway, initiationView);
        Task<CsobPaymentReconciliationResult>? attempt = null;

        try
        {
            if (conflict == "already-succeeded")
            {
                await AddSettlementDependenciesAsync(payment.CustomerUserId, serviceUnitId: null);
            }

            await AddPreparedPaymentAsync(payment);
            using var webScope = _factory.Services.CreateScope();
            var web = webScope.ServiceProvider;
            var initiations = web.GetRequiredService<IPaymentInitiationRepository>();
            var payments = web.GetRequiredService<IPaymentRepository>();
            var initiation = Assert.IsType<PaymentInitiation>(await initiations.FindByPaymentIdAsync(payment.Id, cancellation.Token));
            initiation.Begin(AttemptAt);
            initiation.MarkUncertain("Synthetic pending initialization", AttemptAt, payId);
            await initiations.SaveAsync(initiation, cancellation.Token);

            attempt = reconciliation.ReconcileAsync(payment.Id, payId, cancellation.Token);
            await gateway.WaitForStatusRequestAsync(attempt);
            var expectedStatus = PaymentStatus.Created;
            string? expectedReference = null;
            if (conflict != "still-uncertain")
            {
                expectedStatus = PaymentStatus.Pending;
                var initializedPayId = conflict == "different-reference" ? "otherbinding" : payId;
                expectedReference = initializedPayId;
                var currentPayment = Assert.IsType<Payment>(await payments.FindByIdAsync(payment.Id, cancellation.Token));
                await web.GetRequiredService<IApplicationTransaction>().ExecuteAsync(async ct =>
                {
                    currentPayment.MarkPending(initializedPayId, AttemptAt);
                    initiation.RecoverObservedInitialization(AttemptAt);
                    await payments.SaveAsync(currentPayment, ct);
                    await initiations.SaveAsync(initiation, ct);
                    return true;
                }, cancellation.Token);

                if (conflict is "different-provider" or "different-payment")
                {
                    initiationView.ReadOverride = CreateBindingTestInitiation(payment, payId, conflict, initialized: true);
                }
                if (conflict == "inconsistent-initiation")
                {
                    initiationView.ReadOverride = CreateBindingTestInitiation(payment, payId, conflict);
                }
                if (conflict == "already-succeeded")
                {
                    Assert.True(await web.GetRequiredService<IPaymentSettlementService>().CompleteAsync(
                        new VerifiedPaymentConfirmation(PaymentProvider.Csob, payId, payment.Amount), cancellation.Token));
                    expectedStatus = PaymentStatus.Succeeded;
                }
            }

            gateway.ReleaseStatusResponse();
            var error = await Assert.ThrowsAsync<CsobPaymentRequiresAttentionException>(
                () => attempt.WaitAsync(BarrierTimeout));
            Assert.Equal(resultCode, error.ResultCode);
            Assert.Equal(gatewayStatus, error.GatewayPaymentStatus);
            Assert.Equal(1, gateway.StatusResponses);
            Assert.Equal(0, gateway.InitializationRequests);
            await AssertPaymentAsync(web, payment.Id, expectedStatus, initiation.State,
                expectedReference, initiation.ObservedProviderReference);
            var persisted = Assert.IsType<Payment>(await payments.FindByIdAsync(payment.Id, cancellation.Token));
            await AssertSettlementEffectsAsync(web, persisted);
        }
        finally
        {
            await cancellation.CancelAsync();
            if (attempt is not null)
            {
                await ((Task)attempt).ConfigureAwait(
                    ConfigureAwaitOptions.ContinueOnCapturedContext | ConfigureAwaitOptions.SuppressThrowing);
            }
            await DeleteUncoveredScenarioAsync(payment, serviceUnitId: null);
        }
    }

    private static Payment CreateBindingTestPayment() =>
        new(Guid.NewGuid(), Guid.NewGuid(), PaymentPurposeType.CreditTopUp, null,
            new Money(9_300), PaymentProvider.Csob, AttemptAt, creationRequestId: Guid.NewGuid());

    private static PaymentInitiation CreateBindingTestInitiation(
        Payment payment,
        string payId,
        string conflict,
        bool initialized = false)
    {
        var initiation = new PaymentInitiation(
            conflict == "different-payment" ? Guid.NewGuid() : payment.Id,
            conflict == "different-provider" ? PaymentProvider.Development : PaymentProvider.Csob,
            1,
            Guid.NewGuid(),
            AttemptAt);
        initiation.Begin(AttemptAt);
        if (initialized || conflict == "still-mixed")
        {
            initiation.Complete(AttemptAt);
        }
        else
        {
            var reference = conflict switch
            {
                "missing-reference" => null,
                "different-reference" => "otherbinding",
                _ => payId
            };
            initiation.MarkUncertain("Synthetic initiation snapshot", AttemptAt, reference);
        }
        return initiation;
    }

    private static CsobPaymentReconciliationService CreateBindingTestReconciliation(
        IServiceProvider services,
        HttpClient http,
        ICsobGatewaySignature signature,
        IPaymentInitiationRepository initiations,
        IPaymentRepository? payments = null)
    {
        var clock = new FixedTimeProvider(AttemptAt);
        return new CsobPaymentReconciliationService(
            new CsobGatewayClient(http, Gateway, new CsobGatewayAvailability(IsEnabled: true), signature, clock),
            payments ?? services.GetRequiredService<IPaymentRepository>(),
            initiations,
            services.GetRequiredService<ICsobVerifiedReturnEvidenceReader>(),
            services.GetRequiredService<ICsobExpiryCorrectionGuard>(),
            services.GetRequiredService<IPaymentSettlementService>(),
            services.GetRequiredService<IApplicationTransaction>(),
            clock,
            services.GetRequiredService<IAuditTrail>());
    }

    private sealed class BindingTestInitiationRepository(IPaymentInitiationRepository inner) : IPaymentInitiationRepository
    {
        public PaymentInitiation? ReadOverride { get; set; }

        public Task<PaymentInitiation?> FindByPaymentIdAsync(Guid paymentId, CancellationToken cancellationToken) =>
            ReadOverride is { } snapshot
                ? Task.FromResult<PaymentInitiation?>(snapshot)
                : inner.FindByPaymentIdAsync(paymentId, cancellationToken);

        public Task SaveAsync(PaymentInitiation initiation, CancellationToken cancellationToken) =>
            inner.SaveAsync(initiation, cancellationToken);
    }

    [Theory]
    [InlineData(2, PaymentStatus.Pending, PaymentReconciliationState.Scheduled)]
    [InlineData(4, PaymentStatus.Pending, PaymentReconciliationState.Scheduled)]
    [InlineData(3, PaymentStatus.Cancelled, PaymentReconciliationState.Completed)]
    [InlineData(6, PaymentStatus.Failed, PaymentReconciliationState.Completed)]
    public Task WebCommitsWhileStatusResponseIsHeld_UsesInitializedPaymentLifecycle(
        int gatewayStatus,
        PaymentStatus expectedPayment,
        PaymentReconciliationState expectedRecovery) =>
        RunWithHeldStatusResponseAsync(gatewayStatus, expectedPayment, expectedRecovery, completeWeb: true);

    [Theory]
    [InlineData(3)]
    [InlineData(6)]
    public Task NoWebCommitWhileStatusResponseIsHeld_LeavesUncertainPaymentOpen(int gatewayStatus) =>
        RunWithHeldStatusResponseAsync(
            gatewayStatus, PaymentStatus.Created, PaymentReconciliationState.RequiresAttention, completeWeb: false);

    private async Task RunWithHeldStatusResponseAsync(
        int gatewayStatus,
        PaymentStatus expectedPayment,
        PaymentReconciliationState expectedRecovery,
        bool completeWeb)
    {
        var payment = new Payment(
            Guid.NewGuid(), Guid.NewGuid(), PaymentPurposeType.Job, Guid.NewGuid(),
            new Money(9_300), PaymentProvider.Csob, AttemptAt);
        var payId = payment.Id.ToString("N")[..15];
        using var webGateway = new SignedGatewayHandler(payId, payId, allowInitialization: true);
        using var workerGateway = new SignedGatewayHandler(
            payId, payId, allowInitialization: false, paymentStatus: gatewayStatus);
        using var webHttp = new HttpClient(webGateway, disposeHandler: false) { BaseAddress = Gateway.ApiBaseUri };
        using var workerHttp = new HttpClient(workerGateway, disposeHandler: false) { BaseAddress = Gateway.ApiBaseUri };
        using var cancellation = new CancellationTokenSource();
        using var webScope = _factory.Services.CreateScope();
        using var workerScope = _factory.Services.CreateScope();
        var clock = new FixedTimeProvider(AttemptAt);
        var availability = new CsobGatewayAvailability(IsEnabled: true);
        var webClient = new CsobGatewayClient(webHttp, Gateway, availability, webGateway, clock);
        var workerClient = new CsobGatewayClient(workerHttp, Gateway, availability, workerGateway, clock);
        var webServices = webScope.ServiceProvider;
        var webInitiation = new PaymentInitiationService(
            webServices.GetRequiredService<IPaymentRepository>(),
            webServices.GetRequiredService<IPaymentInitiationRepository>(),
            new CsobPaymentProviderInitiator(webClient, availability, Gateway),
            webServices.GetRequiredService<IApplicationTransaction>(),
            clock,
            webServices.GetRequiredService<IAuditTrail>());
        var worker = CreateWorker(workerScope.ServiceProvider, workerClient, clock);

        Task<PaymentInitializationOutcome>? webTask = null;
        Task<CsobPaymentRecoveryCycleResult>? workerTask = null;
        try
        {
            await AddPreparedPaymentAsync(payment);
            webTask = webInitiation.InitializeAsync(payment.Id, cancellation.Token);
            await webGateway.WaitForStatusRequestAsync(webTask);
            workerTask = worker.RunOnceAsync(cancellation.Token);
            await workerGateway.WaitForStatusRequestAsync(workerTask);
            Assert.False(webTask.IsCompleted);
            Assert.Equal(1, workerGateway.StatusRequests);
            Assert.Equal(0, workerGateway.StatusResponses);
            using var verification = _factory.Services.CreateScope();
            var services = verification.ServiceProvider;
            var beforeCommit = await ReadHeldStatusBindingAsync(services, payment.Id, initializedPayId: null, observedPayId: payId);
            Assert.Equal(PaymentStatus.Created, beforeCommit.Status);
            _output.WriteLine("1. Worker reached payment/status with Created/Uncertain and its reply was blocked.");

            if (completeWeb)
            {
                webGateway.ReleaseStatusResponse();
                Assert.True((await webTask.WaitAsync(BarrierTimeout)).CompletedByCurrentRequest);
                var committed = await ReadHeldStatusBindingAsync(services, payment.Id, initializedPayId: payId, observedPayId: null);
                Assert.Equal(PaymentStatus.Pending, committed.Status);
                _output.WriteLine("2. Independent scope verified web commit Pending/Initialized with the same payId.");
            }
            else
            {
                _output.WriteLine("2. Web's reply remains held; there is no web commit.");
            }

            Assert.Equal(0, workerGateway.StatusResponses);
            workerGateway.ReleaseStatusResponse();
            var cycle = await workerTask.WaitAsync(BarrierTimeout);
            Assert.Equal(1, workerGateway.StatusRequests);
            Assert.Equal(1, workerGateway.StatusResponses);
            Assert.Equal(1, webGateway.InitializationRequests);
            Assert.Equal(0, workerGateway.InitializationRequests);
            _output.WriteLine($"3. Worker received signed 0/{gatewayStatus}. One worker status GET, one original web init, zero worker init.");
            var persisted = await ReadHeldStatusBindingAsync(
                services, payment.Id, completeWeb ? payId : null, completeWeb ? null : payId);
            Assert.NotEqual(PaymentStatus.Succeeded, persisted.Status);
            await AssertNoFinancialEffectsAsync(services, persisted);
            await AssertSettlementEffectsAsync(services, persisted);

            var db = services.GetRequiredService<FuaPayDbContext>().Database;
            var recovery = await db.SqlQuery<HeldStatusRecoveryRow>(
                $"""
                SELECT state AS "State", provider_reference AS "ProviderReference", attempt_count AS "AttemptCount",
                       last_result_code AS "ResultCode", last_gateway_payment_status AS "GatewayStatus",
                       lease_token AS "LeaseToken", completed_at AS "CompletedAt",
                       verified_expiry_observed_at AS "VerifiedExpiryObservedAt"
                FROM payments.csob_payment_reconciliation
                WHERE payment_id = {payment.Id}
                """).SingleAsync();
            var audit = await db.SqlQuery<string>(
                $"""
                SELECT action AS "Value"
                FROM audit.events
                WHERE entity_type = 'payment' AND entity_id = {payment.Id.ToString()}
                """).ToListAsync();
            var actualRecovery = (PaymentReconciliationState)recovery.State;
            _output.WriteLine($"Payment={persisted.Status}, recovery={actualRecovery}, bank={recovery.ResultCode}/{recovery.GatewayStatus}, " +
                $"failure provenance={persisted.FailureProvenance?.ToString() ?? "none"}, " +
                $"claimed={cycle.ClaimedCount}, completed={cycle.CompletedCount}, rescheduled={cycle.RescheduledCount}, attention={cycle.RequiresAttentionCount}.");
            _output.WriteLine($"Audit actions: {string.Join(", ", audit.Order())}");
            Assert.Single(audit, action => action == "payment.provider-initiation.candidate-observed");
            Assert.Equal(completeWeb ? 1 : 0, audit.Count(action => action == "payment.provider-initialized"));
            Assert.Equal(payId, recovery.ProviderReference);
            Assert.Equal(1, recovery.AttemptCount);
            Assert.Equal(0, recovery.ResultCode);
            Assert.Equal(gatewayStatus, recovery.GatewayStatus);
            Assert.Null(recovery.LeaseToken);
            Assert.Null(recovery.VerifiedExpiryObservedAt);
            Assert.Equal(1, cycle.ClaimedCount);
            Assert.Equal(0, cycle.LostClaimCount);

            Assert.True(
                persisted.Status == expectedPayment && actualRecovery == expectedRecovery,
                $"Signed 0/{gatewayStatus}: expected payment/recovery {expectedPayment}/{expectedRecovery}; " +
                $"actual {persisted.Status}/{actualRecovery}.");
            Assert.Equal(expectedPayment == PaymentStatus.Failed ? PaymentFailureProvenance.CsobResult0Status6 : null,
                persisted.FailureProvenance);
            Assert.Equal(expectedRecovery == PaymentReconciliationState.Completed ? AttemptAt : null, recovery.CompletedAt);
            Assert.Equal(expectedRecovery == PaymentReconciliationState.Completed ? 1 : 0, cycle.CompletedCount);
            Assert.Equal(expectedRecovery == PaymentReconciliationState.Scheduled ? 1 : 0, cycle.RescheduledCount);
            Assert.Equal(expectedRecovery == PaymentReconciliationState.RequiresAttention ? 1 : 0, cycle.RequiresAttentionCount);
            Assert.Equal(expectedPayment == PaymentStatus.Cancelled ? 1 : 0, audit.Count(action => action == "payment.cancelled"));
            Assert.Equal(expectedPayment == PaymentStatus.Failed ? 1 : 0, audit.Count(action => action == "payment.failed"));
            Assert.DoesNotContain("payment.expired", audit);
            Assert.Equal(expectedRecovery == PaymentReconciliationState.Completed ? 1 : 0,
                audit.Count(action => action == "payment.reconciliation.completed"));
            Assert.Equal(expectedRecovery == PaymentReconciliationState.Scheduled ? 1 : 0,
                audit.Count(action => action == "payment.reconciliation.retry-scheduled"));
            Assert.Equal(expectedRecovery == PaymentReconciliationState.RequiresAttention ? 1 : 0,
                audit.Count(action => action == "payment.reconciliation.requires-attention"));
        }
        finally
        {
            await cancellation.CancelAsync();
            await Task.WhenAll(
                    (Task?)webTask ?? Task.CompletedTask,
                    (Task?)workerTask ?? Task.CompletedTask)
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext | ConfigureAwaitOptions.SuppressThrowing);
            await DeleteUncoveredScenarioAsync(payment, serviceUnitId: null);
        }
    }

    private static async Task<Payment> ReadHeldStatusBindingAsync(
        IServiceProvider services,
        Guid paymentId,
        string? initializedPayId,
        string? observedPayId)
    {
        var payment = Assert.IsType<Payment>(
            await services.GetRequiredService<IPaymentRepository>().FindByIdAsync(paymentId));
        var initiation = Assert.IsType<PaymentInitiation>(
            await services.GetRequiredService<IPaymentInitiationRepository>().FindByPaymentIdAsync(paymentId));
        Assert.Equal(paymentId, payment.Id);
        Assert.Equal(paymentId, initiation.PaymentId);
        Assert.Equal(PaymentProvider.Csob, payment.Provider);
        Assert.Equal(PaymentProvider.Csob, initiation.Provider);
        Assert.Equal(initializedPayId, payment.ProviderReference);
        Assert.Equal(observedPayId, initiation.ObservedProviderReference);
        Assert.Equal(initializedPayId is null ? PaymentInitiationState.Uncertain : PaymentInitiationState.Initialized,
            initiation.State);
        return payment;
    }

    private sealed record HeldStatusRecoveryRow(
        int State,
        string? ProviderReference,
        int AttemptCount,
        int? ResultCode,
        int? GatewayStatus,
        Guid? LeaseToken,
        DateTimeOffset? CompletedAt,
        DateTimeOffset? VerifiedExpiryObservedAt);

    private async Task RunUncoveredRaceAsync(
        PaymentPurposeType purpose,
        int gatewayPaymentStatus,
        PaymentStatus expectedStatus)
    {
        var beforeStatus = expectedStatus == PaymentStatus.Pending;
        var gatewayResultCode = expectedStatus == PaymentStatus.Expired ? 130 : 0;
        var serviceUnitId = expectedStatus == PaymentStatus.Succeeded && purpose == PaymentPurposeType.Job
            ? Guid.NewGuid()
            : (Guid?)null;
        var payment = new Payment(
            Guid.NewGuid(),
            Guid.NewGuid(),
            purpose,
            purpose == PaymentPurposeType.Job ? Guid.NewGuid() : null,
            new Money(9_300),
            PaymentProvider.Csob,
            AttemptAt,
            creationRequestId: purpose == PaymentPurposeType.CreditTopUp ? Guid.NewGuid() : null);
        var payId = payment.Id.ToString("N")[..15];
        using var webGateway = new SignedGatewayHandler(payId, payId, allowInitialization: true);
        using var workerGateway = new SignedGatewayHandler(
            payId, payId, allowInitialization: false, gatewayResultCode, gatewayPaymentStatus);
        using var webHttp = new HttpClient(webGateway, disposeHandler: false) { BaseAddress = Gateway.ApiBaseUri };
        using var workerHttp = new HttpClient(workerGateway, disposeHandler: false) { BaseAddress = Gateway.ApiBaseUri };
        using var cancellation = new CancellationTokenSource();
        using var webScope = _factory.Services.CreateScope();
        using var workerScope = _factory.Services.CreateScope();
        var clock = new FixedTimeProvider(AttemptAt);
        var availability = new CsobGatewayAvailability(IsEnabled: true);
        var webClient = new CsobGatewayClient(webHttp, Gateway, availability, webGateway, clock);
        var workerClient = new CsobGatewayClient(workerHttp, Gateway, availability, workerGateway, clock);
        var webServices = webScope.ServiceProvider;
        var webInitiation = new PaymentInitiationService(
            webServices.GetRequiredService<IPaymentRepository>(),
            webServices.GetRequiredService<IPaymentInitiationRepository>(),
            new CsobPaymentProviderInitiator(webClient, availability, Gateway),
            webServices.GetRequiredService<IApplicationTransaction>(),
            clock,
            webServices.GetRequiredService<IAuditTrail>());
        var workerServices = workerScope.ServiceProvider;
        var barrier = beforeStatus
            ? new RecoveryReadBarrier(
                workerServices.GetRequiredService<IPaymentRepository>(),
                workerServices.GetRequiredService<IPaymentInitiationRepository>(),
                workerServices.GetRequiredService<FuaPayDbContext>().Database,
                payment.Id,
                beforeStatus: true)
            : null;
        var worker = CreateWorker(workerServices, workerClient, clock, barrier);

        Task<PaymentInitializationOutcome>? webTask = null;
        Task<CsobPaymentRecoveryCycleResult>? workerTask = null;
        try
        {
            if (expectedStatus == PaymentStatus.Succeeded)
            {
                await AddSettlementDependenciesAsync(payment.CustomerUserId, serviceUnitId);
            }

            await AddPreparedPaymentAsync(payment, serviceUnitId);
            webTask = webInitiation.InitializeAsync(payment.Id, cancellation.Token);
            await webGateway.WaitForStatusRequestAsync(webTask);
            using var snapshot = _factory.Services.CreateScope();
            await AssertPaymentAsync(snapshot.ServiceProvider, payment.Id, PaymentStatus.Created,
                PaymentInitiationState.Uncertain, providerReference: null, observedReference: payId);

            workerTask = worker.RunOnceAsync(cancellation.Token);
            if (barrier is not null)
            {
                var first = await barrier.PaymentRead.WaitAsync(BarrierTimeout);
                Assert.Equal(PaymentStatus.Created, first.Status);
                Assert.Null(first.ProviderReference);
                Assert.Null(first.TransactionId);
                Assert.Equal(0, workerGateway.StatusRequests);
                _output.WriteLine("1. Worker read Created outside the recovery transaction, before payment/status.");
            }
            else
            {
                await workerGateway.WaitForStatusRequestAsync(workerTask);
                Assert.Equal(1, workerGateway.StatusRequests);
                Assert.Equal(0, workerGateway.StatusResponses);
                _output.WriteLine("1. Worker reached payment/status with Created/Uncertain and its reply was blocked.");
            }

            Assert.False(webTask.IsCompleted);
            webGateway.ReleaseStatusResponse();
            var initialized = await webTask.WaitAsync(BarrierTimeout);
            Assert.True(initialized.CompletedByCurrentRequest);
            await AssertPaymentAsync(snapshot.ServiceProvider, payment.Id, PaymentStatus.Pending,
                PaymentInitiationState.Initialized, providerReference: payId, observedReference: null);
            _output.WriteLine("2. Independent scope verified web commit Pending/Initialized with the same payId.");

            workerGateway.ReleaseStatusResponse();
            barrier?.ReleaseAfterWebCommit();
            var firstCycle = await workerTask.WaitAsync(BarrierTimeout);
            Assert.Equal(1, firstCycle.ClaimedCount);
            if (barrier is not null)
            {
                Assert.True(barrier.InitiationRead.IsCompletedSuccessfully);
                var second = await barrier.InitiationRead;
                Assert.Equal(PaymentInitiationState.Initialized, second.State);
                Assert.Null(second.ObservedReference);
                Assert.Null(second.TransactionId);
                _output.WriteLine("3. Worker read Initialized outside the recovery transaction, after the web commit.");
            }
            else
            {
                Assert.Equal(1, workerGateway.StatusResponses);
                _output.WriteLine($"3. Worker received the signed {gatewayResultCode}/{gatewayPaymentStatus} after the web commit.");
            }

            var recoveryQueries = snapshot.ServiceProvider.GetRequiredService<IPaymentReconciliationQueries>();
            var firstRecovery = (await recoveryQueries.ListOpenAsync(100))
                .SingleOrDefault(row => row.PaymentId == payment.Id);
            _output.WriteLine(
                $"First cycle: status requests={workerGateway.StatusRequests}, completed={firstCycle.CompletedCount}, " +
                $"rescheduled={firstCycle.RescheduledCount}, requires attention={firstCycle.RequiresAttentionCount}, " +
                $"open state={firstRecovery?.State.ToString() ?? "none"}, " +
                $"bank result/status={firstRecovery?.LastResultCode?.ToString() ?? "NULL"}/" +
                $"{firstRecovery?.LastGatewayPaymentStatus?.ToString() ?? "NULL"}, error={firstRecovery?.LastError}.");

            var restartedCycle = default(CsobPaymentRecoveryCycleResult?);
            if (!beforeStatus)
            {
                using var restartedScope = _factory.Services.CreateScope();
                var restartedClock = new FixedTimeProvider(AttemptAt + Reconciliation.MaximumBackoff);
                var restartedWorker = CreateWorker(restartedScope.ServiceProvider, workerClient, restartedClock);
                restartedCycle = await restartedWorker.RunOnceAsync(cancellation.Token).WaitAsync(BarrierTimeout);
                _output.WriteLine(
                    $"Fresh worker: claimed={restartedCycle.ClaimedCount}, completed={restartedCycle.CompletedCount}.");
            }

            using var verification = _factory.Services.CreateScope();
            var services = verification.ServiceProvider;
            var persistedPayment = Assert.IsType<Payment>(
                await services.GetRequiredService<IPaymentRepository>().FindByIdAsync(payment.Id));
            await AssertPaymentAsync(services, payment.Id, persistedPayment.Status,
                PaymentInitiationState.Initialized, providerReference: payId, observedReference: null);
            await AssertSettlementEffectsAsync(services, persistedPayment);
            Assert.Equal(1, webGateway.InitializationRequests);
            Assert.Equal(0, workerGateway.InitializationRequests);
            _output.WriteLine("One web payment/init, zero worker payment/init.");

            if (beforeStatus)
            {
                Assert.True(
                    firstRecovery?.State == PaymentReconciliationState.Scheduled,
                    $"Same-payId web commit between the pre-status reads must leave recovery Scheduled. " +
                    $"Actual: {firstRecovery?.State}, status requests={workerGateway.StatusRequests}, " +
                    $"requires attention={firstCycle.RequiresAttentionCount}.");
                Assert.Equal(1, workerGateway.StatusRequests);
                Assert.Equal(1, firstCycle.RescheduledCount);
            }
            else
            {
                Assert.True(
                    persistedPayment.Status == expectedStatus,
                    $"After signed {gatewayResultCode}/{gatewayPaymentStatus} and a same-payId web commit, expected {expectedStatus}. " +
                    $"Actual: {persistedPayment.Status}, recovery={firstRecovery?.State}, " +
                    $"first completed={firstCycle.CompletedCount}, restart claimed={restartedCycle!.ClaimedCount}.");
                Assert.Equal(1, firstCycle.CompletedCount + restartedCycle!.CompletedCount);
            }

            Assert.Equal(0, firstCycle.RequiresAttentionCount);
        }
        finally
        {
            await cancellation.CancelAsync();
            await Task.WhenAll(
                    (Task?)webTask ?? Task.CompletedTask,
                    (Task?)workerTask ?? Task.CompletedTask)
                .ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            await DeleteUncoveredScenarioAsync(payment, serviceUnitId);
        }
    }

    private static CsobPaymentRecoveryProcessor CreateWorker(
        IServiceProvider services,
        ICsobGatewayClient client,
        TimeProvider clock,
        RecoveryReadBarrier? barrier = null)
    {
        var transaction = services.GetRequiredService<IApplicationTransaction>();
        var audit = services.GetRequiredService<IAuditTrail>();
        var reconciliation = new CsobPaymentReconciliationService(
            client,
            (IPaymentRepository?)barrier ?? services.GetRequiredService<IPaymentRepository>(),
            (IPaymentInitiationRepository?)barrier ?? services.GetRequiredService<IPaymentInitiationRepository>(),
            services.GetRequiredService<ICsobVerifiedReturnEvidenceReader>(),
            services.GetRequiredService<ICsobExpiryCorrectionGuard>(),
            services.GetRequiredService<IPaymentSettlementService>(),
            transaction,
            clock,
            audit);
        return new CsobPaymentRecoveryProcessor(
            services.GetRequiredService<ICsobPaymentRecoveryRepository>(),
            reconciliation, transaction, Reconciliation, clock, audit,
            NullLogger<CsobPaymentRecoveryProcessor>.Instance);
    }

    private async Task AssertSettlementEffectsAsync(IServiceProvider services, Payment payment)
    {
        var settled = payment.Status == PaymentStatus.Succeeded;
        var account = await services.GetRequiredService<ICreditAccountRepository>()
            .FindByOwnerIdAsync(payment.CustomerUserId, CancellationToken.None);
        var movements = account?.Movements ?? [];
        var document = await services.GetRequiredService<IFinancialDocumentRepository>()
            .FindBySourceAsync(FinancialDocumentSourceType.Payment, payment.Id);
        Assert.Equal(settled, document is not null);
        if (settled && payment.PurposeType == PaymentPurposeType.CreditTopUp)
        {
            var credit = Assert.Single(movements);
            Assert.Equal(payment.Id, credit.OperationId);
            Assert.Equal(CreditMovementType.Credit, credit.Type);
            Assert.Equal(payment.Amount, credit.Amount);
        }
        else
        {
            Assert.Empty(movements);
        }

        JobPaymentStatus? jobStatus = null;
        if (payment.JobId is Guid jobId)
        {
            var job = Assert.IsType<Job>(await services.GetRequiredService<IJobRepository>()
                .FindByIdAsync(jobId, CancellationToken.None));
            jobStatus = job.PaymentStatus;
            Assert.Equal(settled ? JobPaymentStatus.Paid : JobPaymentStatus.Unpaid, job.PaymentStatus);
            Assert.Equal(settled ? payment.Id : (Guid?)null, job.SettlementReferenceId);
        }

        var database = services.GetRequiredService<FuaPayDbContext>().Database;
        var settlements = await database.SqlQuery<int>(
            $"""
            SELECT count(*)::int AS "Value"
            FROM audit.events
            WHERE entity_type = 'payment' AND entity_id = {payment.Id.ToString()} AND action = 'payment.succeeded'
            """).SingleAsync();
        Assert.Equal(settled ? 1 : 0, settlements);
        _output.WriteLine(
            $"Financial effects: settlements={settlements}, credit movements={movements.Count}, " +
            $"job={jobStatus?.ToString() ?? "none"}, documents={(document is null ? 0 : 1)}.");
    }

    private async Task AddSettlementDependenciesAsync(Guid customerId, Guid? serviceUnitId)
    {
        using var scope = _factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        await services.GetRequiredService<IAccessUserRepository>().AddAsync(
            new AccessUser(customerId, "Race Test Customer", "race@example.test", AttemptAt),
            new ExternalIdentityKey("race-test", "fuapay", customerId.ToString()),
            CancellationToken.None);
        if (serviceUnitId is Guid unitId)
        {
            await services.GetRequiredService<IServiceUnitRepository>().AddAsync(
                new ServiceUnit(unitId, $"S{unitId:N}"[..8].ToUpperInvariant(), "Race Test Workshop",
                    ServiceType.ThreeDPrint, AttemptAt, ServiceUnitChangeActor.ForProcess("race-test")),
                CancellationToken.None);
        }
    }

    private async Task DeleteUncoveredScenarioAsync(Payment payment, Guid? serviceUnitId)
    {
        using var scope = _factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<FuaPayDbContext>().Database;
        await database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM financial_documents.documents WHERE source_type = {(int)FinancialDocumentSourceType.Payment} AND source_id = {payment.Id}");
        await database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM notifications.outbox WHERE recipient_user_id = {payment.CustomerUserId}");
        await DeleteScenarioAsync(payment, useEfForPayment: true);
        await database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM credits.movements WHERE account_id IN (SELECT id FROM credits.accounts WHERE owner_id = {payment.CustomerUserId})");
        await database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM credits.accounts WHERE owner_id = {payment.CustomerUserId}");
        await database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM access.external_identities WHERE user_id = {payment.CustomerUserId}");
        await database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM access.users WHERE id = {payment.CustomerUserId}");
        if (serviceUnitId is Guid unitId)
        {
            await database.ExecuteSqlInterpolatedAsync($"DELETE FROM service_units.units WHERE id = {unitId}");
        }
        if (payment.JobId is Guid jobId)
        {
            await database.ExecuteSqlInterpolatedAsync($"DELETE FROM audit.events WHERE entity_type = 'job' AND entity_id = {jobId.ToString()}");
        }
    }

    private async Task RunRaceAsync(
        PaymentPurposeType purpose,
        bool conflictingStatusPayId,
        bool completeBetweenRecoveryReads = false)
    {
        var payment = new Payment(
            Guid.NewGuid(),
            Guid.NewGuid(),
            purpose,
            purpose == PaymentPurposeType.Job ? Guid.NewGuid() : null,
            new Money(9_300),
            PaymentProvider.Csob,
            AttemptAt,
            creationRequestId: purpose == PaymentPurposeType.CreditTopUp ? Guid.NewGuid() : null);
        var payId = payment.Id.ToString("N")[..15];
        var responsePayId = conflictingStatusPayId ? "differentpayid" : payId;
        using var webGateway =
            new SignedGatewayHandler(payId, payId, allowInitialization: true);
        using var workerGateway =
            new SignedGatewayHandler(payId, responsePayId, allowInitialization: false);
        using var webHttp = new HttpClient(webGateway, disposeHandler: false)
        {
            BaseAddress = Gateway.ApiBaseUri
        };
        using var workerHttp = new HttpClient(workerGateway, disposeHandler: false)
        {
            BaseAddress = Gateway.ApiBaseUri
        };
        using var cancellation = new CancellationTokenSource();
        using var webScope = _factory.Services.CreateScope();
        using var workerScope = _factory.Services.CreateScope();
        var availability = new CsobGatewayAvailability(IsEnabled: true);
        var timeProvider = new FixedTimeProvider(AttemptAt);
        var webServices = webScope.ServiceProvider;
        var workerServices = workerScope.ServiceProvider;
        var workerPayments = workerServices.GetRequiredService<IPaymentRepository>();
        var workerInitiations = workerServices.GetRequiredService<IPaymentInitiationRepository>();
        var readBarrier = completeBetweenRecoveryReads
            ? new RecoveryReadBarrier(
                workerPayments,
                workerInitiations,
                workerServices.GetRequiredService<FuaPayDbContext>().Database,
                payment.Id)
            : null;
        var webClient = new CsobGatewayClient(
            webHttp, Gateway, availability, webGateway, timeProvider);
        var workerClient = new CsobGatewayClient(
            workerHttp, Gateway, availability, workerGateway, timeProvider);
        var webInitiation = new PaymentInitiationService(
            webServices.GetRequiredService<IPaymentRepository>(),
            webServices.GetRequiredService<IPaymentInitiationRepository>(),
            new CsobPaymentProviderInitiator(webClient, availability, Gateway),
            webServices.GetRequiredService<IApplicationTransaction>(),
            timeProvider,
            webServices.GetRequiredService<IAuditTrail>());
        var reconciliation = new CsobPaymentReconciliationService(
            workerClient,
            (IPaymentRepository?)readBarrier ?? workerPayments,
            (IPaymentInitiationRepository?)readBarrier ?? workerInitiations,
            workerServices.GetRequiredService<ICsobVerifiedReturnEvidenceReader>(),
            workerServices.GetRequiredService<ICsobExpiryCorrectionGuard>(),
            workerServices.GetRequiredService<IPaymentSettlementService>(),
            workerServices.GetRequiredService<IApplicationTransaction>(),
            timeProvider,
            workerServices.GetRequiredService<IAuditTrail>());
        var processor = new CsobPaymentRecoveryProcessor(
            workerServices.GetRequiredService<ICsobPaymentRecoveryRepository>(),
            reconciliation,
            workerServices.GetRequiredService<IApplicationTransaction>(),
            Reconciliation,
            timeProvider,
            workerServices.GetRequiredService<IAuditTrail>(),
            NullLogger<CsobPaymentRecoveryProcessor>.Instance);

        Task<PaymentInitializationOutcome>? webTask = null;
        Task<CsobPaymentRecoveryCycleResult>? workerTask = null;
        try
        {
            await AddPreparedPaymentAsync(payment);

            webTask = webInitiation.InitializeAsync(payment.Id, cancellation.Token);
            await webGateway.WaitForStatusRequestAsync(webTask);

            // The web request has committed the candidate and is waiting for its status reply.
            using var snapshot = _factory.Services.CreateScope();
            await AssertPaymentAsync(
                snapshot.ServiceProvider,
                payment.Id,
                PaymentStatus.Created,
                PaymentInitiationState.Uncertain,
                providerReference: null,
                observedReference: payId);

            workerTask = processor.RunOnceAsync(cancellation.Token);
            // The worker can only reach payment/status after reading Created/Uncertain.
            await workerGateway.WaitForStatusRequestAsync(workerTask);

            if (readBarrier is not null)
            {
                workerGateway.ReleaseStatusResponse();
                await Task.WhenAny(readBarrier.PaymentRead, workerTask).WaitAsync(BarrierTimeout);
                Assert.True(readBarrier.PaymentRead.IsCompletedSuccessfully,
                    "The worker must read Payment inside the recovery transaction.");
                Assert.False(webTask.IsCompleted);
                var first = await readBarrier.PaymentRead;
                Assert.Equal(PaymentStatus.Created, first.Status);
                Assert.Null(first.ProviderReference);
                Assert.Equal("read committed", first.Isolation);
                _output.WriteLine(
                    $"1. Worker read Payment={first.Status}, reference=NULL in transaction " +
                    $"{first.TransactionId}, PostgreSQL isolation={first.Isolation}, after signed status 0/1 for {payId}.");
            }

            webGateway.ReleaseStatusResponse();
            var initialized = await webTask.WaitAsync(BarrierTimeout);
            Assert.True(initialized.CompletedByCurrentRequest);
            Assert.Equal(PaymentStatus.Pending, initialized.Payment.Status);
            Assert.Equal(payId, initialized.Payment.ProviderReference);

            // A separate context must see the committed web result before the worker resumes.
            await AssertPaymentAsync(
                snapshot.ServiceProvider,
                payment.Id,
                PaymentStatus.Pending,
                PaymentInitiationState.Initialized,
                providerReference: payId,
                observedReference: null);
            if (readBarrier is null)
            {
                workerGateway.ReleaseStatusResponse();
            }
            else
            {
                Assert.False(readBarrier.InitiationRead.IsCompleted);
                _output.WriteLine($"2. Independent scope verified web commit Pending/Initialized for {payId}.");
                readBarrier.ReleaseAfterWebCommit();
            }

            var cycle = await workerTask.WaitAsync(BarrierTimeout);
            if (readBarrier is not null)
            {
                Assert.True(readBarrier.InitiationRead.IsCompletedSuccessfully,
                    "The worker must read Initiation after the web commit.");
                var second = await readBarrier.InitiationRead;
                Assert.Equal((await readBarrier.PaymentRead).TransactionId, second.TransactionId);
                Assert.Equal(PaymentInitiationState.Initialized, second.State);
                Assert.Null(second.ObservedReference);
                _output.WriteLine(
                    $"3. Worker read Initiation={second.State}, observed reference=NULL in the same transaction {second.TransactionId}.");
            }

            using var verifyScope = _factory.Services.CreateScope();
            var services = verifyScope.ServiceProvider;
            await AssertPaymentAsync(
                services,
                payment.Id,
                PaymentStatus.Pending,
                PaymentInitiationState.Initialized,
                providerReference: payId,
                observedReference: null);
            if (readBarrier is null)
            {
                await AssertNoFinancialEffectsAsync(services, payment);
            }
            else
            {
                await AssertNoFinancialEffectsThroughRepositoriesAsync(services, payment);
                _output.WriteLine("4. Persisted Pending/Initialized, zero credit movement, job Unpaid, no financial document.");
            }

            var open = await services.GetRequiredService<IPaymentReconciliationQueries>()
                .ListOpenAsync(100);
            var recovery = Assert.Single(open, row => row.PaymentId == payment.Id);
            Assert.Equal(payId, recovery.ProviderReference);
            Assert.Equal(1, cycle.ClaimedCount);
            Assert.Equal(0, cycle.CompletedCount);
            Assert.Equal(1, recovery.AttemptCount);
            Assert.Null(recovery.LeaseExpiresAt);
            if (readBarrier is not null)
            {
                _output.WriteLine(
                    $"5. Recovery={recovery.State}, rescheduled={cycle.RescheduledCount}, " +
                    $"requires attention={cycle.RequiresAttentionCount}, error={recovery.LastError}.");
            }

            if (conflictingStatusPayId)
            {
                Assert.Equal(PaymentReconciliationState.RequiresAttention, recovery.State);
                Assert.Equal(1, cycle.RequiresAttentionCount);
                Assert.Equal(0, cycle.RescheduledCount);
                Assert.Null(recovery.LastGatewayPaymentStatus);
            }
            else
            {
                Assert.True(
                    recovery.State == PaymentReconciliationState.Scheduled,
                    $"Expected Scheduled after signed status 0/1 with the same payId. " +
                    $"Actual: {recovery.State}, attempt count: {recovery.AttemptCount}, " +
                    $"gateway status: {recovery.LastGatewayPaymentStatus?.ToString() ?? "NULL"}, " +
                    $"result code: {recovery.LastResultCode?.ToString() ?? "NULL"}, " +
                    $"error: {recovery.LastError}");
                Assert.Equal(0, cycle.RequiresAttentionCount);
                Assert.Equal(1, cycle.RescheduledCount);
                Assert.Equal(1, recovery.LastGatewayPaymentStatus);
                Assert.Equal(0, recovery.LastResultCode);
                Assert.True(recovery.NextAttemptAt > AttemptAt);
            }

            var nextClaims = await services.GetRequiredService<ICsobPaymentRecoveryRepository>()
                .ClaimDueAsync(
                    recovery.NextAttemptAt,
                    Reconciliation.LeaseDuration,
                    Reconciliation.BatchSize);
            if (conflictingStatusPayId)
            {
                Assert.DoesNotContain(nextClaims, claim => claim.PaymentId == payment.Id);
            }
            else
            {
                var nextClaim = Assert.Single(nextClaims, claim => claim.PaymentId == payment.Id);
                Assert.Equal(payId, nextClaim.ProviderReference);
            }
        }
        finally
        {
            await cancellation.CancelAsync();
            await Task.WhenAll(
                    (Task?)webTask ?? Task.CompletedTask,
                    (Task?)workerTask ?? Task.CompletedTask)
                .ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            await DeleteScenarioAsync(payment, useEfForPayment: completeBetweenRecoveryReads);
        }
    }

    private async Task AddPreparedPaymentAsync(Payment payment, Guid? serviceUnitId = null)
    {
        using var scope = _factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        if (payment.JobId is Guid jobId)
        {
            var job = new Job(
                jobId,
                TestJobData.NextJobNumber(),
                serviceUnitId ?? Guid.NewGuid(),
                payment.CustomerUserId,
                Guid.NewGuid(),
                ServiceType.ThreeDPrint,
                "CSOB initialization race",
                "Direct job payment for the CSOB initialization race test.",
                payment.Amount,
                AttemptAt);
            job.Publish(AttemptAt);
            await services.GetRequiredService<IJobRepository>()
                .AddAsync(job, CancellationToken.None);
        }

        var orderNumber = await services.GetRequiredService<IPaymentOrderNumberAllocator>()
            .AllocateAsync();
        var initiation = new PaymentInitiation(
            payment.Id,
            PaymentProvider.Csob,
            orderNumber,
            Guid.NewGuid(),
            AttemptAt);
        await services.GetRequiredService<IPaymentRepository>()
            .AddPreparedAsync(payment, initiation);
    }

    private static async Task AssertPaymentAsync(
        IServiceProvider services,
        Guid paymentId,
        PaymentStatus status,
        PaymentInitiationState initiationState,
        string? providerReference,
        string? observedReference)
    {
        var payment = Assert.IsType<Payment>(
            await services.GetRequiredService<IPaymentRepository>()
                .FindByIdAsync(paymentId));
        var initiation = Assert.IsType<PaymentInitiation>(
            await services.GetRequiredService<IPaymentInitiationRepository>()
                .FindByPaymentIdAsync(paymentId));
        Assert.Equal(status, payment.Status);
        Assert.Equal(providerReference, payment.ProviderReference);
        Assert.Equal(initiationState, initiation.State);
        Assert.Equal(observedReference, initiation.ObservedProviderReference);
    }

    private static async Task AssertNoFinancialEffectsAsync(
        IServiceProvider services,
        Payment payment)
    {
        var database = services.GetRequiredService<FuaPayDbContext>().Database;
        Assert.False(await database.SqlQuery<bool>(
            $"""
            SELECT EXISTS (
                SELECT 1 FROM credits.movements WHERE operation_id = {payment.Id}
            ) AS "Value"
            """).SingleAsync());
        Assert.False(await database.SqlQuery<bool>(
            $"""
            SELECT EXISTS (
                SELECT 1 FROM financial_documents.documents WHERE source_id = {payment.Id}
            ) AS "Value"
            """).SingleAsync());

        if (payment.JobId is Guid jobId)
        {
            var job = Assert.IsType<Job>(
                await services.GetRequiredService<IJobRepository>()
                    .FindByIdAsync(jobId, CancellationToken.None));
            Assert.Equal(JobPaymentStatus.Unpaid, job.PaymentStatus);
        }
    }

    private static async Task AssertNoFinancialEffectsThroughRepositoriesAsync(
        IServiceProvider services,
        Payment payment)
    {
        Assert.Null(await services.GetRequiredService<ICreditQueries>()
            .FindMovementForOwnerAsync(payment.CustomerUserId, payment.Id));
        Assert.Null(await services.GetRequiredService<IFinancialDocumentRepository>()
            .FindBySourceAsync(FinancialDocumentSourceType.Payment, payment.Id));
        var job = Assert.IsType<Job>(await services.GetRequiredService<IJobRepository>()
            .FindByIdAsync(Assert.IsType<Guid>(payment.JobId), CancellationToken.None));
        Assert.Equal(JobPaymentStatus.Unpaid, job.PaymentStatus);
    }

    private async Task DeleteScenarioAsync(Payment payment, bool useEfForPayment)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<FuaPayDbContext>();
        var database = context.Database;
        await database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM audit.events WHERE entity_type = 'payment' AND entity_id = {payment.Id.ToString()}");
        if (useEfForPayment)
        {
            var entityType = context.Model.GetEntityTypes().Single(
                type => type.GetSchema() == "payments" && type.GetTableName() == "payments");
            var entity = await context.FindAsync(entityType.ClrType, payment.Id);
            if (entity is not null)
            {
                context.Remove(entity);
                await context.SaveChangesAsync();
            }
        }
        else
        {
            await database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM payments.payments WHERE id = {payment.Id}");
        }

        if (payment.JobId is Guid jobId)
        {
            await database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM jobs.jobs WHERE id = {jobId}");
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class RecoveryReadBarrier(
        IPaymentRepository payments,
        IPaymentInitiationRepository initiations,
        DatabaseFacade database,
        Guid scenarioPaymentId,
        bool beforeStatus = false) : IPaymentRepository, IPaymentInitiationRepository
    {
        private readonly TaskCompletionSource<(Guid? TransactionId, PaymentStatus Status, string? ProviderReference, string? Isolation)>
            _paymentRead = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<(Guid? TransactionId, PaymentInitiationState State, string? ObservedReference)>
            _initiationRead = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _webCommitted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<(Guid? TransactionId, PaymentStatus Status, string? ProviderReference, string? Isolation)>
            PaymentRead => _paymentRead.Task;
        public Task<(Guid? TransactionId, PaymentInitiationState State, string? ObservedReference)>
            InitiationRead => _initiationRead.Task;

        public int PaymentReadCount { get; private set; }

        public void ReleaseAfterWebCommit() => _webCommitted.SetResult();

        public async Task<Payment?> FindByIdAsync(Guid paymentId, CancellationToken cancellationToken)
        {
            PaymentReadCount++;
            var payment = await payments.FindByIdAsync(paymentId, cancellationToken);
            var transaction = database.CurrentTransaction;
            if (paymentId == scenarioPaymentId && (transaction is null) == beforeStatus && !PaymentRead.IsCompleted)
            {
                var loaded = Assert.IsType<Payment>(payment);
                var isolation = transaction is null
                    ? null
                    : await database.SqlQuery<string>(
                        $"SELECT current_setting('transaction_isolation') AS \"Value\"")
                        .SingleAsync(cancellationToken);
                _paymentRead.SetResult((transaction?.TransactionId, loaded.Status, loaded.ProviderReference, isolation));
                await _webCommitted.Task.WaitAsync(cancellationToken);
            }

            return payment;
        }

        public async Task<PaymentInitiation?> FindByPaymentIdAsync(
            Guid paymentId,
            CancellationToken cancellationToken)
        {
            var transaction = database.CurrentTransaction;
            if (paymentId != scenarioPaymentId || (transaction is null) != beforeStatus || InitiationRead.IsCompleted)
            {
                return await initiations.FindByPaymentIdAsync(paymentId, cancellationToken);
            }

            Assert.True(_webCommitted.Task.IsCompletedSuccessfully,
                "The worker must read Initiation after the web commit.");
            var loaded = Assert.IsType<PaymentInitiation>(
                await initiations.FindByPaymentIdAsync(paymentId, cancellationToken));
            _initiationRead.SetResult((transaction?.TransactionId, loaded.State, loaded.ObservedProviderReference));
            return loaded;
        }

        public Task<Payment?> FindBlockingForJobAsync(Guid jobId, CancellationToken cancellationToken) =>
            payments.FindBlockingForJobAsync(jobId, cancellationToken);

        public Task<Payment?> FindByProviderReferenceAsync(
            PaymentProvider provider,
            string providerReference,
            CancellationToken cancellationToken) =>
            payments.FindByProviderReferenceAsync(provider, providerReference, cancellationToken);

        public Task<Payment?> FindByCreationRequestIdAsync(Guid creationRequestId, CancellationToken cancellationToken) =>
            payments.FindByCreationRequestIdAsync(creationRequestId, cancellationToken);

        public Task AddAsync(Payment payment, CancellationToken cancellationToken) =>
            payments.AddAsync(payment, cancellationToken);

        public Task AddPreparedAsync(Payment payment, PaymentInitiation initiation, CancellationToken cancellationToken) =>
            payments.AddPreparedAsync(payment, initiation, cancellationToken);

        public Task SaveAsync(Payment payment, CancellationToken cancellationToken) =>
            payments.SaveAsync(payment, cancellationToken);

        public Task SaveAsync(PaymentInitiation initiation, CancellationToken cancellationToken) =>
            initiations.SaveAsync(initiation, cancellationToken);
    }

    private sealed class SignedGatewayHandler(
        string requestedPayId,
        string responsePayId,
        bool allowInitialization,
        int statusResultCode = 0,
        int paymentStatus = 1) : HttpMessageHandler, ICsobGatewaySignature
    {
        private const string GatewayTimestamp = "20261118122517";

        private readonly RSA _key = RSA.Create(2048);
        private readonly TaskCompletionSource _statusRequested =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _releaseStatus =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int InitializationRequests { get; private set; }
        public int StatusRequests { get; private set; }
        public int StatusResponses { get; private set; }

        public async Task WaitForStatusRequestAsync(Task operation)
        {
            if (await Task.WhenAny(_statusRequested.Task, operation).WaitAsync(BarrierTimeout) == operation)
            {
                await operation;
            }

            Assert.True(_statusRequested.Task.IsCompletedSuccessfully, "Expected a payment/status request.");
        }

        public void ReleaseStatusResponse() => _releaseStatus.SetResult();

        public string Sign(string textToSign) =>
            Convert.ToBase64String(_key.SignData(
                Encoding.UTF8.GetBytes(textToSign),
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1));

        public bool Verify(string textToSign, string signature) =>
            _key.VerifyData(
                Encoding.UTF8.GetBytes(textToSign),
                Convert.FromBase64String(signature),
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Post && path == "/api/v1.9/payment/init")
            {
                InitializationRequests++;
                Assert.True(allowInitialization, "Only the web request may call payment/init.");
                return CreateResponse(requestedPayId, resultCode: 0, status: 1);
            }

            var expectedPrefix = $"/api/v1.9/payment/status/{Gateway.MerchantId}/{requestedPayId}/{GatewayTimestamp}/";
            if (request.Method != HttpMethod.Get || !path.StartsWith(expectedPrefix, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Unexpected request to the fake CSOB gateway.");
            }

            StatusRequests++;
            _statusRequested.TrySetResult();
            await _releaseStatus.Task.WaitAsync(cancellationToken);
            StatusResponses++;
            return CreateResponse(responsePayId, statusResultCode, paymentStatus);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _key.Dispose();
            }

            base.Dispose(disposing);
        }

        private HttpResponseMessage CreateResponse(string payId, int resultCode, int status) =>
            new(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    payId,
                    dttm = GatewayTimestamp,
                    resultCode,
                    resultMessage = "OK",
                    paymentStatus = status,
                    signature = Sign(FormattableString.Invariant($"{payId}|{GatewayTimestamp}|{resultCode}|OK|{status}"))
                })
            };
    }
}
