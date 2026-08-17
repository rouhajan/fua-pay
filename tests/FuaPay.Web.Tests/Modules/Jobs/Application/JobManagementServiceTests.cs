using FuaPay.Web.BuildingBlocks.Auditing;
using FuaPay.Web.BuildingBlocks.Domain;
using FuaPay.Web.BuildingBlocks.Notifications;
using FuaPay.Web.Modules.Access.Application;
using FuaPay.Web.Modules.Access.Domain;
using FuaPay.Web.Modules.Jobs.Application;
using FuaPay.Web.Modules.Jobs.Domain;
using FuaPay.Web.Modules.ServiceUnits.Application;

namespace FuaPay.Web.Tests.Modules.Jobs.Application;

public sealed class JobManagementServiceTests
{
    private static readonly DateTimeOffset CurrentTime =
        new(2026, 7, 26, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Actor_RejectsEmptyUserId()
    {
        Action action = () =>
        {
            _ = new JobManagementActor(
                Guid.Empty,
                JobManagementScope.AssignedServiceUnits);
        };

        Assert.Throws<ArgumentException>(action);
    }

    [Fact]
    public void Actor_RejectsUnknownScope()
    {
        Action action = () =>
        {
            _ = new JobManagementActor(
                Guid.NewGuid(),
                JobManagementScope.Unknown);
        };

        Assert.Throws<ArgumentOutOfRangeException>(action);
    }

    [Fact]
    public void Actor_RejectsEmptyServiceUnitId()
    {
        Action action = () =>
        {
            _ = new JobManagementActor(
                Guid.NewGuid(),
                new[] { Guid.Empty });
        };

        Assert.Throws<ArgumentException>(action);
    }

    [Fact]
    public async Task CreateDraftAsync_AssignedUnit_CreatesNumberedJob()
    {
        var serviceUnitId = Guid.NewGuid();
        var actor = CreateRequesterActor(serviceUnitId);
        var customerId = Guid.NewGuid();
        var repository = new FakeJobRepository();
        var service = CreateService(repository);

        var job = await service.CreateDraftAsync(
            actor,
            serviceUnitId,
            customerId,
            ServiceType.ThreeDPrint,
            "  Model  ",
            "  Tisk modelu  ",
            new Money(12_500));

        Assert.NotEqual(Guid.Empty, job.Id);
        Assert.Equal("3D-2026-000001", job.Number);
        Assert.Equal(serviceUnitId, job.ServiceUnitId);
        Assert.Equal(customerId, job.CustomerUserId);
        Assert.Equal(actor.UserId, job.CreatedByUserId);
        Assert.Equal(CurrentTime, job.CreatedAt);
        Assert.Equal("Model", job.Title);
        Assert.Equal("Tisk modelu", job.Description);
        Assert.Same(job, repository.Job);
        Assert.Equal(1, repository.AddCalls);
    }

    [Fact]
    public async Task CreateDraftAsync_RejectsPriceOutsideCentralPolicy()
    {
        var serviceUnitId = Guid.NewGuid();
        var service = CreateService(new FakeJobRepository());

        await Assert.ThrowsAsync<JobPriceNotAllowedException>(
            () => service.CreateDraftAsync(
                CreateRequesterActor(serviceUnitId),
                serviceUnitId,
                Guid.NewGuid(),
                ServiceType.ThreeDPrint,
                "Model",
                "Description",
                new Money(
                    FinancialAmountPolicy.JobPrice.MaximumMinorUnits + 1)));
    }

    [Fact]
    public async Task CreateDraftAsync_UnassignedUnit_IsRejected()
    {
        var actor = CreateRequesterActor(Guid.NewGuid());
        var repository = new FakeJobRepository();
        var service = CreateService(repository);

        await Assert.ThrowsAsync<
            JobServiceUnitAccessDeniedException>(
                () => service.CreateDraftAsync(
                    actor,
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    ServiceType.ThreeDPrint,
                    "Model",
                    "Tisk modelu",
                    new Money(12_500)));

        Assert.Equal(0, repository.AddCalls);
    }

    [Fact]
    public async Task CreateDraftAsync_InactiveUnit_IsRejected()
    {
        var serviceUnitId = Guid.NewGuid();
        var actor = CreateRequesterActor(serviceUnitId);
        var repository = new FakeJobRepository();
        var service = new JobManagementService(
            repository,
            new FakeJobNumberAllocator(),
            new MissingServiceUnitQueries(),
            new FakeAccessUserQueries(),
            new FixedTimeProvider(CurrentTime),
            NullAuditTrail.Instance,
            NullNotificationOutbox.Instance);

        await Assert.ThrowsAsync<JobServiceUnitUnavailableException>(
            () => service.CreateDraftAsync(
                actor,
                serviceUnitId,
                Guid.NewGuid(),
                ServiceType.ThreeDPrint,
                "Model",
                "Tisk modelu",
                new Money(12_500)));

        Assert.Equal(0, repository.AddCalls);
    }

    [Fact]
    public async Task UpdateDraftAsync_AssignedUnit_Saves()
    {
        var serviceUnitId = Guid.NewGuid();
        var actor = CreateRequesterActor(serviceUnitId);
        var job = CreateDraft(actor.UserId, serviceUnitId);
        var repository = new FakeJobRepository { Job = job };
        var service = CreateService(repository);
        var newCustomerId = Guid.NewGuid();

        var result = await service.UpdateDraftAsync(
            actor,
            job.Id,
            newCustomerId,
            ServiceType.ThreeDPrint,
            "Upravený model",
            "Upravený tisk modelu",
            new Money(25_000));

        Assert.Same(job, result);
        Assert.Equal(newCustomerId, job.CustomerUserId);
        Assert.Equal(ServiceType.ThreeDPrint, job.ServiceType);
        Assert.Equal(1, repository.SaveCalls);
    }

    [Fact]
    public async Task UpdateDraftAsync_OtherUnit_IsRejected()
    {
        var actor = CreateRequesterActor(Guid.NewGuid());
        var job = CreateDraft(Guid.NewGuid(), Guid.NewGuid());
        var repository = new FakeJobRepository { Job = job };
        var service = CreateService(repository);

        var exception =
            await Assert.ThrowsAsync<JobAccessDeniedException>(
                () => service.UpdateDraftAsync(
                    actor,
                    job.Id,
                    Guid.NewGuid(),
                    ServiceType.Workshop,
                    "Laser",
                    "Řezání překližky",
                    new Money(25_000)));

        Assert.Equal(job.Id, exception.JobId);
        Assert.Equal(0, repository.SaveCalls);
    }

    [Fact]
    public async Task UpdateDraftAsync_AllScopeCanManageAnyUnit()
    {
        var actor = new JobManagementActor(
            Guid.NewGuid(),
            JobManagementScope.All);
        var job = CreateDraft(Guid.NewGuid(), Guid.NewGuid());
        var repository = new FakeJobRepository { Job = job };
        var service = CreateService(repository);

        await service.UpdateDraftAsync(
            actor,
            job.Id,
            Guid.NewGuid(),
            ServiceType.ThreeDPrint,
            "Upravený model",
            "Upravený tisk modelu",
            new Money(8_000));

        Assert.Equal(1, repository.SaveCalls);
    }

    [Fact]
    public async Task UpdateDraftAsync_ServiceTypeDifferentFromUnit_IsRejected()
    {
        var serviceUnitId = Guid.NewGuid();
        var actor = CreateRequesterActor(serviceUnitId);
        var job = CreateDraft(actor.UserId, serviceUnitId);
        var repository = new FakeJobRepository { Job = job };
        var service = CreateService(repository);

        await Assert.ThrowsAsync<ServiceTypeMismatchException>(
            () => service.UpdateDraftAsync(
                actor,
                job.Id,
                Guid.NewGuid(),
                ServiceType.Workshop,
                "Laser",
                "Řezání překližky",
                new Money(25_000)));

        Assert.Equal(0, repository.SaveCalls);
    }

    [Fact]
    public async Task Lifecycle_AssignedUnit_UsesCurrentTime()
    {
        var serviceUnitId = Guid.NewGuid();
        var actor = CreateRequesterActor(serviceUnitId);
        var job = CreatePaidJob(actor.UserId, serviceUnitId);
        var repository = new FakeJobRepository { Job = job };
        var service = CreateService(repository);

        await service.StartProductionAsync(actor, job.Id);
        await service.MarkReadyForPickupAsync(actor, job.Id);
        await service.CompleteAsync(actor, job.Id);

        Assert.Equal(JobProductionStatus.Completed, job.ProductionStatus);
        Assert.Equal(CurrentTime, job.ProductionStartedAt);
        Assert.Equal(CurrentTime, job.ReadyForPickupAt);
        Assert.Equal(CurrentTime, job.CompletedAt);
        Assert.Equal(3, repository.SaveCalls);
    }

    [Fact]
    public async Task PublishAsync_MissingJob_Throws()
    {
        var actor = CreateRequesterActor(Guid.NewGuid());
        var jobId = Guid.NewGuid();
        var repository = new FakeJobRepository();
        var service = CreateService(repository);

        var exception =
            await Assert.ThrowsAsync<JobNotFoundException>(
                () => service.PublishAsync(actor, jobId));

        Assert.Equal(jobId, exception.JobId);
        Assert.Equal(0, repository.SaveCalls);
    }

    private static JobManagementService CreateService(
        IJobRepository repository)
    {
        return new JobManagementService(
            repository,
            new FakeJobNumberAllocator(),
            new FakeServiceUnitQueries(),
            new FakeAccessUserQueries(),
            new FixedTimeProvider(CurrentTime),
            NullAuditTrail.Instance,
            NullNotificationOutbox.Instance);
    }

    private static JobManagementActor CreateRequesterActor(
        Guid serviceUnitId)
    {
        return new JobManagementActor(
            Guid.NewGuid(),
            new[] { serviceUnitId });
    }

    private static Job CreateDraft(
        Guid createdByUserId,
        Guid serviceUnitId)
    {
        return new Job(
            Guid.NewGuid(),
            NextJobNumber(),
            serviceUnitId,
            Guid.NewGuid(),
            createdByUserId,
            ServiceType.ThreeDPrint,
            "Model",
            "Tisk modelu",
            new Money(12_500),
            CurrentTime.AddHours(-4));
    }

    private static Job CreatePaidJob(
        Guid createdByUserId,
        Guid serviceUnitId)
    {
        var job = CreateDraft(createdByUserId, serviceUnitId);
        job.Publish(CurrentTime.AddHours(-3));
        job.ConfirmSettlement(
            JobSettlementType.Credit,
            Guid.NewGuid(),
            CurrentTime.AddHours(-2));
        return job;
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _currentTime;

        public FixedTimeProvider(DateTimeOffset currentTime)
        {
            _currentTime = currentTime;
        }

        public override DateTimeOffset GetUtcNow() => _currentTime;
    }

    private sealed class FakeJobNumberAllocator : IJobNumberAllocator
    {
        public Task<string> AllocateAsync(
            Guid serviceUnitId,
            string serviceUnitCode,
            int year,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(
                $"{serviceUnitCode}-{year:D4}-000001");
        }

        public Task EnsureAtLeastAsync(
            Guid serviceUnitId,
            int year,
            int value,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class FakeServiceUnitQueries : IServiceUnitQueries
    {
        public Task<IReadOnlyList<ServiceUnitAdministrationListItem>> ListAllAsync(
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<IReadOnlyList<RequesterServiceUnitAssignmentReadModel>>
            ListAssignmentsForUserAsync(
                Guid userId,
                bool includeRevoked = false,
                CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<ServiceUnitReadModel?> FindActiveAsync(
            Guid serviceUnitId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<ServiceUnitReadModel?>(
                new ServiceUnitReadModel(
                    serviceUnitId,
                    "3D",
                    "3D tisk",
                    ServiceType.ThreeDPrint));
        }

        public Task<IReadOnlyList<ServiceUnitReadModel>> ListActiveAsync(
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<IReadOnlyList<ServiceUnitReadModel>> ListForRequesterAsync(
            Guid userId,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class MissingServiceUnitQueries : IServiceUnitQueries
    {
        public Task<IReadOnlyList<ServiceUnitAdministrationListItem>> ListAllAsync(
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<IReadOnlyList<RequesterServiceUnitAssignmentReadModel>>
            ListAssignmentsForUserAsync(
                Guid userId,
                bool includeRevoked = false,
                CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<ServiceUnitReadModel?> FindActiveAsync(
            Guid serviceUnitId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<ServiceUnitReadModel?>(null);
        }

        public Task<IReadOnlyList<ServiceUnitReadModel>> ListActiveAsync(
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<IReadOnlyList<ServiceUnitReadModel>> ListForRequesterAsync(
            Guid userId,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }


    private sealed class FakeAccessUserQueries : IAccessUserQueries
    {
        public bool IsActiveCustomer { get; set; } = true;

        public Task<bool> IsActiveAsync(
            Guid userId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(userId != Guid.Empty);
        }

        public Task<bool> IsActiveCustomerAsync(
            Guid userId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(IsActiveCustomer);
        }

        public Task<AccessUserPage> ListAsync(
            AccessUserListRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AccessUserDetail?> FindDetailAsync(
            Guid userId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<AccessUserOption>> ListActiveCustomersAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyDictionary<Guid, AccessUserOption>> FindOptionsAsync(
            IEnumerable<Guid> userIds,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<long> CountActiveUsersWithRoleAsync(
            AccessRole role,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeJobRepository : IJobRepository
    {
        public Job? Job { get; set; }
        public int AddCalls { get; private set; }
        public int SaveCalls { get; private set; }

        public Task<Job?> FindByIdAsync(
            Guid jobId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(
                Job?.Id == jobId ? Job : null);
        }

        public Task AddAsync(
            Job job,
            CancellationToken cancellationToken)
        {
            Job = job;
            AddCalls++;
            return Task.CompletedTask;
        }

        public Task SaveAsync(
            Job job,
            CancellationToken cancellationToken)
        {
            Assert.Same(Job, job);
            SaveCalls++;
            return Task.CompletedTask;
        }
    }
}
