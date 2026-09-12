using FuaPay.Web.BuildingBlocks.Application;
using FuaPay.Web.BuildingBlocks.Auditing;
using FuaPay.Web.BuildingBlocks.Persistence;
using FuaPay.Web.Modules.Payments.Application;
using FuaPay.Web.Modules.Payments.Domain;
using FuaPay.Web.Modules.Payments.Infrastructure.Csob;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace FuaPay.DatabaseTests;

public sealed class PaymentReconciliationPersistenceTests :
    IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly DateTimeOffset CreatedAt =
        new(2026, 8, 14, 9, 0, 0, TimeSpan.Zero);

    private readonly WebApplicationFactory<Program> _factory;

    public PaymentReconciliationPersistenceTests(
        WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ScheduleFromReturnAsync_ConcurrentDuplicatesCreateSingleItem()
    {
        var payment = CreatePendingPayment("payconcurrent01");

        try
        {
            await AddPaymentAsync(payment);

            var scheduled = await Task.WhenAll(
                Enumerable.Range(0, 8)
                    .Select(_ => ScheduleReturnAsync(
                        payment.ProviderReference!,
                        CreatedAt.AddMinutes(1))));

            Assert.All(
                scheduled,
                observation => Assert.Equal(
                    payment.Id,
                    Assert.IsType<CsobBrowserReturnObservation>(observation)
                        .PaymentId));
            Assert.Equal(
                1,
                scheduled.Count(
                    observation => observation?.IsFirstObservation == true));

            using var queryScope = _factory.Services.CreateScope();
            var items = await queryScope.ServiceProvider
                .GetRequiredService<IPaymentReconciliationQueries>()
                .ListOpenAsync(100);
            var matching = items.Where(item => item.PaymentId == payment.Id)
                .ToArray();

            var item = Assert.Single(matching);
            Assert.Equal(PaymentReconciliationState.Scheduled, item.State);
            Assert.Equal(payment.ProviderReference, item.ProviderReference);
            Assert.Equal(0, item.AttemptCount);
        }
        finally
        {
            await DeletePaymentAsync(payment.Id);
        }
    }

    [Fact]
    public async Task ScheduleFromReturnAsync_ConcurrentVerifiedExpiryIsDurableAndHasNoPaymentEffect()
    {
        var payment = CreatePendingPayment("payexpiry00001");
        var verifiedReturn = CreateVerifiedExpiryReturn(
            payment.ProviderReference!);

        try
        {
            await AddPaymentAsync(payment);

            var observations = await Task.WhenAll(
                Enumerable.Range(0, 8)
                    .Select(_ => ScheduleVerifiedReturnAsync(
                        verifiedReturn,
                        CreatedAt.AddMinutes(31))));

            Assert.Equal(
                1,
                observations.Count(item =>
                    item?.IsFirstVerifiedExpiryObservation == true));

            using var scope = _factory.Services.CreateScope();
            var evidence = await scope.ServiceProvider
                .GetRequiredService<ICsobVerifiedReturnEvidenceReader>()
                .FindVerifiedExpiryAsync(
                    payment.Id,
                    payment.ProviderReference!);

            Assert.NotNull(evidence);
            Assert.Equal(130, evidence.ResultCode);
            Assert.Equal(6, evidence.PaymentStatus);
            Assert.Equal(verifiedReturn.Dttm, evidence.Dttm);
            Assert.Equal(verifiedReturn.TextToSign, evidence.TextToSign);
            Assert.Equal(verifiedReturn.Signature, evidence.Signature);

            var persistedPayment = await scope.ServiceProvider
                .GetRequiredService<IPaymentRepository>()
                .FindByIdAsync(payment.Id);

            Assert.Equal(PaymentStatus.Pending, persistedPayment?.Status);
        }
        finally
        {
            await DeletePaymentAsync(payment.Id);
        }
    }

    [Fact]
    public async Task ClaimDueAsync_LeaseBlocksSecondWorkerAndCanBeReclaimedAfterExpiry()
    {
        var payment = CreatePendingPayment("paylease000001");
        var scheduledAt = CreatedAt.AddMinutes(1);
        var leaseDuration = TimeSpan.FromMinutes(2);

        try
        {
            await AddPaymentAsync(payment);
            await ScheduleReturnAsync(
                payment.ProviderReference!,
                scheduledAt);

            var firstClaim = Assert.Single(
                await ClaimAsync(scheduledAt, leaseDuration));

            Assert.Empty(
                await ClaimAsync(scheduledAt, leaseDuration));

            var reclaimed = Assert.Single(
                await ClaimAsync(
                    scheduledAt + leaseDuration + TimeSpan.FromSeconds(1),
                    leaseDuration));

            Assert.Equal(payment.Id, reclaimed.PaymentId);
            Assert.NotEqual(firstClaim.LeaseToken, reclaimed.LeaseToken);

            using var transitionScope = _factory.Services.CreateScope();
            var repository = transitionScope.ServiceProvider
                .GetRequiredService<ICsobPaymentRecoveryRepository>();

            Assert.False(
                await repository.RescheduleAsync(
                    firstClaim,
                    scheduledAt.AddMinutes(3),
                    scheduledAt.AddMinutes(4),
                    gatewayPaymentStatus: 2,
                    resultCode: 0,
                    error: "stale worker"));

            Assert.True(
                await repository.RescheduleAsync(
                    reclaimed,
                    scheduledAt.AddMinutes(3),
                    scheduledAt.AddMinutes(4),
                    gatewayPaymentStatus: 2,
                    resultCode: 0,
                    error: "retry"));
        }
        finally
        {
            await DeletePaymentAsync(payment.Id);
        }
    }

    [Fact]
    public async Task ScheduleLongOpenPaymentsAsync_DiscoversPendingWithoutBrowserReturn()
    {
        var payment = CreatePendingPayment("paylongopen001");

        try
        {
            await AddPaymentAsync(payment);

            using var scope = _factory.Services.CreateScope();
            var repository = scope.ServiceProvider
                .GetRequiredService<ICsobPaymentRecoveryRepository>();
            var scheduledAt = CreatedAt.AddMinutes(10);

            var scheduled = await repository.ScheduleLongOpenPaymentsAsync(
                pendingBefore: CreatedAt.AddMinutes(5),
                scheduledAt,
                limit: 20);

            Assert.Equal(1, scheduled);

            var claim = Assert.Single(
                await repository.ClaimDueAsync(
                    scheduledAt,
                    TimeSpan.FromMinutes(2),
                    limit: 20));

            Assert.Equal(payment.Id, claim.PaymentId);
            Assert.Equal(payment.ProviderReference, claim.ProviderReference);
        }
        finally
        {
            await DeletePaymentAsync(payment.Id);
        }
    }

    [Fact]
    public async Task ScheduleFromReturnAsync_ConcurrentDuplicatesAfterLongOpenHaveSingleFirstObservation()
    {
        var payment = CreatePendingPayment("paylongreturn01");

        try
        {
            await AddPaymentAsync(payment);

            using (var discoveryScope = _factory.Services.CreateScope())
            {
                var scheduled = await discoveryScope.ServiceProvider
                    .GetRequiredService<ICsobPaymentRecoveryRepository>()
                    .ScheduleLongOpenPaymentsAsync(
                        pendingBefore: CreatedAt.AddMinutes(5),
                        scheduledAt: CreatedAt.AddMinutes(10),
                        limit: 20);
                Assert.Equal(1, scheduled);
            }

            var observations = await Task.WhenAll(
                Enumerable.Range(0, 8)
                    .Select(_ => ScheduleReturnAsync(
                        payment.ProviderReference!,
                        CreatedAt.AddMinutes(11))));

            Assert.All(
                observations,
                observation => Assert.Equal(
                    payment.Id,
                    Assert.IsType<CsobBrowserReturnObservation>(observation)
                        .PaymentId));
            Assert.Equal(
                1,
                observations.Count(
                    observation => observation?.IsFirstObservation == true));
        }
        finally
        {
            await DeletePaymentAsync(payment.Id);
        }
    }

    [Fact]
    public async Task ScheduleFromReturnAsync_FirstReturnAcceleratesFutureScheduledRecovery()
    {
        var payment = CreatePendingPayment("payfuture00001");
        var scheduledAt = CreatedAt.AddMinutes(10);
        var attemptedAt = CreatedAt.AddMinutes(11);
        var futureAttemptAt = CreatedAt.AddMinutes(20);
        var returnedAt = CreatedAt.AddMinutes(12);

        try
        {
            await AddPaymentAsync(payment);
            await ScheduleLongOpenAsync(payment, scheduledAt);

            var claim = Assert.Single(
                await ClaimAsync(scheduledAt, TimeSpan.FromMinutes(2)));

            using (var rescheduleScope = _factory.Services.CreateScope())
            {
                Assert.True(
                    await rescheduleScope.ServiceProvider
                        .GetRequiredService<ICsobPaymentRecoveryRepository>()
                        .RescheduleAsync(
                            claim,
                            attemptedAt,
                            futureAttemptAt,
                            gatewayPaymentStatus: 2,
                            resultCode: 100,
                            error: "provider still pending"));
            }

            var observation = Assert.IsType<CsobBrowserReturnObservation>(
                await ScheduleReturnAsync(
                    payment.ProviderReference!,
                    returnedAt));
            var recovery = await LoadRecoveryAsync(payment.Id);

            Assert.True(observation.IsFirstObservation);
            Assert.Equal(
                (int)PaymentReconciliationState.Scheduled,
                recovery.State);
            Assert.Equal(returnedAt, recovery.NextAttemptAt);
            Assert.Equal(1, recovery.AttemptCount);
            Assert.Equal(attemptedAt, recovery.LastAttemptAt);
            Assert.Equal(2, recovery.LastGatewayPaymentStatus);
            Assert.Equal(100, recovery.LastResultCode);
            Assert.Equal("provider still pending", recovery.LastError);

            var acceleratedClaim = Assert.Single(
                await ClaimAsync(returnedAt, TimeSpan.FromMinutes(2)));
            Assert.Equal(payment.Id, acceleratedClaim.PaymentId);
            Assert.Equal(1, acceleratedClaim.AttemptCount);
        }
        finally
        {
            await DeletePaymentAsync(payment.Id);
        }
    }

    [Fact]
    public async Task ScheduleFromReturnAsync_DoesNotPostponeEarlierDueTimeOrDuplicate()
    {
        var payment = CreatePendingPayment("payearlier0001");
        var scheduledAt = CreatedAt.AddMinutes(10);
        var returnedAt = CreatedAt.AddMinutes(12);

        try
        {
            await AddPaymentAsync(payment);
            await ScheduleLongOpenAsync(payment, scheduledAt);

            var first = Assert.IsType<CsobBrowserReturnObservation>(
                await ScheduleReturnAsync(
                    payment.ProviderReference!,
                    returnedAt));
            var duplicate = Assert.IsType<CsobBrowserReturnObservation>(
                await ScheduleReturnAsync(
                    payment.ProviderReference!,
                    returnedAt.AddMinutes(5)));
            var recovery = await LoadRecoveryAsync(payment.Id);

            Assert.True(first.IsFirstObservation);
            Assert.False(duplicate.IsFirstObservation);
            Assert.Equal(scheduledAt, recovery.NextAttemptAt);
            Assert.Equal(returnedAt, recovery.LastBrowserReturnAt);
            Assert.Equal(
                (int)PaymentReconciliationState.Scheduled,
                recovery.State);
            Assert.Equal(0, recovery.AttemptCount);
        }
        finally
        {
            await DeletePaymentAsync(payment.Id);
        }
    }

    [Fact]
    public async Task ScheduleFromReturnAsync_DoesNotRewriteActiveLeaseScheduling()
    {
        var payment = CreatePendingPayment("payleased00001");
        var scheduledAt = CreatedAt.AddMinutes(10);
        var leaseDuration = TimeSpan.FromMinutes(3);
        var returnedAt = CreatedAt.AddMinutes(11);

        try
        {
            await AddPaymentAsync(payment);
            await ScheduleLongOpenAsync(payment, scheduledAt);
            var claim = Assert.Single(
                await ClaimAsync(scheduledAt, leaseDuration));

            var observation = Assert.IsType<CsobBrowserReturnObservation>(
                await ScheduleReturnAsync(
                    payment.ProviderReference!,
                    returnedAt));
            var recovery = await LoadRecoveryAsync(payment.Id);

            Assert.True(observation.IsFirstObservation);
            Assert.Equal(
                (int)PaymentReconciliationState.Leased,
                recovery.State);
            Assert.Equal(scheduledAt, recovery.NextAttemptAt);
            Assert.Equal(claim.LeaseToken, recovery.LeaseToken);
            Assert.Equal(scheduledAt + leaseDuration, recovery.LeaseExpiresAt);
            Assert.Equal(0, recovery.AttemptCount);
        }
        finally
        {
            await DeletePaymentAsync(payment.Id);
        }
    }

    [Theory]
    [InlineData(PaymentReconciliationState.Completed)]
    [InlineData(PaymentReconciliationState.RequiresAttention)]
    public async Task ScheduleFromReturnAsync_DoesNotReopenClosedRecovery(
        PaymentReconciliationState terminalState)
    {
        var providerReference = terminalState ==
            PaymentReconciliationState.Completed
                ? "payterminal001"
                : "payterminal002";
        var payment = CreatePendingPayment(providerReference);
        var scheduledAt = CreatedAt.AddMinutes(10);
        var attemptedAt = CreatedAt.AddMinutes(11);

        try
        {
            await AddPaymentAsync(payment);
            await ScheduleLongOpenAsync(payment, scheduledAt);
            var claim = Assert.Single(
                await ClaimAsync(scheduledAt, TimeSpan.FromMinutes(2)));

            using (var transitionScope = _factory.Services.CreateScope())
            {
                var repository = transitionScope.ServiceProvider
                    .GetRequiredService<ICsobPaymentRecoveryRepository>();
                var transitioned = terminalState ==
                    PaymentReconciliationState.Completed
                        ? await repository.MarkCompletedAsync(
                            claim,
                            attemptedAt,
                            gatewayPaymentStatus: 7,
                            resultCode: 0)
                        : await repository.MarkRequiresAttentionAsync(
                            claim,
                            attemptedAt,
                            gatewayPaymentStatus: 2,
                            resultCode: 100,
                            error: "manual review");
                Assert.True(transitioned);
            }

            var observation = Assert.IsType<CsobBrowserReturnObservation>(
                await ScheduleReturnAsync(
                    payment.ProviderReference!,
                    attemptedAt.AddMinutes(1)));
            var recovery = await LoadRecoveryAsync(payment.Id);

            Assert.True(observation.IsFirstObservation);
            Assert.Equal((int)terminalState, recovery.State);
            Assert.Equal(1, recovery.AttemptCount);
            Assert.Empty(
                await ClaimAsync(
                    attemptedAt.AddHours(1),
                    TimeSpan.FromMinutes(2)));
        }
        finally
        {
            await DeletePaymentAsync(payment.Id);
        }
    }

    [Fact]
    public async Task ScheduleFromReturnAsync_InconsistentPaymentReferenceRelationshipFailsClosed()
    {
        var firstPayment = CreatePendingPayment("payrelation0001");
        var secondPayment = CreatePendingPayment("payrelation0002");

        try
        {
            await AddPaymentAsync(firstPayment);
            await AddPaymentAsync(secondPayment);
            await ScheduleReturnAsync(
                firstPayment.ProviderReference!,
                CreatedAt.AddMinutes(1));

            using (var corruptScope = _factory.Services.CreateScope())
            {
                await corruptScope.ServiceProvider
                    .GetRequiredService<FuaPayDbContext>()
                    .Database.ExecuteSqlInterpolatedAsync(
                        $"""
                        UPDATE payments.csob_payment_reconciliation
                        SET provider_reference = {secondPayment.ProviderReference}
                        WHERE payment_id = {firstPayment.Id}
                        """);
            }

            using var verifyScope = _factory.Services.CreateScope();
            await Assert.ThrowsAsync<InvalidDataException>(
                () => verifyScope.ServiceProvider
                    .GetRequiredService<ICsobPaymentRecoveryRepository>()
                    .ScheduleFromReturnAsync(
                        CreateVerifiedReturn(
                            secondPayment.ProviderReference!),
                        CreatedAt.AddMinutes(2)));
        }
        finally
        {
            await DeletePaymentAsync(firstPayment.Id);
            await DeletePaymentAsync(secondPayment.Id);
        }
    }

    [Fact]
    public async Task UncertainInitializationWithoutObservedPayId_RequiresAttention()
    {
        var payment = CreateCreatedPayment();
        var initiation = new PaymentInitiation(
            payment.Id,
            PaymentProvider.Csob,
            9_100_000_001L,
            Guid.NewGuid(),
            CreatedAt);

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

            using var recoveryScope = _factory.Services.CreateScope();
            var recoveryRepository = recoveryScope.ServiceProvider
                .GetRequiredService<ICsobPaymentRecoveryRepository>();
            var seeded = await recoveryRepository
                .RegisterUnrecoverableUncertainAsync(
                    CreatedAt.AddMinutes(1),
                    limit: 20);

            Assert.Equal(1, seeded);

            var item = Assert.Single((await recoveryScope.ServiceProvider
                    .GetRequiredService<IPaymentReconciliationQueries>()
                    .ListOpenAsync(100))
, candidate => candidate.PaymentId == payment.Id);

            Assert.Equal(
                PaymentReconciliationState.RequiresAttention,
                item.State);
            Assert.Null(item.ProviderReference);
            Assert.NotNull(item.LastError);
            Assert.Contains(
                "payment/init retry",
                item.LastError,
                StringComparison.Ordinal);
        }
        finally
        {
            await DeletePaymentAsync(payment.Id);
        }
    }

    [Fact]
    public async Task StaleInProgress_ConcurrentWorkersCreateOneRestartSafeDisposition()
    {
        var payment = CreateCreatedPayment();
        var initiation = new PaymentInitiation(
            payment.Id,
            PaymentProvider.Csob,
            9_100_000_002L,
            Guid.NewGuid(),
            CreatedAt);

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

            Assert.Equal(
                0,
                await RegisterStaleInProgressAsync(
                    CreatedAt,
                    CreatedAt.AddSeconds(30)));

            var dispositions = await Task.WhenAll(
                Enumerable.Range(0, 4)
                    .Select(_ => RegisterStaleInProgressAsync(
                        CreatedAt.AddSeconds(2),
                        CreatedAt.AddMinutes(1))));

            Assert.Equal(1, dispositions.Sum());

            using var verifyScope = _factory.Services.CreateScope();
            var restored = Assert.IsType<PaymentInitiation>(
                await verifyScope.ServiceProvider
                    .GetRequiredService<IPaymentInitiationRepository>()
                    .FindByPaymentIdAsync(payment.Id));

            Assert.Equal(PaymentInitiationState.Uncertain, restored.State);
            Assert.Null(restored.ObservedProviderReference);
            Assert.Contains(
                "payment/init retry",
                restored.LastError,
                StringComparison.Ordinal);

            var recoveryRepository = verifyScope.ServiceProvider
                .GetRequiredService<ICsobPaymentRecoveryRepository>();
            Assert.Equal(
                1,
                await recoveryRepository.RegisterUnrecoverableUncertainAsync(
                    CreatedAt.AddMinutes(1),
                    limit: 20));
            Assert.Equal(
                0,
                await recoveryRepository.RegisterStaleInProgressAsync(
                    CreatedAt.AddMinutes(2),
                    CreatedAt.AddMinutes(3),
                    limit: 20));
        }
        finally
        {
            await DeletePaymentAsync(payment.Id);
        }
    }

    [Fact]
    public async Task RecoverableUncertainInitialization_TwoScopesScheduleExactlyOnceWithoutInitializing()
    {
        var payment = CreateCreatedPayment();
        var initiation = new PaymentInitiation(
            payment.Id,
            PaymentProvider.Csob,
            9_100_000_003L,
            Guid.NewGuid(),
            CreatedAt);

        try
        {
            using (var createScope = _factory.Services.CreateScope())
            {
                await createScope.ServiceProvider
                    .GetRequiredService<IPaymentRepository>()
                    .AddPreparedAsync(payment, initiation);
            }

            using (var observationScope = _factory.Services.CreateScope())
            {
                var repository = observationScope.ServiceProvider
                    .GetRequiredService<IPaymentInitiationRepository>();
                var persisted = Assert.IsType<PaymentInitiation>(
                    await repository.FindByPaymentIdAsync(payment.Id));
                persisted.Begin(CreatedAt.AddSeconds(1));
                await repository.SaveAsync(persisted);

                persisted = Assert.IsType<PaymentInitiation>(
                    await repository.FindByPaymentIdAsync(payment.Id));
                persisted.MarkUncertain(
                    "candidate pending verification",
                    CreatedAt.AddSeconds(2),
                    "payrecover001",
                    new Uri("https://example.test/process/payrecover001"));
                await repository.SaveAsync(persisted);
            }

            using var firstScope = _factory.Services.CreateScope();
            using var secondScope = _factory.Services.CreateScope();
            var results = await Task.WhenAll(
                firstScope.ServiceProvider
                    .GetRequiredService<ICsobPaymentRecoveryRepository>()
                    .ScheduleRecoverableUncertainAsync(
                        CreatedAt.AddMinutes(1),
                        limit: 20),
                secondScope.ServiceProvider
                    .GetRequiredService<ICsobPaymentRecoveryRepository>()
                    .ScheduleRecoverableUncertainAsync(
                        CreatedAt.AddMinutes(1),
                        limit: 20));

            Assert.Equal(1, results.Sum());

            using var verifyScope = _factory.Services.CreateScope();
            var restoredPayment = Assert.IsType<Payment>(
                await verifyScope.ServiceProvider
                    .GetRequiredService<IPaymentRepository>()
                    .FindByIdAsync(payment.Id));
            var restoredInitiation = Assert.IsType<PaymentInitiation>(
                await verifyScope.ServiceProvider
                    .GetRequiredService<IPaymentInitiationRepository>()
                    .FindByPaymentIdAsync(payment.Id));
            var recovery = Assert.Single(
                await verifyScope.ServiceProvider
                    .GetRequiredService<IPaymentReconciliationQueries>()
                    .ListOpenAsync(100),
                item => item.PaymentId == payment.Id);

            Assert.Equal(PaymentStatus.Created, restoredPayment.Status);
            Assert.Equal(
                PaymentInitiationState.Uncertain,
                restoredInitiation.State);
            Assert.Equal(
                PaymentReconciliationState.Scheduled,
                recovery.State);
            Assert.Equal("payrecover001", recovery.ProviderReference);
        }
        finally
        {
            await DeletePaymentAsync(payment.Id);
        }
    }

    [Fact]
    public async Task MarkCompletedAsync_PersistsAuthoritativeExpiryEvidence()
    {
        var payment = CreatePendingPayment("payexpired0130");
        var scheduledAt = CreatedAt.AddMinutes(1);

        try
        {
            await AddPaymentAsync(payment);
            await ScheduleReturnAsync(
                payment.ProviderReference!,
                scheduledAt);
            var claim = Assert.Single(
                await ClaimAsync(
                    scheduledAt,
                    TimeSpan.FromMinutes(2)));

            using (var transitionScope = _factory.Services.CreateScope())
            {
                var repository = transitionScope.ServiceProvider
                    .GetRequiredService<ICsobPaymentRecoveryRepository>();

                Assert.True(
                    await repository.MarkCompletedAsync(
                        claim,
                        scheduledAt.AddSeconds(1),
                        gatewayPaymentStatus: 6,
                        resultCode: 130));
            }

            using var verifyScope = _factory.Services.CreateScope();
            var dbContext = verifyScope.ServiceProvider
                .GetRequiredService<FuaPayDbContext>();
            await dbContext.Database.OpenConnectionAsync();
            await using var command = dbContext.Database
                .GetDbConnection()
                .CreateCommand();
            command.CommandText =
                """
                SELECT state,
                       last_gateway_payment_status,
                       last_result_code,
                       completed_at
                FROM payments.csob_payment_reconciliation
                WHERE payment_id = @paymentId
                """;
            var paymentIdParameter = command.CreateParameter();
            paymentIdParameter.ParameterName = "paymentId";
            paymentIdParameter.Value = payment.Id;
            command.Parameters.Add(paymentIdParameter);
            await using var reader = await command.ExecuteReaderAsync();

            Assert.True(await reader.ReadAsync());

            Assert.Equal(
                (int)PaymentReconciliationState.Completed,
                reader.GetInt32(0));
            Assert.Equal(6, reader.GetInt32(1));
            Assert.Equal(130, reader.GetInt32(2));
            Assert.Equal(
                scheduledAt.AddSeconds(1),
                reader.GetFieldValue<DateTimeOffset>(3));
            Assert.False(await reader.ReadAsync());
        }
        finally
        {
            await DeletePaymentAsync(payment.Id);
        }
    }

    [Fact]
    public async Task RecoveryAuditInsertFailure_RollsBackClaimTransition()
    {
        var payment = CreatePendingPayment("payauditfail01");
        var scheduledAt = CreatedAt.AddMinutes(1);
        var duplicateAudit = AuditEntry.ForProcess(
            "database-test",
            "test.audit-duplicate",
            "test",
            Guid.NewGuid().ToString(),
            "Audit row used to force a duplicate-key failure.",
            CreatedAt);

        try
        {
            await AddPaymentAsync(payment);
            await ScheduleReturnAsync(
                payment.ProviderReference!,
                scheduledAt);

            using (var auditScope = _factory.Services.CreateScope())
            {
                await auditScope.ServiceProvider
                    .GetRequiredService<IAuditTrail>()
                    .WriteAsync(duplicateAudit);
            }

            using (var processorScope = _factory.Services.CreateScope())
            {
                var services = processorScope.ServiceProvider;
                var processor = new CsobPaymentRecoveryProcessor(
                    services.GetRequiredService<ICsobPaymentRecoveryRepository>(),
                    new PendingReconciliationService(payment.Id),
                    services.GetRequiredService<IApplicationTransaction>(),
                    new CsobReconciliationConfiguration(
                        Enabled: true,
                        PollInterval: TimeSpan.FromSeconds(15),
                        PendingMinimumAge: TimeSpan.FromSeconds(15),
                        LeaseDuration: TimeSpan.FromMinutes(3),
                        BaseBackoff: TimeSpan.FromSeconds(15),
                        MaximumBackoff: TimeSpan.FromMinutes(3),
                        MaximumAttempts: 4,
                        BatchSize: 20),
                    new FixedTimeProvider(scheduledAt),
                    new DuplicateAuditTrail(
                        services.GetRequiredService<IAuditTrail>(),
                        duplicateAudit),
                    NullLogger<CsobPaymentRecoveryProcessor>.Instance);

                await Assert.ThrowsAsync<DbUpdateException>(
                    () => processor.RunOnceAsync());
            }

            using var verifyScope = _factory.Services.CreateScope();
            var item = Assert.Single(
                await verifyScope.ServiceProvider
                    .GetRequiredService<IPaymentReconciliationQueries>()
                    .ListOpenAsync(100),
                candidate => candidate.PaymentId == payment.Id);

            Assert.Equal(PaymentReconciliationState.Leased, item.State);
            Assert.Equal(0, item.AttemptCount);
            Assert.Null(item.LastAttemptAt);
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

            await DeletePaymentAsync(payment.Id);
        }
    }

    private async Task<CsobBrowserReturnObservation?> ScheduleReturnAsync(
        string providerReference,
        DateTimeOffset observedAt)
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider
            .GetRequiredService<ICsobPaymentRecoveryRepository>()
            .ScheduleFromReturnAsync(
                CreateVerifiedReturn(providerReference),
                observedAt);
    }

    private async Task<CsobBrowserReturnObservation?>
        ScheduleVerifiedReturnAsync(
            CsobVerifiedPaymentReturn verifiedReturn,
            DateTimeOffset observedAt)
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider
            .GetRequiredService<ICsobPaymentRecoveryRepository>()
            .ScheduleFromReturnAsync(
                verifiedReturn,
                observedAt);
    }

    private static CsobVerifiedPaymentReturn CreateVerifiedReturn(
        string providerReference) =>
        new(
            providerReference,
            "20260814110000",
            0,
            "OK",
            3,
            AuthCode: null,
            MerchantData: null,
            StatusDetail: null,
            $"{providerReference}|20260814110000|0|OK|3",
            "signature");

    private static CsobVerifiedPaymentReturn CreateVerifiedExpiryReturn(
        string providerReference) =>
        new(
            providerReference,
            "20260814113100",
            130,
            "Session expired",
            6,
            AuthCode: null,
            MerchantData: "AQIDBA==",
            StatusDetail: null,
            $"{providerReference}|20260814113100|130|Session expired|6|AQIDBA==",
            "signature");

    private async Task<IReadOnlyList<CsobPaymentRecoveryClaim>> ClaimAsync(
        DateTimeOffset now,
        TimeSpan leaseDuration)
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider
            .GetRequiredService<ICsobPaymentRecoveryRepository>()
            .ClaimDueAsync(
                now,
                leaseDuration,
                limit: 20);
    }

    private async Task ScheduleLongOpenAsync(
        Payment payment,
        DateTimeOffset scheduledAt)
    {
        using var scope = _factory.Services.CreateScope();
        var scheduled = await scope.ServiceProvider
            .GetRequiredService<ICsobPaymentRecoveryRepository>()
            .ScheduleLongOpenPaymentsAsync(
                pendingBefore: payment.UpdatedAt.AddSeconds(1),
                scheduledAt,
                limit: 20);

        Assert.Equal(1, scheduled);
    }

    private async Task<RecoverySnapshot> LoadRecoveryAsync(
        Guid paymentId)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider
            .GetRequiredService<FuaPayDbContext>();

        return await dbContext.Database
            .SqlQuery<RecoverySnapshot>(
                $"""
                SELECT
                    state AS "State",
                    attempt_count AS "AttemptCount",
                    next_attempt_at AS "NextAttemptAt",
                    lease_token AS "LeaseToken",
                    lease_expires_at AS "LeaseExpiresAt",
                    last_attempt_at AS "LastAttemptAt",
                    last_browser_return_at AS "LastBrowserReturnAt",
                    last_gateway_payment_status AS "LastGatewayPaymentStatus",
                    last_result_code AS "LastResultCode",
                    last_error AS "LastError"
                FROM payments.csob_payment_reconciliation
                WHERE payment_id = {paymentId}
                """)
            .SingleAsync();
    }

    private sealed record RecoverySnapshot(
        int State,
        int AttemptCount,
        DateTimeOffset NextAttemptAt,
        Guid? LeaseToken,
        DateTimeOffset? LeaseExpiresAt,
        DateTimeOffset? LastAttemptAt,
        DateTimeOffset? LastBrowserReturnAt,
        int? LastGatewayPaymentStatus,
        int? LastResultCode,
        string? LastError);

    private async Task<int> RegisterStaleInProgressAsync(
        DateTimeOffset staleBefore,
        DateTimeOffset observedAt)
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider
            .GetRequiredService<ICsobPaymentRecoveryRepository>()
            .RegisterStaleInProgressAsync(
                staleBefore,
                observedAt,
                limit: 20);
    }

    private async Task AddPaymentAsync(Payment payment)
    {
        using var scope = _factory.Services.CreateScope();
        await scope.ServiceProvider
            .GetRequiredService<IPaymentRepository>()
            .AddAsync(payment);
    }

    private static Payment CreatePendingPayment(string providerReference)
    {
        var payment = CreateCreatedPayment();
        payment.MarkPending(providerReference, CreatedAt);
        return payment;
    }

    private static Payment CreateCreatedPayment()
    {
        return new Payment(
            Guid.NewGuid(),
            Guid.NewGuid(),
            PaymentPurposeType.CreditTopUp,
            jobId: null,
            new Money(50_000),
            PaymentProvider.Csob,
            CreatedAt,
            Guid.NewGuid());
    }

    private async Task DeletePaymentAsync(Guid paymentId)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider
            .GetRequiredService<FuaPayDbContext>();

        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            DELETE FROM audit.events
            WHERE entity_type = 'payment'
              AND entity_id = {paymentId.ToString()}
            """);

        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            DELETE FROM payments.payments
            WHERE id = {paymentId}
            """);
    }

    private sealed class PendingReconciliationService :
        ICsobPaymentReconciliationService
    {
        private readonly Guid _paymentId;

        public PendingReconciliationService(Guid paymentId)
        {
            _paymentId = paymentId;
        }

        public Task<CsobPaymentReconciliationResult> ReconcileAsync(
            Guid paymentId,
            string payId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new CsobPaymentReconciliationResult(
                _paymentId,
                PaymentStatus.Pending,
                GatewayPaymentStatus: 2,
                GatewayResultCode: 0,
                StateChanged: false));
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
