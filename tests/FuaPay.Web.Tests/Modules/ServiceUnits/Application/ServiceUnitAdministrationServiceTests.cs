using FuaPay.Web.BuildingBlocks.Auditing;
using FuaPay.Web.Modules.Access.Application;
using FuaPay.Web.Modules.Access.Domain;
using FuaPay.Web.Modules.Jobs.Domain;
using FuaPay.Web.Modules.ServiceUnits.Application;
using FuaPay.Web.Modules.ServiceUnits.Domain;

namespace FuaPay.Web.Tests.Modules.ServiceUnits.Application;

public sealed class ServiceUnitAdministrationServiceTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 29, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task CreateAsync_AddsNewUnit()
    {
        var fixture = new Fixture();

        var unit = await fixture.Service.CreateAsync(
            Guid.NewGuid(),
            "3d",
            "3D tisk",
            ServiceType.ThreeDPrint,
            ServiceUnitChangeActor.ForUser(Guid.NewGuid()));

        Assert.Equal("3D", unit.Code);
        Assert.Same(unit, fixture.ServiceUnits.Items.Single());
    }

    [Fact]
    public async Task AssignRequesterAsync_CreatesAuditedAssignment()
    {
        var fixture = new Fixture();
        var unit = fixture.AddUnit();
        var userId = fixture.AccessUsers.AddUser(
            AccessRole.Requester);

        var assignment = await fixture.Service.AssignRequesterAsync(
            Guid.NewGuid(),
            unit.Id,
            userId,
            ServiceUnitChangeActor.ForProcess("test"));

        Assert.Equal(unit.Id, assignment.ServiceUnitId);
        Assert.Equal(userId, assignment.UserId);
        Assert.Same(
            assignment,
            fixture.Assignments.Items.Single());
    }

    [Fact]
    public async Task AssignRequesterAsync_DuplicateIsRejected()
    {
        var fixture = new Fixture();
        var unit = fixture.AddUnit();
        var userId = fixture.AccessUsers.AddUser(
            AccessRole.Requester);

        await fixture.Service.AssignRequesterAsync(
            Guid.NewGuid(),
            unit.Id,
            userId,
            ServiceUnitChangeActor.ForProcess("first"));

        await Assert.ThrowsAsync<RequesterAlreadyAssignedException>(
            () =>
                fixture.Service.AssignRequesterAsync(
                    Guid.NewGuid(),
                    unit.Id,
                    userId,
                    ServiceUnitChangeActor.ForProcess("second")));
    }

    [Fact]
    public async Task AssignRequesterAsync_MissingUser_IsRejected()
    {
        var fixture = new Fixture();
        var unit = fixture.AddUnit();
        var userId = Guid.NewGuid();

        var exception =
            await Assert.ThrowsAsync<AccessUserNotFoundException>(
                () => fixture.Service.AssignRequesterAsync(
                    Guid.NewGuid(),
                    unit.Id,
                    userId,
                    ServiceUnitChangeActor.ForProcess("test")));

        Assert.Equal(userId, exception.UserId);
        Assert.Empty(fixture.Assignments.Items);
    }

    [Fact]
    public async Task AssignRequesterAsync_UserWithoutRequesterRole_IsRejected()
    {
        var fixture = new Fixture();
        var unit = fixture.AddUnit();
        var userId = fixture.AccessUsers.AddUser(
            AccessRole.Customer);

        var exception =
            await Assert.ThrowsAsync<RequesterRoleRequiredException>(
                () => fixture.Service.AssignRequesterAsync(
                    Guid.NewGuid(),
                    unit.Id,
                    userId,
                    ServiceUnitChangeActor.ForProcess("test")));

        Assert.Equal(userId, exception.UserId);
        Assert.Empty(fixture.Assignments.Items);
    }

    [Fact]
    public async Task AssignRequesterAsync_BlockedRequester_IsRejected()
    {
        var fixture = new Fixture();
        var unit = fixture.AddUnit();
        var userId = fixture.AccessUsers.AddUser(
            AccessUserStatus.Blocked,
            AccessRole.Requester);

        var exception =
            await Assert.ThrowsAsync<AccessUserBlockedException>(
                () => fixture.Service.AssignRequesterAsync(
                    Guid.NewGuid(),
                    unit.Id,
                    userId,
                    ServiceUnitChangeActor.ForProcess("test")));

        Assert.Equal(userId, exception.UserId);
        Assert.Empty(fixture.Assignments.Items);
    }

    [Fact]
    public async Task RevokeRequesterAsync_RevokesExistingAssignment()
    {
        var fixture = new Fixture();
        var unit = fixture.AddUnit();
        var userId = fixture.AccessUsers.AddUser(
            AccessRole.Requester);

        var assignment = await fixture.Service.AssignRequesterAsync(
            Guid.NewGuid(),
            unit.Id,
            userId,
            ServiceUnitChangeActor.ForProcess("grant"));

        await fixture.Service.RevokeRequesterAsync(
            unit.Id,
            userId,
            ServiceUnitChangeActor.ForProcess("revoke"));

        Assert.False(assignment.IsActive);
        Assert.Equal(1, fixture.Assignments.SaveCalls);
    }

    private sealed class Fixture
    {
        public Fixture()
        {
            Service = new ServiceUnitAdministrationService(
                ServiceUnits,
                Assignments,
                AccessUsers,
                new FixedTimeProvider(Now),
                NullAuditTrail.Instance);
        }

        public FakeServiceUnitRepository ServiceUnits { get; } = new();

        public FakeAssignmentRepository Assignments { get; } = new();

        public FakeAccessUserQueries AccessUsers { get; } = new();

        public ServiceUnitAdministrationService Service { get; }

        public ServiceUnit AddUnit()
        {
            var unit = new ServiceUnit(
                Guid.NewGuid(),
                "PLT",
                "Velkoformátový tisk",
                ServiceType.LargeFormatPrint,
                Now.AddDays(-1),
                ServiceUnitChangeActor.ForProcess("seed"));

            ServiceUnits.Items.Add(unit);
            return unit;
        }
    }

    private sealed class FakeServiceUnitRepository :
        IServiceUnitRepository
    {
        public List<ServiceUnit> Items { get; } = [];

        public Task<ServiceUnit?> FindByIdAsync(
            Guid serviceUnitId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(
                Items.SingleOrDefault(item => item.Id == serviceUnitId));
        }

        public Task<ServiceUnit?> FindByCodeAsync(
            string code,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(
                Items.SingleOrDefault(
                    item =>
                        string.Equals(
                            item.Code,
                            code,
                            StringComparison.Ordinal)));
        }

        public Task AddAsync(
            ServiceUnit serviceUnit,
            CancellationToken cancellationToken)
        {
            Items.Add(serviceUnit);
            return Task.CompletedTask;
        }

        public Task SaveAsync(
            ServiceUnit serviceUnit,
            CancellationToken cancellationToken)
        {
            Assert.Contains(serviceUnit, Items);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeAssignmentRepository :
        IRequesterServiceUnitAssignmentRepository
    {
        public List<RequesterServiceUnitAssignment> Items { get; } = [];

        public int SaveCalls { get; private set; }

        public Task<RequesterServiceUnitAssignment?> FindByIdAsync(
            Guid assignmentId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(
                Items.SingleOrDefault(item => item.Id == assignmentId));
        }

        public Task<RequesterServiceUnitAssignment?> FindActiveAsync(
            Guid serviceUnitId,
            Guid userId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(
                Items.SingleOrDefault(
                    item =>
                        item.ServiceUnitId == serviceUnitId &&
                        item.UserId == userId &&
                        item.IsActive));
        }

        public Task AddAsync(
            RequesterServiceUnitAssignment assignment,
            CancellationToken cancellationToken)
        {
            Items.Add(assignment);
            return Task.CompletedTask;
        }

        public Task SaveAsync(
            RequesterServiceUnitAssignment assignment,
            CancellationToken cancellationToken)
        {
            Assert.Contains(assignment, Items);
            SaveCalls++;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeAccessUserQueries : IAccessUserQueries
    {
        private readonly Dictionary<Guid, AccessUserDetail> _users = [];

        public Guid AddUser(
            AccessUserStatus status,
            params AccessRole[] roles)
        {
            return AddUserCore(status, roles);
        }

        public Guid AddUser(params AccessRole[] roles)
        {
            return AddUserCore(AccessUserStatus.Active, roles);
        }

        private Guid AddUserCore(
            AccessUserStatus status,
            IReadOnlyList<AccessRole> roles)
        {
            var userId = Guid.NewGuid();
            _users.Add(
                userId,
                new AccessUserDetail(
                    userId,
                    "Testovací uživatel",
                    "user@example.test",
                    status,
                    Now.AddDays(-1),
                    Now,
                    1,
                    roles,
                    [],
                    []));

            return userId;
        }

        public Task<AccessUserDetail?> FindDetailAsync(
            Guid userId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _users.TryGetValue(userId, out var user);
            return Task.FromResult(user);
        }

        public Task<AccessUserPage> ListAsync(
            AccessUserListRequest request,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<IReadOnlyList<AccessUserOption>>
            ListActiveCustomersAsync(
                CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<IReadOnlyDictionary<Guid, AccessUserOption>>
            FindOptionsAsync(
                IEnumerable<Guid> userIds,
                CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<bool> IsActiveAsync(
            Guid userId,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<bool> IsActiveCustomerAsync(
            Guid userId,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<long> CountActiveUsersWithRoleAsync(
            AccessRole role,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
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
}
