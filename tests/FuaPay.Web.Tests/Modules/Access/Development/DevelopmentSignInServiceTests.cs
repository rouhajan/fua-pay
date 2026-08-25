using FuaPay.Web.BuildingBlocks.Auditing;
using FuaPay.Web.Modules.Access.Application;
using FuaPay.Web.Modules.Access.Development;
using FuaPay.Web.Modules.Access.Domain;

namespace FuaPay.Web.Tests.Modules.Access.Development;

public sealed class DevelopmentSignInServiceTests
{
    private static readonly DateTimeOffset CurrentTime =
        new(2026, 7, 28, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ResolveAsync_NewRequesterCreatesUserAndAddsRole()
    {
        var repository = new FakeRepository();
        var timeProvider = new FixedTimeProvider(CurrentTime);

        var identityService =
            new AccessIdentityService(
                repository,
                NullAuditTrail.Instance,
                timeProvider);

        var service =
            new DevelopmentSignInService(
                identityService,
                repository,
                timeProvider);

        var profile =
            Assert.IsType<DevelopmentIdentityProfile>(
                DevelopmentIdentityProfiles.Find(
                    DevelopmentIdentityProfiles.ThreeDPrintRequesterKey));

        var user =
            await service.ResolveAsync(profile);

        Assert.True(
            user.HasEffectiveRole(
                AccessRole.Customer));

        Assert.True(
            user.HasEffectiveRole(
                AccessRole.Requester));

        Assert.False(
            user.HasEffectiveRole(
                AccessRole.Admin));

        Assert.Equal(1, repository.AddCalls);
        Assert.Equal(1, repository.SaveCalls);
    }

    [Fact]
    public async Task ResolveAsync_ExistingProfileDoesNotDuplicateRoles()
    {
        var repository = new FakeRepository();
        var timeProvider = new FixedTimeProvider(CurrentTime);

        var identityService =
            new AccessIdentityService(
                repository,
                NullAuditTrail.Instance,
                timeProvider);

        var service =
            new DevelopmentSignInService(
                identityService,
                repository,
                timeProvider);

        var profile =
            Assert.IsType<DevelopmentIdentityProfile>(
                DevelopmentIdentityProfiles.Find(
                    DevelopmentIdentityProfiles.AdministratorKey));

        _ = await service.ResolveAsync(profile);

        repository.ResetSaveCalls();

        var user =
            await service.ResolveAsync(profile);

        Assert.Equal(
            2,
            user.AssignedRoles.Count);

        Assert.Equal(
            2,
            user.RoleAssignments.Count(
                assignment => assignment.IsActive));

        Assert.Equal(1, repository.SaveCalls);
    }

    [Fact]
    public async Task ResolveAsync_ProfileChangeRevokesStaleMutableRole()
    {
        var repository = new FakeRepository();
        var timeProvider = new FixedTimeProvider(CurrentTime);
        var identityService = new AccessIdentityService(
            repository,
            NullAuditTrail.Instance,
            timeProvider);
        var service = new DevelopmentSignInService(
            identityService,
            repository,
            timeProvider);

        var requesterProfile =
            Assert.IsType<DevelopmentIdentityProfile>(
                DevelopmentIdentityProfiles.Find(
                    DevelopmentIdentityProfiles.ThreeDPrintRequesterKey));

        var requester = await service.ResolveAsync(requesterProfile);

        Assert.True(
            requester.HasEffectiveRole(AccessRole.Requester));

        var customerOnlyProfile = requesterProfile with
        {
            Group = DevelopmentIdentityProfileGroup.Customer,
            Roles = Array.AsReadOnly(
                new[] { AccessRole.Customer })
        };

        var customer = await service.ResolveAsync(customerOnlyProfile);

        Assert.True(
            customer.HasEffectiveRole(AccessRole.Customer));
        Assert.False(
            customer.HasEffectiveRole(AccessRole.Requester));
        Assert.False(
            customer.HasEffectiveRole(AccessRole.Admin));
    }

    private sealed class FakeRepository :
        IAccessUserRepository
    {
        public AccessUser? User { get; private set; }

        public ExternalIdentityKey? IdentityKey { get; private set; }

        public int AddCalls { get; private set; }

        public int SaveCalls { get; private set; }

        public Task<AccessUser?> FindByIdAsync(
            Guid userId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(
                User?.Id == userId
                    ? User
                    : null);
        }

        public Task<AccessUser?> FindByExternalIdentityAsync(
            ExternalIdentityKey identityKey,
            CancellationToken cancellationToken)
        {
            var matches =
                IdentityKey == identityKey;

            return Task.FromResult(
                matches
                    ? User
                    : null);
        }

        public Task AddAsync(
            AccessUser user,
            ExternalIdentityKey identityKey,
            CancellationToken cancellationToken)
        {
            User = user;
            IdentityKey = identityKey;
            AddCalls++;

            return Task.CompletedTask;
        }

        public Task SaveAsync(
            AccessUser user,
            CancellationToken cancellationToken)
        {
            Assert.Same(User, user);
            SaveCalls++;

            return Task.CompletedTask;
        }

        public void ResetSaveCalls()
        {
            SaveCalls = 0;
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
