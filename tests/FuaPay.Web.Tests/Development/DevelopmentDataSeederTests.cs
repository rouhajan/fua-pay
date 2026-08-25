using FuaPay.Web.BuildingBlocks.Auditing;
using FuaPay.Web.Development;
using FuaPay.Web.Modules.Access.Application;
using FuaPay.Web.Modules.Access.Development;
using FuaPay.Web.Modules.Access.Domain;
using FuaPay.Web.Modules.Credits.Application;
using FuaPay.Web.Modules.Credits.Domain;
using FuaPay.Web.Modules.Jobs.Application;
using FuaPay.Web.Modules.Jobs.Domain;
using FuaPay.Web.Modules.ServiceUnits.Application;
using FuaPay.Web.Modules.ServiceUnits.Domain;

namespace FuaPay.Web.Tests.Development;

public sealed class DevelopmentDataSeederTests
{
    private static readonly DateTimeOffset CurrentTime =
        new(2026, 7, 28, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task SeedAsync_CreatesRepeatableScenario()
    {
        var fixture = new SeederFixture();

        await fixture.Seeder.SeedAsync(
            resetBeforeSeed: false);

        var firstAccounts =
            fixture.CreditRepository.Accounts.Values.ToArray();

        Assert.Equal(2, firstAccounts.Length);
        Assert.Contains(
            firstAccounts,
            account => account.Movements.Count == 4);
        Assert.Contains(
            firstAccounts,
            account => account.Movements.Count == 1);
        Assert.Equal(6, fixture.JobRepository.Jobs.Count);
        Assert.Equal(4, fixture.ServiceUnitRepository.ServiceUnits.Count);
        Assert.Equal(
            6,
            fixture.ServiceUnitAssignmentRepository.Assignments.Count);
        Assert.Equal(9, fixture.AccessRepository.Users.Count);
        Assert.Equal(4, fixture.JobNumberAllocator.EnsureCalls);

        await fixture.Seeder.SeedAsync(
            resetBeforeSeed: false);

        Assert.Equal(
            firstAccounts,
            fixture.CreditRepository.Accounts.Values.ToArray());

        Assert.Contains(
            firstAccounts,
            account => account.Movements.Count == 4);
        Assert.Equal(6, fixture.JobRepository.Jobs.Count);
        Assert.Equal(0, fixture.CreditRepository.SaveCalls);
        Assert.Equal(0, fixture.JobRepository.SaveCalls);
        Assert.Equal(0, fixture.ServiceUnitRepository.SaveCalls);
        Assert.Equal(
            0,
            fixture.ServiceUnitAssignmentRepository.SaveCalls);
        Assert.Equal(0, fixture.Resetter.ResetCalls);
        Assert.Equal(8, fixture.JobNumberAllocator.EnsureCalls);
    }

    [Fact]
    public async Task SeedAsync_WhenResetRequested_ResetsBeforeCreating()
    {
        var fixture = new SeederFixture();

        await fixture.Seeder.SeedAsync(
            resetBeforeSeed: false);

        fixture.Resetter.ResetAction = () =>
        {
            fixture.AccessRepository.Clear();
            fixture.CreditRepository.Clear();
            fixture.JobRepository.Clear();
            fixture.ServiceUnitRepository.Clear();
            fixture.ServiceUnitAssignmentRepository.Clear();
        };

        await fixture.Seeder.SeedAsync(
            resetBeforeSeed: true);

        Assert.Equal(1, fixture.Resetter.ResetCalls);
        Assert.Equal(2, fixture.CreditRepository.Accounts.Count);
        Assert.Equal(9, fixture.AccessRepository.Users.Count);
        Assert.Equal(6, fixture.JobRepository.Jobs.Count);
        Assert.Equal(4, fixture.ServiceUnitRepository.ServiceUnits.Count);
        Assert.Equal(
            6,
            fixture.ServiceUnitAssignmentRepository.Assignments.Count);
    }

    private sealed class SeederFixture
    {
        public SeederFixture()
        {
            var timeProvider =
                new FixedTimeProvider(CurrentTime);

            var identityService =
                new AccessIdentityService(
                    AccessRepository,
                    NullAuditTrail.Instance,
                    timeProvider);

            var signInService =
                new DevelopmentSignInService(
                    identityService,
                    AccessRepository,
                    timeProvider);

            Seeder = new DevelopmentDataSeeder(
                signInService,
                CreditRepository,
                JobRepository,
                JobNumberAllocator,
                ServiceUnitRepository,
                ServiceUnitAssignmentRepository,
                Resetter);
        }

        public FakeAccessRepository AccessRepository { get; } = new();

        public FakeCreditRepository CreditRepository { get; } = new();

        public FakeJobRepository JobRepository { get; } = new();

        public FakeJobNumberAllocator JobNumberAllocator { get; } = new();

        public FakeServiceUnitRepository ServiceUnitRepository
        { get; } = new();

        public FakeServiceUnitAssignmentRepository
            ServiceUnitAssignmentRepository
        { get; } = new();

        public FakeResetter Resetter { get; } = new();

        public DevelopmentDataSeeder Seeder { get; }
    }

    private sealed class FakeAccessRepository :
        IAccessUserRepository
    {
        private readonly Dictionary<ExternalIdentityKey, AccessUser>
            _users = [];

        public IReadOnlyCollection<AccessUser> Users =>
            _users.Values;

        public Task<AccessUser?> FindByIdAsync(
            Guid userId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(
                _users.Values.SingleOrDefault(
                    user => user.Id == userId));
        }

        public Task<AccessUser?> FindByExternalIdentityAsync(
            ExternalIdentityKey identityKey,
            CancellationToken cancellationToken)
        {
            _users.TryGetValue(identityKey, out var user);

            return Task.FromResult(user);
        }

        public Task AddAsync(
            AccessUser user,
            ExternalIdentityKey identityKey,
            CancellationToken cancellationToken)
        {
            _users.Add(identityKey, user);

            return Task.CompletedTask;
        }

        public Task SaveAsync(
            AccessUser user,
            CancellationToken cancellationToken)
        {
            Assert.Contains(user, _users.Values);

            return Task.CompletedTask;
        }

        public void Clear()
        {
            _users.Clear();
        }
    }

    private sealed class FakeCreditRepository :
        ICreditAccountRepository
    {
        private readonly Dictionary<Guid, CreditAccount> _accounts = [];

        public IReadOnlyDictionary<Guid, CreditAccount> Accounts =>
            _accounts;

        public int SaveCalls { get; private set; }

        public Task<CreditAccount?> FindByOwnerIdAsync(
            Guid ownerId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(
                _accounts.Values.SingleOrDefault(
                    account => account.OwnerId == ownerId));
        }

        public Task AddAsync(
            CreditAccount account,
            CancellationToken cancellationToken)
        {
            _accounts.Add(account.Id, account);

            return Task.CompletedTask;
        }

        public Task SaveAsync(
            CreditAccount account,
            CancellationToken cancellationToken)
        {
            Assert.Same(_accounts[account.Id], account);
            SaveCalls++;

            return Task.CompletedTask;
        }

        public void Clear()
        {
            _accounts.Clear();
            SaveCalls = 0;
        }
    }

    private sealed class FakeJobRepository : IJobRepository
    {
        private readonly Dictionary<Guid, Job> _jobs = [];

        public IReadOnlyDictionary<Guid, Job> Jobs => _jobs;

        public int SaveCalls { get; private set; }

        public Task<Job?> FindByIdAsync(
            Guid jobId,
            CancellationToken cancellationToken)
        {
            _jobs.TryGetValue(jobId, out var job);

            return Task.FromResult(job);
        }

        public Task AddAsync(
            Job job,
            CancellationToken cancellationToken)
        {
            _jobs.Add(job.Id, job);

            return Task.CompletedTask;
        }

        public Task SaveAsync(
            Job job,
            CancellationToken cancellationToken)
        {
            Assert.Same(_jobs[job.Id], job);
            SaveCalls++;

            return Task.CompletedTask;
        }

        public void Clear()
        {
            _jobs.Clear();
            SaveCalls = 0;
        }
    }

    private sealed class FakeJobNumberAllocator : IJobNumberAllocator
    {
        public int EnsureCalls { get; private set; }

        public Task<string> AllocateAsync(
            Guid serviceUnitId,
            string serviceUnitCode,
            int year,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task EnsureAtLeastAsync(
            Guid serviceUnitId,
            int year,
            int value,
            CancellationToken cancellationToken = default)
        {
            EnsureCalls++;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeServiceUnitRepository :
        IServiceUnitRepository
    {
        private readonly Dictionary<Guid, ServiceUnit> _serviceUnits = [];

        public IReadOnlyDictionary<Guid, ServiceUnit> ServiceUnits =>
            _serviceUnits;

        public int SaveCalls { get; private set; }

        public Task<ServiceUnit?> FindByIdAsync(
            Guid serviceUnitId,
            CancellationToken cancellationToken)
        {
            _serviceUnits.TryGetValue(serviceUnitId, out var unit);
            return Task.FromResult(unit);
        }

        public Task<ServiceUnit?> FindByCodeAsync(
            string code,
            CancellationToken cancellationToken)
        {
            var unit = _serviceUnits.Values.SingleOrDefault(
                item =>
                    string.Equals(
                        item.Code,
                        code,
                        StringComparison.Ordinal));

            return Task.FromResult(unit);
        }

        public Task AddAsync(
            ServiceUnit serviceUnit,
            CancellationToken cancellationToken)
        {
            _serviceUnits.Add(serviceUnit.Id, serviceUnit);
            return Task.CompletedTask;
        }

        public Task SaveAsync(
            ServiceUnit serviceUnit,
            CancellationToken cancellationToken)
        {
            Assert.Same(_serviceUnits[serviceUnit.Id], serviceUnit);
            SaveCalls++;
            return Task.CompletedTask;
        }

        public void Clear()
        {
            _serviceUnits.Clear();
            SaveCalls = 0;
        }
    }

    private sealed class FakeServiceUnitAssignmentRepository :
        IRequesterServiceUnitAssignmentRepository
    {
        private readonly Dictionary<Guid, RequesterServiceUnitAssignment>
            _assignments = [];

        public IReadOnlyDictionary<Guid, RequesterServiceUnitAssignment>
            Assignments => _assignments;

        public int SaveCalls { get; private set; }

        public Task<RequesterServiceUnitAssignment?> FindByIdAsync(
            Guid assignmentId,
            CancellationToken cancellationToken)
        {
            _assignments.TryGetValue(assignmentId, out var assignment);
            return Task.FromResult(assignment);
        }

        public Task<RequesterServiceUnitAssignment?> FindActiveAsync(
            Guid serviceUnitId,
            Guid userId,
            CancellationToken cancellationToken)
        {
            var assignment = _assignments.Values.SingleOrDefault(
                item =>
                    item.ServiceUnitId == serviceUnitId &&
                    item.UserId == userId &&
                    item.IsActive);

            return Task.FromResult(assignment);
        }

        public Task AddAsync(
            RequesterServiceUnitAssignment assignment,
            CancellationToken cancellationToken)
        {
            _assignments.Add(assignment.Id, assignment);
            return Task.CompletedTask;
        }

        public Task SaveAsync(
            RequesterServiceUnitAssignment assignment,
            CancellationToken cancellationToken)
        {
            Assert.Same(_assignments[assignment.Id], assignment);
            SaveCalls++;
            return Task.CompletedTask;
        }

        public void Clear()
        {
            _assignments.Clear();
            SaveCalls = 0;
        }
    }

    private sealed class FakeResetter : IDevelopmentDataResetter
    {
        public int ResetCalls { get; private set; }

        public Action? ResetAction { get; set; }

        public Task ResetAsync(
            CancellationToken cancellationToken = default)
        {
            ResetCalls++;
            ResetAction?.Invoke();

            return Task.CompletedTask;
        }
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;

        public FixedTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public override DateTimeOffset GetUtcNow()
        {
            return _utcNow;
        }
    }
}
