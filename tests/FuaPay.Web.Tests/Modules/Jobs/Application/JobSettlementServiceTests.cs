using FuaPay.Web.BuildingBlocks.Auditing;
using FuaPay.Web.BuildingBlocks.Domain;
using FuaPay.Web.Modules.Jobs.Application;
using FuaPay.Web.Modules.Jobs.Domain;

namespace FuaPay.Web.Tests.Modules.Jobs.Application;

public sealed class JobSettlementServiceTests
{
    private static readonly DateTimeOffset CurrentTime =
        new(2026, 7, 26, 16, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ConfirmAsync_PublishedJob_Saves()
    {
        var job = CreatePublishedJob();
        var referenceId = Guid.NewGuid();

        var repository = new FakeJobRepository
        {
            Job = job
        };

        var service = CreateService(repository);

        var wasApplied = await service.ConfirmAsync(
            job.Id,
            JobSettlementType.Credit,
            referenceId);

        Assert.True(wasApplied);
        Assert.Equal(JobPaymentStatus.Paid, job.PaymentStatus);
        Assert.Equal(JobSettlementType.Credit, job.SettlementType);
        Assert.Equal(referenceId, job.SettlementReferenceId);
        Assert.Equal(CurrentTime, job.SettledAt);
        Assert.Equal(1, repository.SaveCalls);
    }

    [Fact]
    public async Task ConfirmAsync_SameSourceRepeated_DoesNotSaveAgain()
    {
        var job = CreatePublishedJob();
        var referenceId = Guid.NewGuid();

        var repository = new FakeJobRepository
        {
            Job = job
        };

        var service = CreateService(repository);

        var firstApplied = await service.ConfirmAsync(
            job.Id,
            JobSettlementType.DirectPayment,
            referenceId);

        var secondApplied = await service.ConfirmAsync(
            job.Id,
            JobSettlementType.DirectPayment,
            referenceId);

        Assert.True(firstApplied);
        Assert.False(secondApplied);
        Assert.Equal(1, repository.SaveCalls);
        Assert.Equal(CurrentTime, job.SettledAt);
    }

    [Fact]
    public async Task ConfirmAsync_DifferentSecondSource_Throws()
    {
        var job = CreatePublishedJob();
        var firstReferenceId = Guid.NewGuid();

        var repository = new FakeJobRepository
        {
            Job = job
        };

        var service = CreateService(repository);

        await service.ConfirmAsync(
            job.Id,
            JobSettlementType.Credit,
            firstReferenceId);

        await Assert.ThrowsAsync<
            JobSettlementConflictException>(
                () => service.ConfirmAsync(
                    job.Id,
                    JobSettlementType.DirectPayment,
                    Guid.NewGuid()));

        Assert.Equal(firstReferenceId, job.SettlementReferenceId);
        Assert.Equal(1, repository.SaveCalls);
    }

    [Fact]
    public async Task ConfirmAsync_WhenJobDoesNotExist_Throws()
    {
        var jobId = Guid.NewGuid();
        var repository = new FakeJobRepository();
        var service = CreateService(repository);

        var exception =
            await Assert.ThrowsAsync<JobNotFoundException>(
                () => service.ConfirmAsync(
                    jobId,
                    JobSettlementType.Credit,
                    Guid.NewGuid()));

        Assert.Equal(jobId, exception.JobId);
        Assert.Equal(1, repository.FindCalls);
        Assert.Equal(0, repository.SaveCalls);
    }

    [Fact]
    public async Task ConfirmAsync_DraftJob_DoesNotSave()
    {
        var job = new Job(
            Guid.NewGuid(),
            NextJobNumber(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            ServiceType.ThreeDPrint,
            "Model",
            "Tisk modelu",
            new Money(12_500),
            CurrentTime.AddHours(-2));

        var repository = new FakeJobRepository
        {
            Job = job
        };

        var service = CreateService(repository);

        await Assert.ThrowsAsync<
            JobSettlementNotAllowedException>(
                () => service.ConfirmAsync(
                    job.Id,
                    JobSettlementType.Credit,
                    Guid.NewGuid()));

        Assert.Equal(JobPaymentStatus.Unpaid, job.PaymentStatus);
        Assert.Equal(0, repository.SaveCalls);
    }

    private static JobSettlementService CreateService(
        IJobRepository repository)
    {
        return new JobSettlementService(
            repository,
            new FixedTimeProvider(CurrentTime),
            NullAuditTrail.Instance);
    }

    private static Job CreatePublishedJob()
    {
        var job = new Job(
            Guid.NewGuid(),
            NextJobNumber(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            ServiceType.ThreeDPrint,
            "Model",
            "Tisk modelu",
            new Money(12_500),
            CurrentTime.AddHours(-2));

        job.Publish(
            CurrentTime.AddHours(-1));

        return job;
    }

    private sealed class FixedTimeProvider :
        TimeProvider
    {
        private readonly DateTimeOffset _currentTime;

        public FixedTimeProvider(
            DateTimeOffset currentTime)
        {
            _currentTime = currentTime;
        }

        public override DateTimeOffset GetUtcNow()
        {
            return _currentTime;
        }
    }

    private sealed class FakeJobRepository :
        IJobRepository
    {
        public Job? Job { get; set; }

        public int FindCalls { get; private set; }

        public int SaveCalls { get; private set; }

        public Task<Job?> FindByIdAsync(
            Guid jobId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            FindCalls++;

            var result =
                Job?.Id == jobId
                    ? Job
                    : null;

            return Task.FromResult(result);
        }

        public Task AddAsync(
            Job job,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            Job = job;

            return Task.CompletedTask;
        }

        public Task SaveAsync(
            Job job,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            Assert.Same(Job, job);
            SaveCalls++;

            return Task.CompletedTask;
        }
    }
}
