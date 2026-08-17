using FuaPay.Web.BuildingBlocks.Application;
using FuaPay.Web.BuildingBlocks.Auditing;
using FuaPay.Web.Modules.Access.Application;
using FuaPay.Web.Modules.Access.Domain;

namespace FuaPay.Web.Tests.Modules.Access.Application;

public sealed class AccessUserAdministrationServiceTests
{
    private static readonly DateTimeOffset CreatedAt =
        new(2026, 7, 29, 13, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset CurrentTime =
        CreatedAt.AddHours(1);

    [Fact]
    public async Task GrantRoleAsync_StoresAuditedRoleAssignment()
    {
        var actorUserId = Guid.NewGuid();
        var user = CreateCustomer();
        var repository = new FakeRepository(user);
        var service = CreateService(repository, activeAdminCount: 2);

        await service.GrantRoleAsync(
            actorUserId,
            user.Id,
            AccessRole.Requester);

        var assignment = Assert.Single(
            user.RoleAssignments,
            item => item.Role == AccessRole.Requester);
        Assert.True(assignment.IsActive);
        Assert.Equal(CurrentTime, assignment.GrantedAt);
        Assert.Equal(actorUserId, assignment.GrantedBy.UserId);
        Assert.Equal(1, repository.SaveCalls);
    }

    [Fact]
    public async Task RevokeRoleAsync_LastAdministrator_IsProtected()
    {
        var user = CreateCustomer();
        user.GrantRole(
            Guid.NewGuid(),
            AccessRole.Admin,
            CreatedAt,
            RoleChangeActor.ForProcess("test"));

        var repository = new FakeRepository(user);
        var service = CreateService(repository, activeAdminCount: 1);

        await Assert.ThrowsAsync<LastAdministratorProtectionException>(
            () => service.RevokeRoleAsync(
                Guid.NewGuid(),
                user.Id,
                AccessRole.Admin));

        Assert.True(user.HasEffectiveRole(AccessRole.Admin));
        Assert.Equal(0, repository.SaveCalls);
    }

    [Fact]
    public async Task BlockAsync_OwnAccount_IsRejected()
    {
        var user = CreateCustomer();
        var service = CreateService(
            new FakeRepository(user),
            activeAdminCount: 2);

        await Assert.ThrowsAsync<SelfBlockNotAllowedException>(
            () => service.BlockAsync(user.Id, user.Id));
    }

    [Fact]
    public async Task ActivateAsync_BlockedUser_ActivatesAndSaves()
    {
        var user = CreateCustomer();
        user.Block();
        var repository = new FakeRepository(user);
        var service = CreateService(repository, activeAdminCount: 2);

        await service.ActivateAsync(
            Guid.NewGuid(),
            user.Id);

        Assert.Equal(AccessUserStatus.Active, user.Status);
        Assert.Equal(1, repository.SaveCalls);
    }

    [Fact]
    public async Task GrantRoleAsync_UnknownRole_IsRejected()
    {
        var user = CreateCustomer();
        var service = CreateService(
            new FakeRepository(user),
            activeAdminCount: 2);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => service.GrantRoleAsync(
                Guid.NewGuid(),
                user.Id,
                (AccessRole)0));
    }

    [Fact]
    public async Task GrantRoleAsync_CustomerRole_IsProtected()
    {
        var user = CreateCustomer();
        var service = CreateService(
            new FakeRepository(user),
            activeAdminCount: 2);

        await Assert.ThrowsAsync<ProtectedCustomerRoleException>(
            () => service.GrantRoleAsync(
                Guid.NewGuid(),
                user.Id,
                AccessRole.Customer));
    }

    private static AccessUserAdministrationService CreateService(
        IAccessUserRepository repository,
        long activeAdminCount)
    {
        return new AccessUserAdministrationService(
            repository,
            new StubQueries(activeAdminCount),
            NullAuditTrail.Instance,
            new FixedTimeProvider(CurrentTime),
            new ImmediateApplicationTransaction(),
            new NoOpAdministrationLock());
    }

    private static AccessUser CreateCustomer()
    {
        var user = new AccessUser(
            Guid.NewGuid(),
            "Testovací uživatel",
            "test@example.invalid",
            CreatedAt);

        user.GrantRole(
            Guid.NewGuid(),
            AccessRole.Customer,
            CreatedAt,
            RoleChangeActor.ForProcess("test"));

        return user;
    }


    private sealed class ImmediateApplicationTransaction :
        IApplicationTransaction
    {
        public Task<T> ExecuteAsync<T>(
            Func<CancellationToken, Task<T>> operation,
            CancellationToken cancellationToken = default)
        {
            return operation(cancellationToken);
        }
    }

    private sealed class NoOpAdministrationLock :
        IAccessAdministrationLock
    {
        public Task AcquireAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
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

    private sealed class FakeRepository : IAccessUserRepository
    {
        private readonly AccessUser _user;

        public FakeRepository(AccessUser user)
        {
            _user = user;
        }

        public int SaveCalls { get; private set; }

        public Task<AccessUser?> FindByIdAsync(
            Guid userId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(
                _user.Id == userId
                    ? _user
                    : null);
        }

        public Task<AccessUser?> FindByExternalIdentityAsync(
            ExternalIdentityKey identityKey,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task AddAsync(
            AccessUser user,
            ExternalIdentityKey identityKey,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task SaveAsync(
            AccessUser user,
            CancellationToken cancellationToken)
        {
            Assert.Same(_user, user);
            SaveCalls++;
            return Task.CompletedTask;
        }
    }

    private sealed class StubQueries : IAccessUserQueries
    {
        private readonly long _activeAdminCount;

        public StubQueries(long activeAdminCount)
        {
            _activeAdminCount = activeAdminCount;
        }

        public Task<bool> IsActiveAsync(
            Guid userId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(userId != Guid.Empty);
        }

        public Task<bool> IsActiveCustomerAsync(
            Guid userId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<long> CountActiveUsersWithRoleAsync(
            AccessRole role,
            CancellationToken cancellationToken = default)
        {
            Assert.Equal(AccessRole.Admin, role);
            return Task.FromResult(_activeAdminCount);
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
    }
}
