using FuaPay.Web.BuildingBlocks.Application;
using FuaPay.Web.BuildingBlocks.Auditing;
using FuaPay.Web.BuildingBlocks.Domain;
using FuaPay.Web.BuildingBlocks.Persistence;
using FuaPay.Web.Modules.Jobs.Application;
using FuaPay.Web.Modules.Jobs.Domain;
using FuaPay.Web.Modules.Payments.Application;
using FuaPay.Web.Modules.Payments.Domain;
using FuaPay.Web.Modules.Payments.Infrastructure.Csob;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FuaPay.DatabaseTests;

public sealed class CsobCardJobSettlementReturnPersistenceTests :
    IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly DateTimeOffset TestTime =
        new(2026, 9, 9, 10, 0, 0, TimeSpan.Zero);

    private static int _jobNumberSequence =
        Random.Shared.Next(100_000, 900_000);

    private readonly WebApplicationFactory<Program> _factory;

    public CsobCardJobSettlementReturnPersistenceTests(
        WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ConcurrentStableOperationSendsOneReversePut()
    {
        var scenario = await SeedScenarioAsync();
        var gateway = new CoordinatedGateway();
        var command = new CardJobSettlementReturnCommand(
            Guid.NewGuid(),
            scenario.PaymentId,
            Guid.NewGuid(),
            "Concurrent full CardJob return");

        try
        {
            var results = await Task.WhenAll(
                RunAsync(command, gateway),
                RunAsync(command, gateway));

            Assert.All(
                results,
                result => Assert.Equal(
                    CardJobSettlementReturnOutcome.ReverseCompleted,
                    result.Outcome));
            Assert.Equal(1, gateway.ReverseCalls);
            Assert.Equal(1, gateway.StatusCalls);

            using var verifyScope = _factory.Services.CreateScope();
            var returnRepository = verifyScope.ServiceProvider
                .GetRequiredService<ISettlementReturnRepository>();
            var attemptRepository = verifyScope.ServiceProvider
                .GetRequiredService<
                    ISettlementReturnProviderAttemptRepository>();
            var settlementReturn = Assert.IsType<SettlementReturn>(
                await returnRepository.FindByRequestIdAsync(
                    command.OperationId));
            var attempt = Assert.IsType<SettlementReturnProviderAttempt>(
                await attemptRepository.FindByIdAsync(
                    command.OperationId));

            Assert.Equal(
                SettlementReturnState.Completed,
                settlementReturn.State);
            Assert.Equal(
                SettlementReturnProviderAttemptState.Confirmed,
                attempt.State);
            Assert.Equal(scenario.CustomerUserId, settlementReturn.CustomerUserId);
            Assert.Equal(scenario.Amount, settlementReturn.Amount);
            Assert.Equal(scenario.PayId, attempt.ProviderReference);
        }
        finally
        {
            await DeleteScenarioAsync(scenario);
        }
    }

    [Fact]
    public async Task ConcurrentSettledTransitionSendsOneFullRefundPut()
    {
        var scenario = await SeedScenarioAsync();
        var gateway = new CoordinatedRefundGateway();
        var command = new CardJobSettlementReturnCommand(
            Guid.NewGuid(),
            scenario.PaymentId,
            Guid.NewGuid(),
            "Concurrent settled CardJob full refund");

        Task<CardJobSettlementReturnResult>? staleReverseTask = null;

        try
        {
            staleReverseTask = RunAsync(command, gateway);
            await gateway.WaitUntilReverseStartedAsync();

            var recoveryResult = await RunAsync(command, gateway);
            Assert.Equal(
                CardJobSettlementReturnOutcome.RefundCompleted,
                recoveryResult.Outcome);

            gateway.ReleaseReverse();
            var staleReverseResult = await staleReverseTask;
            var results = new[] { recoveryResult, staleReverseResult };

            Assert.All(
                results,
                result => Assert.Equal(
                    CardJobSettlementReturnOutcome.RefundCompleted,
                    result.Outcome));
            Assert.Equal(1, gateway.ReverseCalls);
            Assert.Equal(1, gateway.StatusCalls);
            Assert.Equal(1, gateway.RefundCalls);
            Assert.Null(gateway.RefundAmountMinorUnits);

            using var verifyScope = _factory.Services.CreateScope();
            var services = verifyScope.ServiceProvider;
            var settlementReturn = Assert.IsType<SettlementReturn>(
                await services.GetRequiredService<
                        ISettlementReturnRepository>()
                    .FindByRequestIdAsync(command.OperationId));
            var attempts = await services.GetRequiredService<
                    ISettlementReturnProviderAttemptRepository>()
                .ListBySettlementReturnIdAsync(settlementReturn.Id);

            Assert.Equal(SettlementReturnState.Completed, settlementReturn.State);
            Assert.Equal(2, attempts.Count);
            Assert.Equal(
                SettlementReturnProviderAttemptState.Rejected,
                attempts.Single(attempt =>
                    attempt.Operation ==
                        SettlementReturnProviderOperation.Reverse).State);
            var confirmedRefund = attempts.Single(attempt =>
                attempt.Operation ==
                    SettlementReturnProviderOperation.Refund);
            Assert.Equal(
                SettlementReturnProviderAttemptState.Confirmed,
                confirmedRefund.State);
            Assert.All(
                results,
                result => Assert.Equal(
                    confirmedRefund.Id,
                    result.ProviderAttemptId));
        }
        finally
        {
            gateway.ReleaseReverse();

            if (staleReverseTask is { IsCompleted: false })
            {
                try
                {
                    await staleReverseTask;
                }
                catch
                {
                    // Preserve the primary test failure while ensuring cleanup
                    // does not race a still-running provider call.
                }
            }

            await DeleteScenarioAsync(scenario);
        }
    }

    [Fact]
    public async Task RefundProcessingPersistsAuditEvent()
    {
        var scenario = await SeedScenarioAsync();
        var gateway = new DirectRefundGateway(refundPaymentStatus: 9);
        var command = new CardJobSettlementReturnCommand(
            Guid.NewGuid(),
            scenario.PaymentId,
            Guid.NewGuid(),
            "Persist refund processing audit");

        try
        {
            var result = await RunAsync(command, gateway);

            Assert.Equal(
                CardJobSettlementReturnOutcome.RefundProcessing,
                result.Outcome);
            Assert.Equal(1, gateway.ReverseCalls);
            Assert.Equal(1, gateway.RefundCalls);

            using var verifyScope = _factory.Services.CreateScope();
            var services = verifyScope.ServiceProvider;
            var settlementReturn = Assert.IsType<SettlementReturn>(
                await services.GetRequiredService<
                        ISettlementReturnRepository>()
                    .FindByRequestIdAsync(command.OperationId));
            var auditCount = await CountAuditEventsAsync(
                services,
                settlementReturn.Id,
                "settlement-return.card-job.refund-processing");

            Assert.Equal(1, auditCount);
        }
        finally
        {
            await DeleteScenarioAsync(scenario);
        }
    }

    [Fact]
    public async Task AmbiguousRefundPersistsRequiresAttentionAuditEvent()
    {
        var scenario = await SeedScenarioAsync();
        var gateway = new DirectRefundGateway(
            refundException: new HttpRequestException(
                "simulated ambiguous refund transport failure"));
        var command = new CardJobSettlementReturnCommand(
            Guid.NewGuid(),
            scenario.PaymentId,
            Guid.NewGuid(),
            "Persist ambiguous refund audit");

        try
        {
            var result = await RunAsync(command, gateway);

            Assert.Equal(
                CardJobSettlementReturnOutcome.RequiresAttention,
                result.Outcome);
            Assert.Equal(1, gateway.ReverseCalls);
            Assert.Equal(1, gateway.RefundCalls);

            using var verifyScope = _factory.Services.CreateScope();
            var services = verifyScope.ServiceProvider;
            var settlementReturn = Assert.IsType<SettlementReturn>(
                await services.GetRequiredService<
                        ISettlementReturnRepository>()
                    .FindByRequestIdAsync(command.OperationId));
            var auditCount = await CountAuditEventsAsync(
                services,
                settlementReturn.Id,
                "settlement-return.card-job.refund-requires-attention");

            Assert.Equal(1, auditCount);
        }
        finally
        {
            await DeleteScenarioAsync(scenario);
        }
    }

    [Fact]
    public async Task DurableInProgressSurvivesRestartAndRecoversStatusOnly()
    {
        var scenario = await SeedScenarioAsync();
        var command = new CardJobSettlementReturnCommand(
            Guid.NewGuid(),
            scenario.PaymentId,
            Guid.NewGuid(),
            "Restart recovery for full CardJob return");

        try
        {
            using (var prepareScope = _factory.Services.CreateScope())
            {
                var services = prepareScope.ServiceProvider;
                var returnRepository = services.GetRequiredService<
                    ISettlementReturnRepository>();
                var attemptRepository = services.GetRequiredService<
                    ISettlementReturnProviderAttemptRepository>();
                var paymentRepository = services.GetRequiredService<
                    IPaymentRepository>();
                var timeProvider = new FixedTimeProvider(TestTime);
                var attemptService =
                    new SettlementReturnProviderAttemptService(
                        attemptRepository,
                        returnRepository,
                        paymentRepository,
                        timeProvider);
                var transaction = services.GetRequiredService<
                    IApplicationTransaction>();

                await transaction.ExecuteAsync(async cancellationToken =>
                {
                    var settlementReturn = new SettlementReturn(
                        Guid.NewGuid(),
                        command.OperationId,
                        SettlementReturnKind.CardJob,
                        scenario.PaymentId,
                        scenario.JobId,
                        scenario.CustomerUserId,
                        command.AdministratorUserId,
                        scenario.Amount,
                        command.Reason,
                        TestTime);
                    await returnRepository.AddAsync(
                        settlementReturn,
                        cancellationToken);
                    await attemptService.CreateAsync(
                        new CreateSettlementReturnProviderAttemptCommand(
                            command.OperationId,
                            settlementReturn.Id,
                            SettlementReturnProviderOperation.Reverse),
                        cancellationToken);
                    settlementReturn.Begin(TestTime);
                    await attemptService.BeginAsync(
                        command.OperationId,
                        cancellationToken);
                    await returnRepository.SaveAsync(
                        settlementReturn,
                        cancellationToken);
                    return true;
                });
            }

            using (var readScope = _factory.Services.CreateScope())
            {
                var existing = await readScope.ServiceProvider
                    .GetRequiredService<ISettlementReturnQueries>()
                    .FindByOriginalPaymentIdsAsync([scenario.PaymentId]);
                var item = Assert.Single(existing).Value;

                Assert.Equal(command.OperationId, item.RequestId);
                Assert.Equal(
                    SettlementReturnState.InProgress,
                    item.State);
                Assert.Equal(
                    SettlementReturnProviderAttemptState.InProgress,
                    item.ProviderAttemptState);
                Assert.True(item.CanRecoverProviderAttempt);
            }

            var gateway = new StatusOnlyGateway(scenario.PayId);
            var result = await RunAsync(command, gateway);

            Assert.Equal(
                CardJobSettlementReturnOutcome.ReverseCompleted,
                result.Outcome);
            Assert.False(result.ReverseRequestSent);
            Assert.Equal(0, gateway.ReverseCalls);
            Assert.Equal(1, gateway.StatusCalls);

            using var verifyScope = _factory.Services.CreateScope();
            var settlementReturn = Assert.IsType<SettlementReturn>(
                await verifyScope.ServiceProvider
                    .GetRequiredService<ISettlementReturnRepository>()
                    .FindByRequestIdAsync(command.OperationId));
            var attempt = Assert.IsType<SettlementReturnProviderAttempt>(
                await verifyScope.ServiceProvider
                    .GetRequiredService<
                        ISettlementReturnProviderAttemptRepository>()
                    .FindByIdAsync(command.OperationId));

            Assert.Equal(
                SettlementReturnState.Completed,
                settlementReturn.State);
            Assert.Equal(
                SettlementReturnProviderAttemptState.Confirmed,
                attempt.State);

            var completed = await verifyScope.ServiceProvider
                .GetRequiredService<ISettlementReturnQueries>()
                .FindByOriginalPaymentIdsAsync([scenario.PaymentId]);
            var completedItem = Assert.Single(completed).Value;
            Assert.Equal(command.OperationId, completedItem.RequestId);
            Assert.True(completedItem.IsCompletedReverse);
            Assert.False(completedItem.CanRecoverProviderAttempt);
        }
        finally
        {
            await DeleteScenarioAsync(scenario);
        }
    }

    private async Task<CardJobSettlementReturnResult> RunAsync(
        CardJobSettlementReturnCommand command,
        ICsobGatewayClient gateway)
    {
        using var scope = _factory.Services.CreateScope();
        return await CreateService(scope.ServiceProvider, gateway)
            .ReturnAsync(command);
    }

    private static CsobCardJobSettlementReturnService CreateService(
        IServiceProvider services,
        ICsobGatewayClient gateway)
    {
        var returnRepository = services.GetRequiredService<
            ISettlementReturnRepository>();
        var attemptRepository = services.GetRequiredService<
            ISettlementReturnProviderAttemptRepository>();
        var paymentRepository = services.GetRequiredService<
            IPaymentRepository>();
        var timeProvider = new FixedTimeProvider(TestTime);

        return new CsobCardJobSettlementReturnService(
            services.GetRequiredService<IJobRepository>(),
            services.GetRequiredService<IJobPaymentCoordination>(),
            paymentRepository,
            returnRepository,
            attemptRepository,
            new SettlementReturnRegistrationService(returnRepository),
            new SettlementReturnProviderAttemptService(
                attemptRepository,
                returnRepository,
                paymentRepository,
                timeProvider),
            services.GetRequiredService<IApplicationTransaction>(),
            services.GetRequiredService<IAuditTrail>(),
            gateway,
            timeProvider);
    }

    private static Task<int> CountAuditEventsAsync(
        IServiceProvider services,
        Guid settlementReturnId,
        string action)
    {
        var dbContext = services.GetRequiredService<FuaPayDbContext>();
        return dbContext.Database.SqlQuery<int>(
                $"""
                SELECT count(*)::integer AS "Value"
                FROM audit.events
                WHERE entity_type = 'settlement-return'
                  AND entity_id = {settlementReturnId.ToString()}
                  AND action = {action}
                """)
            .SingleAsync();
    }

    private async Task<Scenario> SeedScenarioAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        var jobRepository = services.GetRequiredService<IJobRepository>();
        var paymentRepository = services.GetRequiredService<IPaymentRepository>();
        var customerId = Guid.NewGuid();
        var job = new Job(
            Guid.NewGuid(),
            NextJobNumber(),
            Guid.NewGuid(),
            customerId,
            Guid.NewGuid(),
            ServiceType.ThreeDPrint,
            "CSOB CardJob return",
            "PostgreSQL reverse orchestration test",
            new Money(12_500),
            TestTime.AddMinutes(-10));
        job.Publish(TestTime.AddMinutes(-9));
        await jobRepository.AddAsync(job, CancellationToken.None);

        var payId = Guid.NewGuid().ToString("N")[..15];
        var payment = new Payment(
            Guid.NewGuid(),
            customerId,
            PaymentPurposeType.Job,
            job.Id,
            job.Price,
            PaymentProvider.Csob,
            TestTime.AddMinutes(-8));
        payment.MarkPending(payId, TestTime.AddMinutes(-7));
        payment.Complete(TestTime.AddMinutes(-6));
        await paymentRepository.AddAsync(payment);

        job.ConfirmSettlement(
            JobSettlementType.DirectPayment,
            payment.Id,
            TestTime.AddMinutes(-6));
        await jobRepository.SaveAsync(job, CancellationToken.None);

        return new Scenario(
            job.Id,
            payment.Id,
            customerId,
            payment.Amount,
            payId);
    }

    private async Task DeleteScenarioAsync(Scenario scenario)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider
            .GetRequiredService<FuaPayDbContext>();
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            DELETE FROM audit.events
            WHERE entity_type = 'settlement-return'
              AND entity_id IN
              (
                  SELECT id::text
                  FROM payments.settlement_returns
                  WHERE original_payment_id = {scenario.PaymentId}
              )
            """);
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            DELETE FROM payments.settlement_return_provider_attempts
            WHERE settlement_return_id IN
            (
                SELECT id
                FROM payments.settlement_returns
                WHERE original_payment_id = {scenario.PaymentId}
            )
            """);
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            DELETE FROM payments.settlement_returns
            WHERE original_payment_id = {scenario.PaymentId}
            """);
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            DELETE FROM payments.payments
            WHERE id = {scenario.PaymentId}
            """);
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            DELETE FROM jobs.jobs
            WHERE id = {scenario.JobId}
            """);
    }

    private static string NextJobNumber()
    {
        var sequence = Interlocked.Increment(ref _jobNumberSequence);
        return $"CR-2026-{sequence:000000}";
    }

    private sealed class CoordinatedGateway : ICsobGatewayClient
    {
        private readonly TaskCompletionSource _statusObserved =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _reverseCalls;
        private int _statusCalls;

        public int ReverseCalls => Volatile.Read(ref _reverseCalls);

        public int StatusCalls => Volatile.Read(ref _statusCalls);

        public async Task<CsobPaymentReverseResult> ReverseAsync(
            string payId,
            CancellationToken cancellationToken = default)
        {
            Assert.Equal(1, Interlocked.Increment(ref _reverseCalls));
            await _statusObserved.Task.WaitAsync(
                TimeSpan.FromSeconds(10),
                cancellationToken);
            return new CsobPaymentReverseResult(
                payId,
                0,
                "OK",
                5,
                StatusDetail: null);
        }

        public Task<CsobPaymentStatusResult> GetStatusAsync(
            string payId,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _statusCalls);
            _statusObserved.TrySetResult();
            return Task.FromResult(new CsobPaymentStatusResult(
                payId,
                0,
                "OK",
                5,
                AuthCode: null,
                StatusDetail: null));
        }

        public Task<CsobEchoResult> EchoAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<CsobEchoResult> EchoPostAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<CsobPaymentInitResult> InitializeAsync(
            CsobPaymentInit payment,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<CsobPaymentRefundResult> RefundAsync(
            string payId,
            long? amountMinorUnits = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class StatusOnlyGateway : ICsobGatewayClient
    {
        private readonly string _payId;

        public StatusOnlyGateway(string payId)
        {
            _payId = payId;
        }

        public int ReverseCalls { get; private set; }

        public int StatusCalls { get; private set; }

        public Task<CsobPaymentReverseResult> ReverseAsync(
            string payId,
            CancellationToken cancellationToken = default)
        {
            ReverseCalls++;
            throw new InvalidOperationException(
                "Restart recovery must never issue payment/reverse.");
        }

        public Task<CsobPaymentStatusResult> GetStatusAsync(
            string payId,
            CancellationToken cancellationToken = default)
        {
            StatusCalls++;
            Assert.Equal(_payId, payId);
            return Task.FromResult(new CsobPaymentStatusResult(
                payId,
                0,
                "OK",
                5,
                AuthCode: null,
                StatusDetail: null));
        }

        public Task<CsobEchoResult> EchoAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<CsobEchoResult> EchoPostAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<CsobPaymentInitResult> InitializeAsync(
            CsobPaymentInit payment,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<CsobPaymentRefundResult> RefundAsync(
            string payId,
            long? amountMinorUnits = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class DirectRefundGateway : ICsobGatewayClient
    {
        private readonly int _refundPaymentStatus;
        private readonly Exception? _refundException;
        private int _reverseCalls;
        private int _refundCalls;

        public DirectRefundGateway(
            int refundPaymentStatus = 10,
            Exception? refundException = null)
        {
            _refundPaymentStatus = refundPaymentStatus;
            _refundException = refundException;
        }

        public int ReverseCalls => Volatile.Read(ref _reverseCalls);

        public int RefundCalls => Volatile.Read(ref _refundCalls);

        public Task<CsobPaymentReverseResult> ReverseAsync(
            string payId,
            CancellationToken cancellationToken = default)
        {
            Assert.Equal(1, Interlocked.Increment(ref _reverseCalls));
            return Task.FromResult(new CsobPaymentReverseResult(
                payId,
                150,
                "Payment already settled",
                8,
                StatusDetail: null));
        }

        public Task<CsobPaymentStatusResult> GetStatusAsync(
            string payId,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException(
                "Direct refund audit test must not require status recovery.");

        public Task<CsobPaymentRefundResult> RefundAsync(
            string payId,
            long? amountMinorUnits = null,
            CancellationToken cancellationToken = default)
        {
            Assert.Equal(1, Interlocked.Increment(ref _refundCalls));
            Assert.Null(amountMinorUnits);

            if (_refundException is not null)
            {
                return Task.FromException<CsobPaymentRefundResult>(
                    _refundException);
            }

            return Task.FromResult(new CsobPaymentRefundResult(
                payId,
                0,
                "OK",
                _refundPaymentStatus,
                AuthCode: null,
                StatusDetail: null));
        }

        public Task<CsobEchoResult> EchoAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<CsobEchoResult> EchoPostAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<CsobPaymentInitResult> InitializeAsync(
            CsobPaymentInit payment,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class CoordinatedRefundGateway : ICsobGatewayClient
    {
        private readonly TaskCompletionSource _reverseStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _releaseReverse =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _reverseCalls;
        private int _refundCalls;
        private int _statusCalls;

        public int ReverseCalls => Volatile.Read(ref _reverseCalls);

        public int RefundCalls => Volatile.Read(ref _refundCalls);

        public int StatusCalls => Volatile.Read(ref _statusCalls);

        public long? RefundAmountMinorUnits { get; private set; }

        public Task WaitUntilReverseStartedAsync(
            CancellationToken cancellationToken = default) =>
            _reverseStarted.Task.WaitAsync(
                TimeSpan.FromSeconds(10),
                cancellationToken);

        public void ReleaseReverse() => _releaseReverse.TrySetResult();

        public async Task<CsobPaymentReverseResult> ReverseAsync(
            string payId,
            CancellationToken cancellationToken = default)
        {
            Assert.Equal(1, Interlocked.Increment(ref _reverseCalls));
            _reverseStarted.TrySetResult();
            await _releaseReverse.Task.WaitAsync(
                TimeSpan.FromSeconds(10),
                cancellationToken);
            return new CsobPaymentReverseResult(
                payId,
                150,
                "Payment already settled",
                8,
                StatusDetail: null);
        }

        public Task<CsobPaymentStatusResult> GetStatusAsync(
            string payId,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _statusCalls);
            return Task.FromResult(new CsobPaymentStatusResult(
                payId,
                0,
                "OK",
                8,
                AuthCode: null,
                StatusDetail: null));
        }

        public Task<CsobPaymentRefundResult> RefundAsync(
            string payId,
            long? amountMinorUnits = null,
            CancellationToken cancellationToken = default)
        {
            Assert.Equal(1, Interlocked.Increment(ref _refundCalls));
            RefundAmountMinorUnits = amountMinorUnits;
            return Task.FromResult(new CsobPaymentRefundResult(
                payId,
                0,
                "OK",
                10,
                AuthCode: "TEST",
                StatusDetail: null));
        }

        public Task<CsobEchoResult> EchoAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<CsobEchoResult> EchoPostAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<CsobPaymentInitResult> InitializeAsync(
            CsobPaymentInit payment,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
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

    private sealed record Scenario(
        Guid JobId,
        Guid PaymentId,
        Guid CustomerUserId,
        Money Amount,
        string PayId);
}
