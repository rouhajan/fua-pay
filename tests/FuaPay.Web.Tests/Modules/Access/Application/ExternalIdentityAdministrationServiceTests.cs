using FuaPay.Web.BuildingBlocks.Auditing;
using FuaPay.Web.Modules.Access.Application;
using FuaPay.Web.Modules.Access.Domain;
using FuaPay.Web.Modules.Access.Infrastructure.Entra;

namespace FuaPay.Web.Tests.Modules.Access.Application;

public sealed class ExternalIdentityAdministrationServiceTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 16, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task AttachEntraIdentityAsync_AttachesStableKeyAndAuditsActor()
    {
        var administratorId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var objectId = Guid.NewGuid();
        var repository = new RecordingRepository();
        var audit = new RecordingAuditTrail();
        var service = new ExternalIdentityAdministrationService(
            new StubQueries(User(userId)),
            repository,
            audit,
            new FixedTimeProvider(Now));

        var changed = await service.AttachEntraIdentityAsync(
            administratorId,
            userId,
            tenantId,
            objectId);

        Assert.True(changed);
        Assert.Equal(userId, repository.UserId);
        Assert.Equal(
            EntraAuthenticationDefaults.ExternalIdentityProvider,
            repository.IdentityKey?.Provider);
        Assert.Equal(
            tenantId.ToString("D"),
            repository.IdentityKey?.Tenant);
        Assert.Equal(
            objectId.ToString("D"),
            repository.IdentityKey?.Subject);
        var entry = Assert.Single(audit.Entries);
        Assert.Equal(administratorId, entry.ActorUserId);
        Assert.Equal(
            "access.external-identity.attached",
            entry.Action);
    }

    [Fact]
    public async Task AttachEntraIdentityAsync_SameIdentity_IsIdempotent()
    {
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var objectId = Guid.NewGuid();
        var repository = new RecordingRepository();
        var service = new ExternalIdentityAdministrationService(
            new StubQueries(
                User(
                    userId,
                    new AccessExternalIdentityReadModel(
                        EntraAuthenticationDefaults
                            .ExternalIdentityProvider,
                        tenantId.ToString("D"),
                        objectId.ToString("D")))),
            repository,
            NullAuditTrail.Instance,
            new FixedTimeProvider(Now));

        var changed = await service.AttachEntraIdentityAsync(
            Guid.NewGuid(),
            userId,
            tenantId,
            objectId);

        Assert.False(changed);
        Assert.Null(repository.IdentityKey);
    }

    [Fact]
    public async Task AttachEntraIdentityAsync_DifferentObjectInSameTenant_IsRejected()
    {
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var service = new ExternalIdentityAdministrationService(
            new StubQueries(
                User(
                    userId,
                    new AccessExternalIdentityReadModel(
                        EntraAuthenticationDefaults
                            .ExternalIdentityProvider,
                        tenantId.ToString("D"),
                        Guid.NewGuid().ToString("D")))),
            new RecordingRepository(),
            NullAuditTrail.Instance,
            new FixedTimeProvider(Now));

        await Assert.ThrowsAsync<
            ExternalIdentityProviderAlreadyAssignedException>(
                () => service.AttachEntraIdentityAsync(
                    Guid.NewGuid(),
                    userId,
                    tenantId,
                    Guid.NewGuid()));
    }

    private static AccessUserDetail User(
        Guid userId,
        params AccessExternalIdentityReadModel[] identities) =>
        new(
            userId,
            "Test User",
            "user@example.invalid",
            AccessUserStatus.Active,
            Now,
            Now,
            1,
            [AccessRole.Customer],
            identities,
            []);

    private sealed class RecordingRepository :
        IExternalIdentityLinkRepository
    {
        public Guid? UserId { get; private set; }

        public ExternalIdentityKey? IdentityKey { get; private set; }

        public Task AttachAsync(
            Guid userId,
            ExternalIdentityKey identityKey,
            CancellationToken cancellationToken = default)
        {
            UserId = userId;
            IdentityKey = identityKey;
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingAuditTrail : IAuditTrail
    {
        public List<AuditEntry> Entries { get; } = [];

        public void Stage(AuditEntry entry) => Entries.Add(entry);

        public Task WriteAsync(
            AuditEntry entry,
            CancellationToken cancellationToken = default)
        {
            Entries.Add(entry);
            return Task.CompletedTask;
        }
    }

    private sealed class StubQueries(AccessUserDetail user) :
        IAccessUserQueries
    {
        public Task<AccessUserDetail?> FindDetailAsync(
            Guid userId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<AccessUserDetail?>(
                user.Id == userId ? user : null);

        public Task<AccessUserPage> ListAsync(
            AccessUserListRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<AccessUserOption>>
            ListActiveCustomersAsync(
                CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyDictionary<Guid, AccessUserOption>>
            FindOptionsAsync(
                IEnumerable<Guid> userIds,
                CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> IsActiveAsync(
            Guid userId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> IsActiveCustomerAsync(
            Guid userId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<long> CountActiveUsersWithRoleAsync(
            AccessRole role,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) :
        TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
