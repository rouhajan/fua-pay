using FuaPay.Web.BuildingBlocks.Application;
using FuaPay.Web.BuildingBlocks.Auditing;
using FuaPay.Web.BuildingBlocks.Persistence;
using FuaPay.Web.Modules.Credits.Application;
using FuaPay.Web.Modules.Credits.Domain;
using FuaPay.Web.Modules.Jobs.Application;
using FuaPay.Web.Modules.Payments.Application;
using FuaPay.Web.Modules.Payments.Domain;
using FuaPay.Web.Modules.Payments.Infrastructure.Csob;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FuaPay.DatabaseTests;

public sealed class CsobCardTopUpSettlementReturnPersistenceTests :
    IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly DateTimeOffset TestTime =
        new(2026, 9, 25, 10, 0, 0, TimeSpan.Zero);

    private readonly WebApplicationFactory<Program> _factory;

    public CsobCardTopUpSettlementReturnPersistenceTests(
        WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ConfirmedReverse_DebitsConsumesAndCompletesExactlyOnce()
    {
        var scenario = await SeedAsync(12_500);
        var gateway = new DirectGateway();
        var command = Command(scenario);

        try
        {
            var first = await RunAsync(command, gateway);
            var replay = await RunAsync(command, gateway);

            Assert.Equal(
                CardTopUpSettlementReturnOutcome.ReverseCompleted,
                first.Outcome);
            Assert.Equal(first.SettlementReturnId, replay.SettlementReturnId);
            Assert.Equal(1, gateway.ReverseCalls);

            using var scope = _factory.Services.CreateScope();
            var services = scope.ServiceProvider;
            var account = Assert.IsType<CreditAccount>(
                await services.GetRequiredService<ICreditAccountRepository>()
                    .FindByOwnerIdAsync(
                        scenario.CustomerUserId,
                        CancellationToken.None));
            Assert.Equal(Money.Zero, account.Balance);
            var debit = Assert.Single(
                account.Movements,
                item => item.Type == CreditMovementType.Debit);
            Assert.Equal(first.SettlementReturnId, debit.OperationId);
            Assert.Equal(
                $"Vrácení karetního dobití {scenario.PaymentId}",
                debit.Description);

            var settlementReturn = Assert.IsType<SettlementReturn>(
                await services.GetRequiredService<ISettlementReturnRepository>()
                    .FindByRequestIdAsync(command.RequestId));
            Assert.Equal(SettlementReturnState.Completed, settlementReturn.State);
            var hold = Assert.IsType<CreditReturnHold>(
                await services.GetRequiredService<ICreditReturnHoldRepository>()
                    .FindBySettlementReturnIdAsync(settlementReturn.Id));
            Assert.Equal(CreditReturnHoldState.Consumed, hold.State);
        }
        finally
        {
            await DeleteAsync(scenario);
        }
    }

    [Fact]
    public async Task ConcurrentStableRequest_SendsOneMutationAndOneDebit()
    {
        var scenario = await SeedAsync(12_500);
        var gateway = new CoordinatedGateway();
        var command = Command(scenario);

        try
        {
            var results = await Task.WhenAll(
                RunAsync(command, gateway),
                RunAsync(command, gateway));

            Assert.All(results, result => Assert.Equal(
                CardTopUpSettlementReturnOutcome.ReverseCompleted,
                result.Outcome));
            Assert.Equal(1, gateway.ReverseCalls);
            Assert.Equal(1, gateway.StatusCalls);

            using var scope = _factory.Services.CreateScope();
            var account = Assert.IsType<CreditAccount>(
                await scope.ServiceProvider
                    .GetRequiredService<ICreditAccountRepository>()
                    .FindByOwnerIdAsync(
                        scenario.CustomerUserId,
                        CancellationToken.None));
            Assert.Single(
                account.Movements,
                item => item.Type == CreditMovementType.Debit);
        }
        finally
        {
            await DeleteAsync(scenario);
        }
    }

    [Fact]
    public async Task AmbiguousMutation_KeepsHoldAndRestartIsStatusOnly()
    {
        var scenario = await SeedAsync(12_500);
        var ambiguous = new AmbiguousGateway();
        var command = Command(scenario);

        try
        {
            var first = await RunAsync(command, ambiguous);
            Assert.Equal(
                CardTopUpSettlementReturnOutcome.RequiresAttention,
                first.Outcome);
            Assert.Equal(1, ambiguous.ReverseCalls);

            var recovery = new StatusOnlyGateway();
            var replay = await RunAsync(command, recovery);
            Assert.Equal(
                CardTopUpSettlementReturnOutcome.ReverseCompleted,
                replay.Outcome);
            Assert.Equal(0, recovery.ReverseCalls);
            Assert.Equal(1, recovery.StatusCalls);
        }
        finally
        {
            await DeleteAsync(scenario);
        }
    }

    [Fact]
    public async Task InsufficientCredit_RollsBackReservationBeforeProviderMutation()
    {
        var scenario = await SeedAsync(1_000);
        var gateway = new DirectGateway();
        var command = Command(scenario);

        try
        {
            await Assert.ThrowsAsync<
                InsufficientAvailableCreditForReturnHoldException>(
                () => RunAsync(command, gateway));
            Assert.Equal(0, gateway.ReverseCalls);

            using var scope = _factory.Services.CreateScope();
            Assert.Null(await scope.ServiceProvider
                .GetRequiredService<ISettlementReturnRepository>()
                .FindByRequestIdAsync(command.RequestId));
        }
        finally
        {
            await DeleteAsync(scenario);
        }
    }

    [Fact]
    public async Task ReturnReservationVersusConcurrentSpend_HasOneSafeWinner()
    {
        var scenario = await SeedAsync(12_500);
        var gateway = new DirectGateway();
        var command = Command(scenario);
        var spendOperationId = Guid.NewGuid();

        try
        {
            var returnTask = CaptureAsync(() => RunAsync(command, gateway));
            var spendTask = CaptureAsync(async () =>
            {
                using var scope = _factory.Services.CreateScope();
                return await scope.ServiceProvider
                    .GetRequiredService<CreditService>()
                    .DebitAsync(
                        scenario.CustomerUserId,
                        spendOperationId,
                        new Money(12_500),
                        "Concurrent spend");
            });

            await Task.WhenAll(returnTask, spendTask);
            var returnResult = await returnTask;
            var spendResult = await spendTask;
            Assert.NotEqual(returnResult.Succeeded, spendResult.Succeeded);
            Assert.True(
                returnResult.Error is null or
                    InsufficientAvailableCreditForReturnHoldException);
            Assert.True(
                spendResult.Error is null or InsufficientCreditException);

            using var verifyScope = _factory.Services.CreateScope();
            var account = Assert.IsType<CreditAccount>(
                await verifyScope.ServiceProvider
                    .GetRequiredService<ICreditAccountRepository>()
                    .FindByOwnerIdAsync(
                        scenario.CustomerUserId,
                        CancellationToken.None));
            Assert.Equal(Money.Zero, account.Balance);
            Assert.Single(
                account.Movements,
                item => item.Type == CreditMovementType.Debit);
        }
        finally
        {
            await DeleteAsync(scenario);
        }
    }

    private static async Task<Captured<T>> CaptureAsync<T>(Func<Task<T>> action)
    {
        try
        {
            return new Captured<T>(true, await action(), null);
        }
        catch (Exception exception)
        {
            return new Captured<T>(false, default, exception);
        }
    }

    private async Task<CardTopUpSettlementReturnResult> RunAsync(
        CardTopUpSettlementReturnCommand command,
        ICsobGatewayClient gateway)
    {
        using var scope = _factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        var returnRepository = services.GetRequiredService<
            ISettlementReturnRepository>();
        var attemptRepository = services.GetRequiredService<
            ISettlementReturnProviderAttemptRepository>();
        var paymentRepository = services.GetRequiredService<IPaymentRepository>();
        var timeProvider = new FixedTimeProvider(TestTime);
        var service = new CsobCardJobSettlementReturnService(
            services.GetRequiredService<IJobRepository>(),
            services.GetRequiredService<IJobPaymentCoordination>(),
            services.GetRequiredService<ICreditAccountRepository>(),
            services.GetRequiredService<ICreditReturnHoldRepository>(),
            services.GetRequiredService<CreditAvailabilityService>(),
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
        return await ((ICardTopUpSettlementReturnService)service)
            .ReturnAsync(command);
    }

    private async Task<Scenario> SeedAsync(long creditMinorUnits)
    {
        using var scope = _factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        var customerId = Guid.NewGuid();
        var payId = Guid.NewGuid().ToString("N")[..15];
        var payment = new Payment(
            Guid.NewGuid(),
            customerId,
            PaymentPurposeType.CreditTopUp,
            jobId: null,
            new Money(12_500),
            PaymentProvider.Csob,
            TestTime.AddMinutes(-10),
            Guid.NewGuid());
        payment.MarkPending(payId, TestTime.AddMinutes(-9));
        payment.Complete(TestTime.AddMinutes(-8));
        await services.GetRequiredService<IPaymentRepository>()
            .AddAsync(payment);

        var account = new CreditAccount(Guid.NewGuid(), customerId);
        account.Credit(
            Guid.NewGuid(),
            new Money(creditMinorUnits),
            TestTime.AddMinutes(-7),
            "Test CSOB top-up");
        await services.GetRequiredService<ICreditAccountRepository>()
            .AddAsync(account, CancellationToken.None);

        return new Scenario(
            payment.Id,
            customerId,
            account.Id,
            payId);
    }

    private async Task DeleteAsync(Scenario scenario)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FuaPayDbContext>();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            DELETE FROM audit.events
            WHERE entity_type = 'settlement-return'
              AND entity_id IN
              (SELECT id::text FROM payments.settlement_returns
               WHERE original_payment_id = {scenario.PaymentId})
            """);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            DELETE FROM payments.settlement_return_provider_attempts
            WHERE settlement_return_id IN
              (SELECT id FROM payments.settlement_returns
               WHERE original_payment_id = {scenario.PaymentId})
            """);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            DELETE FROM credits.return_holds
            WHERE settlement_return_id IN
              (SELECT id FROM payments.settlement_returns
               WHERE original_payment_id = {scenario.PaymentId})
            """);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            DELETE FROM payments.settlement_returns
            WHERE original_payment_id = {scenario.PaymentId}
            """);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            DELETE FROM credits.movements WHERE account_id = {scenario.AccountId};
            DELETE FROM credits.accounts WHERE id = {scenario.AccountId};
            DELETE FROM payments.payments WHERE id = {scenario.PaymentId};
            """);
    }

    private static CardTopUpSettlementReturnCommand Command(Scenario scenario) =>
        new(
            Guid.NewGuid(),
            scenario.PaymentId,
            Guid.NewGuid(),
            "PostgreSQL CardTopUp full return");

    private sealed record Scenario(
        Guid PaymentId,
        Guid CustomerUserId,
        Guid AccountId,
        string PayId);

    private sealed record Captured<T>(
        bool Succeeded,
        T? Value,
        Exception? Error);

    private sealed class DirectGateway : GatewayBase
    {
        public override Task<CsobPaymentReverseResult> ReverseAsync(
            string payId,
            CancellationToken cancellationToken = default)
        {
            ReverseCalls++;
            return Task.FromResult(new CsobPaymentReverseResult(
                payId, 0, "OK", 5, null));
        }
    }

    private sealed class AmbiguousGateway : GatewayBase
    {
        public override Task<CsobPaymentReverseResult> ReverseAsync(
            string payId,
            CancellationToken cancellationToken = default)
        {
            ReverseCalls++;
            throw new HttpRequestException("simulated timeout");
        }
    }

    private sealed class StatusOnlyGateway : GatewayBase
    {
        public override Task<CsobPaymentReverseResult> ReverseAsync(
            string payId,
            CancellationToken cancellationToken = default)
        {
            ReverseCalls++;
            throw new InvalidOperationException("Recovery must be status-only.");
        }

        public override Task<CsobPaymentStatusResult> GetStatusAsync(
            string payId,
            CancellationToken cancellationToken = default)
        {
            StatusCalls++;
            return Task.FromResult(new CsobPaymentStatusResult(
                payId, 0, "OK", 5, null, null));
        }
    }

    private sealed class CoordinatedGateway : GatewayBase
    {
        private readonly TaskCompletionSource _statusObserved =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async Task<CsobPaymentReverseResult> ReverseAsync(
            string payId,
            CancellationToken cancellationToken = default)
        {
            ReverseCalls++;
            await _statusObserved.Task.WaitAsync(
                TimeSpan.FromSeconds(10),
                cancellationToken);
            return new CsobPaymentReverseResult(payId, 0, "OK", 5, null);
        }

        public override Task<CsobPaymentStatusResult> GetStatusAsync(
            string payId,
            CancellationToken cancellationToken = default)
        {
            StatusCalls++;
            _statusObserved.TrySetResult();
            return Task.FromResult(new CsobPaymentStatusResult(
                payId, 0, "OK", 5, null, null));
        }
    }

    private abstract class GatewayBase : ICsobGatewayClient
    {
        public int ReverseCalls { get; protected set; }

        public int StatusCalls { get; protected set; }

        public abstract Task<CsobPaymentReverseResult> ReverseAsync(
            string payId,
            CancellationToken cancellationToken = default);

        public virtual Task<CsobPaymentStatusResult> GetStatusAsync(
            string payId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

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

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now;

        public FixedTimeProvider(DateTimeOffset now) => _now = now;

        public override DateTimeOffset GetUtcNow() => _now;
    }
}
