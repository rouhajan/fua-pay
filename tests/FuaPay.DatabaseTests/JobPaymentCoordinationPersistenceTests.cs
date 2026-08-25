using FuaPay.Web.BuildingBlocks.Application;
using FuaPay.Web.BuildingBlocks.Domain;
using FuaPay.Web.BuildingBlocks.Persistence;
using FuaPay.Web.Modules.Credits.Application;
using FuaPay.Web.Modules.Credits.Domain;
using FuaPay.Web.Modules.Jobs.Application;
using FuaPay.Web.Modules.Jobs.Domain;
using FuaPay.Web.Modules.Payments.Application;
using FuaPay.Web.Modules.Payments.Domain;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FuaPay.DatabaseTests;

public sealed class JobPaymentCoordinationPersistenceTests :
    IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly DateTimeOffset TestTime =
        new(2026, 8, 25, 10, 0, 0, TimeSpan.Zero);

    private readonly WebApplicationFactory<Program> _factory;

    public JobPaymentCoordinationPersistenceTests(
        WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ExistingDirectPayment_BlocksCreditPayment()
    {
        var scenario = await SeedScenarioAsync(_factory);

        try
        {
            _ = await CreateDirectPaymentAsync(
                _factory,
                scenario.CustomerUserId,
                scenario.JobId);

            using var scope = _factory.Services.CreateScope();
            var service = scope.ServiceProvider
                .GetRequiredService<CreditJobPaymentService>();

            var exception = await Assert.ThrowsAsync<
                JobPaymentInProgressException>(
                    () => service.PayAsync(
                        scenario.CustomerUserId,
                        scenario.JobId));

            Assert.Equal(scenario.JobId, exception.JobId);
            await AssertUnsettledScenarioAsync(
                _factory,
                scenario,
                expectedProductionStatus:
                    JobProductionStatus.Published);
        }
        finally
        {
            await DeleteScenarioAsync(_factory, scenario);
        }
    }

    [Fact]
    public async Task ExistingDirectPayment_BlocksCancellation()
    {
        var scenario = await SeedScenarioAsync(_factory);

        try
        {
            _ = await CreateDirectPaymentAsync(
                _factory,
                scenario.CustomerUserId,
                scenario.JobId);

            using var scope = _factory.Services.CreateScope();
            var service = scope.ServiceProvider
                .GetRequiredService<JobManagementService>();
            var actor = new JobManagementActor(
                scenario.ManagerUserId,
                JobManagementScope.All);

            var exception = await Assert.ThrowsAsync<
                JobPaymentInProgressException>(
                    () => service.CancelAsync(
                        actor,
                        scenario.JobId));

            Assert.Equal(scenario.JobId, exception.JobId);
            await AssertUnsettledScenarioAsync(
                _factory,
                scenario,
                expectedProductionStatus:
                    JobProductionStatus.Published);
        }
        finally
        {
            await DeleteScenarioAsync(_factory, scenario);
        }
    }

    [Fact]
    public async Task ConcurrentDirectAndCreditPayment_LeavesOneFinancialPath()
    {
        var scenario = await SeedScenarioAsync(_factory);
        var gate = new JobLockGate();
        using var factory = CreateCoordinatedFactory(gate);

        try
        {
            using var directScope = factory.Services.CreateScope();
            var directService = directScope.ServiceProvider
                .GetRequiredService<PaymentCreationService>();
            var directTask = directService.CreateJobPaymentAsync(
                scenario.CustomerUserId,
                scenario.JobId);

            await gate.FirstLockAcquired.WaitAsync(
                TimeSpan.FromSeconds(30));

            using var creditScope = factory.Services.CreateScope();
            var creditService = creditScope.ServiceProvider
                .GetRequiredService<CreditJobPaymentService>();
            var creditTask = Assert.ThrowsAsync<
                JobPaymentInProgressException>(
                    () => creditService.PayAsync(
                        scenario.CustomerUserId,
                        scenario.JobId));

            await gate.SecondLockAttempted.WaitAsync(
                TimeSpan.FromSeconds(30));
            gate.ReleaseFirstLock();

            var payment = await directTask;
            var exception = await creditTask;

            Assert.Equal(scenario.JobId, payment.JobId);
            Assert.Equal(scenario.JobId, exception.JobId);
            await AssertUnsettledScenarioAsync(
                factory,
                scenario,
                expectedProductionStatus:
                    JobProductionStatus.Published);
        }
        finally
        {
            gate.ReleaseFirstLock();
            await DeleteScenarioAsync(factory, scenario);
        }
    }

    [Fact]
    public async Task ConcurrentDirectPaymentAndCancellation_LeavesPayableJob()
    {
        var scenario = await SeedScenarioAsync(_factory);
        var gate = new JobLockGate();
        using var factory = CreateCoordinatedFactory(gate);

        try
        {
            using var directScope = factory.Services.CreateScope();
            var directService = directScope.ServiceProvider
                .GetRequiredService<PaymentCreationService>();
            var directTask = directService.CreateJobPaymentAsync(
                scenario.CustomerUserId,
                scenario.JobId);

            await gate.FirstLockAcquired.WaitAsync(
                TimeSpan.FromSeconds(30));

            using var managementScope = factory.Services.CreateScope();
            var managementService = managementScope.ServiceProvider
                .GetRequiredService<JobManagementService>();
            var actor = new JobManagementActor(
                scenario.ManagerUserId,
                JobManagementScope.All);
            var cancellationTask = Assert.ThrowsAsync<
                JobPaymentInProgressException>(
                    () => managementService.CancelAsync(
                        actor,
                        scenario.JobId));

            await gate.SecondLockAttempted.WaitAsync(
                TimeSpan.FromSeconds(30));
            gate.ReleaseFirstLock();

            var payment = await directTask;
            var exception = await cancellationTask;

            Assert.Equal(scenario.JobId, payment.JobId);
            Assert.Equal(scenario.JobId, exception.JobId);
            await AssertUnsettledScenarioAsync(
                factory,
                scenario,
                expectedProductionStatus:
                    JobProductionStatus.Published);
        }
        finally
        {
            gate.ReleaseFirstLock();
            await DeleteScenarioAsync(factory, scenario);
        }
    }

    private WebApplicationFactory<Program> CreateCoordinatedFactory(
        JobLockGate gate)
    {
        return _factory.WithWebHostBuilder(
            builder => builder.ConfigureTestServices(
                services =>
                {
                    var descriptor = Assert.Single(
                        services,
                        item => item.ServiceType ==
                            typeof(IJobPaymentCoordination));

                    services.Remove(descriptor);
                    services.AddScoped<IJobPaymentCoordination>(
                        provider =>
                            new CoordinatingJobPaymentCoordination(
                                CreateOriginalCoordination(
                                    provider,
                                    descriptor),
                                gate));
                }));
    }

    private static IJobPaymentCoordination CreateOriginalCoordination(
        IServiceProvider provider,
        ServiceDescriptor descriptor)
    {
        if (descriptor.ImplementationInstance is
            IJobPaymentCoordination instance)
        {
            return instance;
        }

        if (descriptor.ImplementationFactory is not null)
        {
            return (IJobPaymentCoordination)
                descriptor.ImplementationFactory(provider);
        }

        return (IJobPaymentCoordination)
            ActivatorUtilities.CreateInstance(
                provider,
                descriptor.ImplementationType
                    ?? throw new InvalidOperationException(
                        "Původní koordinace plateb nemá implementaci."));
    }

    private static async Task<Payment> CreateDirectPaymentAsync(
        WebApplicationFactory<Program> factory,
        Guid customerUserId,
        Guid jobId)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider
            .GetRequiredService<PaymentCreationService>()
            .CreateJobPaymentAsync(customerUserId, jobId);
    }

    private static async Task<TestScenario> SeedScenarioAsync(
        WebApplicationFactory<Program> factory)
    {
        var scenario = new TestScenario(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid());

        using var scope = factory.Services.CreateScope();
        var creditService = new CreditService(
            scope.ServiceProvider
                .GetRequiredService<ICreditAccountRepository>(),
            scope.ServiceProvider
                .GetRequiredService<IApplicationTransaction>(),
            new FixedTimeProvider(TestTime));

        await creditService.CreditAsync(
            scenario.CustomerUserId,
            Guid.NewGuid(),
            new Money(20_000),
            "Počáteční kredit pro koordinační test");

        var job = new Job(
            scenario.JobId,
            NextJobNumber(),
            Guid.NewGuid(),
            scenario.CustomerUserId,
            scenario.ManagerUserId,
            ServiceType.ThreeDPrint,
            "Koordinační model",
            "Zakázka pro test vzájemného vyloučení úhrad",
            new Money(12_500),
            TestTime);
        job.Publish(TestTime.AddMinutes(1));

        await scope.ServiceProvider
            .GetRequiredService<IJobRepository>()
            .AddAsync(job, CancellationToken.None);

        return scenario;
    }

    private static async Task AssertUnsettledScenarioAsync(
        WebApplicationFactory<Program> factory,
        TestScenario scenario,
        JobProductionStatus expectedProductionStatus)
    {
        using var scope = factory.Services.CreateScope();
        var job = Assert.IsType<Job>(
            await scope.ServiceProvider
                .GetRequiredService<IJobRepository>()
                .FindByIdAsync(
                    scenario.JobId,
                    CancellationToken.None));
        var account = Assert.IsType<CreditAccount>(
            await scope.ServiceProvider
                .GetRequiredService<ICreditAccountRepository>()
                .FindByOwnerIdAsync(
                    scenario.CustomerUserId,
                    CancellationToken.None));
        var payment = await scope.ServiceProvider
            .GetRequiredService<IPaymentRepository>()
            .FindBlockingForJobAsync(
                scenario.JobId,
                CancellationToken.None);

        Assert.NotNull(payment);
        Assert.Equal(expectedProductionStatus, job.ProductionStatus);
        Assert.Equal(JobPaymentStatus.Unpaid, job.PaymentStatus);
        Assert.Null(job.CancelledAt);
        Assert.Equal(new Money(20_000), account.Balance);
        Assert.Single(account.Movements);
    }

    private static async Task DeleteScenarioAsync(
        WebApplicationFactory<Program> factory,
        TestScenario scenario)
    {
        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider
            .GetRequiredService<FuaPayDbContext>()
            .Database;

        await using var transaction =
            await database.BeginTransactionAsync();

        await database.ExecuteSqlInterpolatedAsync(
            $"""
            DELETE FROM audit.events
            WHERE entity_type = 'payment'
              AND entity_id IN
              (
                  SELECT id::text
                  FROM payments.payments
                  WHERE job_id = {scenario.JobId}
              )
            """);
        await database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM payments.csob_payment_reconciliation WHERE payment_id IN (SELECT id FROM payments.payments WHERE job_id = {scenario.JobId})");
        await database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM payments.payment_initiations WHERE payment_id IN (SELECT id FROM payments.payments WHERE job_id = {scenario.JobId})");
        await database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM payments.payments WHERE job_id = {scenario.JobId}");
        await database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM notifications.outbox WHERE recipient_user_id = {scenario.CustomerUserId}");
        await database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM audit.events WHERE actor_user_id IN ({scenario.CustomerUserId}, {scenario.ManagerUserId})");
        await database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM credits.movements WHERE account_id IN (SELECT id FROM credits.accounts WHERE owner_id = {scenario.CustomerUserId})");
        await database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM credits.accounts WHERE owner_id = {scenario.CustomerUserId}");
        await database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM jobs.jobs WHERE id = {scenario.JobId}");

        await transaction.CommitAsync();
    }

    private static string NextJobNumber()
    {
        var suffix = Random.Shared.Next(0, 1_000_000);
        return $"3D-2026-{suffix:000000}";
    }

    private sealed record TestScenario(
        Guid CustomerUserId,
        Guid ManagerUserId,
        Guid JobId);

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;

        public FixedTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public override DateTimeOffset GetUtcNow() => _utcNow;
    }

    private sealed class JobLockGate
    {
        private readonly TaskCompletionSource _firstLockAcquired =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _secondLockAttempted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _releaseFirstLock =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _attemptCount;

        public Task FirstLockAcquired => _firstLockAcquired.Task;

        public Task SecondLockAttempted => _secondLockAttempted.Task;

        public int NextAttempt() =>
            Interlocked.Increment(ref _attemptCount);

        public void FirstAcquired() =>
            _firstLockAcquired.TrySetResult();

        public void SecondAttempted() =>
            _secondLockAttempted.TrySetResult();

        public Task WaitForFirstReleaseAsync() =>
            _releaseFirstLock.Task;

        public void ReleaseFirstLock() =>
            _releaseFirstLock.TrySetResult();
    }

    private sealed class CoordinatingJobPaymentCoordination :
        IJobPaymentCoordination
    {
        private readonly IJobPaymentCoordination _inner;
        private readonly JobLockGate _gate;

        public CoordinatingJobPaymentCoordination(
            IJobPaymentCoordination inner,
            JobLockGate gate)
        {
            _inner = inner;
            _gate = gate;
        }

        public async Task<bool> LockJobAsync(
            Guid jobId,
            CancellationToken cancellationToken)
        {
            var attempt = _gate.NextAttempt();

            if (attempt == 2)
            {
                _gate.SecondAttempted();
            }

            var wasLocked = await _inner.LockJobAsync(
                jobId,
                cancellationToken);

            if (attempt == 1)
            {
                _gate.FirstAcquired();
                await _gate.WaitForFirstReleaseAsync();
            }

            return wasLocked;
        }

        public Task<bool> HasBlockingDirectPaymentAsync(
            Guid jobId,
            CancellationToken cancellationToken) =>
            _inner.HasBlockingDirectPaymentAsync(
                jobId,
                cancellationToken);
    }
}
