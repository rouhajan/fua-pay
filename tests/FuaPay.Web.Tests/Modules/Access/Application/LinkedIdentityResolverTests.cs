using FuaPay.Web.Modules.Access.Application;
using FuaPay.Web.Modules.Access.Domain;
using FuaPay.Web.Modules.Access.Infrastructure.Entra;

namespace FuaPay.Web.Tests.Modules.Access.Application;

public sealed class LinkedIdentityResolverTests
{
    private static readonly DateTimeOffset CreatedAt =
        new(2026, 8, 27, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ResolveMicrosoftEntraAsync_KnownActiveCustomerReturnsExistingUserIdWithoutWrites()
    {
        var tenantId = Guid.NewGuid();
        var objectId = Guid.NewGuid();
        var user = CreateUser(customer: true);
        var repository = new RecordingRepository(
            Key(tenantId, objectId),
            user);
        var resolver = new LinkedIdentityResolver(repository);

        var result = await resolver.ResolveMicrosoftEntraAsync(
            tenantId,
            objectId);

        Assert.Equal(user.Id, result);
        Assert.Equal(Key(tenantId, objectId), repository.LastIdentityKey);
        Assert.Equal(1, repository.FindCount);
        Assert.Equal(0, repository.AddCount);
        Assert.Equal(0, repository.SaveCount);
    }

    [Fact]
    public async Task ResolveMicrosoftEntraAsync_UnknownIdentityDoesNotProvisionOrWrite()
    {
        var repository = new RecordingRepository(
            Key(Guid.NewGuid(), Guid.NewGuid()),
            CreateUser(customer: true));
        var resolver = new LinkedIdentityResolver(repository);

        await Assert.ThrowsAsync<LinkedIdentityNotFoundException>(
            () => resolver.ResolveMicrosoftEntraAsync(
                Guid.NewGuid(),
                Guid.NewGuid()));

        Assert.Equal(1, repository.FindCount);
        Assert.Equal(0, repository.AddCount);
        Assert.Equal(0, repository.SaveCount);
    }

    [Fact]
    public async Task ResolveMicrosoftEntraAsync_SameProfileEmailOnDifferentIdentityIsNotUsed()
    {
        var linkedTenantId = Guid.NewGuid();
        var linkedObjectId = Guid.NewGuid();
        var repository = new RecordingRepository(
            Key(linkedTenantId, linkedObjectId),
            CreateUser(
                customer: true,
                email: "same-profile@example.cz"));
        var resolver = new LinkedIdentityResolver(repository);

        await Assert.ThrowsAsync<LinkedIdentityNotFoundException>(
            () => resolver.ResolveMicrosoftEntraAsync(
                linkedTenantId,
                Guid.NewGuid()));

        Assert.Equal(0, repository.AddCount);
        Assert.Equal(0, repository.SaveCount);
    }

    [Fact]
    public async Task ResolveMicrosoftEntraAsync_BlockedCustomerIsDeniedWithoutWrites()
    {
        var tenantId = Guid.NewGuid();
        var objectId = Guid.NewGuid();
        var user = CreateUser(customer: true);
        user.Block();
        var repository = new RecordingRepository(
            Key(tenantId, objectId),
            user);
        var resolver = new LinkedIdentityResolver(repository);

        await Assert.ThrowsAsync<LinkedIdentityNotEligibleException>(
            () => resolver.ResolveMicrosoftEntraAsync(
                tenantId,
                objectId));

        Assert.Equal(0, repository.AddCount);
        Assert.Equal(0, repository.SaveCount);
    }

    [Fact]
    public async Task ResolveMicrosoftEntraAsync_UserWithoutCustomerRoleIsDeniedWithoutWrites()
    {
        var tenantId = Guid.NewGuid();
        var objectId = Guid.NewGuid();
        var repository = new RecordingRepository(
            Key(tenantId, objectId),
            CreateUser(customer: false));
        var resolver = new LinkedIdentityResolver(repository);

        await Assert.ThrowsAsync<LinkedIdentityNotEligibleException>(
            () => resolver.ResolveMicrosoftEntraAsync(
                tenantId,
                objectId));

        Assert.Equal(0, repository.AddCount);
        Assert.Equal(0, repository.SaveCount);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ResolveMicrosoftEntraAsync_EmptyStableIdentifierIsRejected(
        bool emptyTenant)
    {
        var repository = new RecordingRepository(null, null);
        var resolver = new LinkedIdentityResolver(repository);

        await Assert.ThrowsAsync<ArgumentException>(
            () => resolver.ResolveMicrosoftEntraAsync(
                emptyTenant ? Guid.Empty : Guid.NewGuid(),
                emptyTenant ? Guid.NewGuid() : Guid.Empty));

        Assert.Equal(0, repository.FindCount);
    }

    private static AccessUser CreateUser(
        bool customer,
        string? email = "student@example.cz")
    {
        var user = new AccessUser(
            Guid.NewGuid(),
            "Student",
            email,
            CreatedAt);

        if (customer)
        {
            user.GrantRole(
                Guid.NewGuid(),
                AccessRole.Customer,
                CreatedAt,
                RoleChangeActor.ForProcess("test"));
        }

        return user;
    }

    private static ExternalIdentityKey Key(
        Guid tenantId,
        Guid objectId) =>
        ExternalIdentityKey.FromGuidIdentifiers(
            EntraAuthenticationDefaults.ExternalIdentityProvider,
            tenantId.ToString("D"),
            objectId.ToString("D"));

    private sealed class RecordingRepository : IAccessUserRepository
    {
        private readonly ExternalIdentityKey? _identityKey;
        private readonly AccessUser? _user;

        public RecordingRepository(
            ExternalIdentityKey? identityKey,
            AccessUser? user)
        {
            _identityKey = identityKey;
            _user = user;
        }

        public int FindCount { get; private set; }

        public int AddCount { get; private set; }

        public int SaveCount { get; private set; }

        public ExternalIdentityKey? LastIdentityKey { get; private set; }

        public Task<AccessUser?> FindByIdAsync(
            Guid userId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<AccessUser?> FindByExternalIdentityAsync(
            ExternalIdentityKey identityKey,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            FindCount++;
            LastIdentityKey = identityKey;
            return Task.FromResult(
                identityKey == _identityKey ? _user : null);
        }

        public Task AddAsync(
            AccessUser user,
            ExternalIdentityKey identityKey,
            CancellationToken cancellationToken)
        {
            AddCount++;
            return Task.CompletedTask;
        }

        public Task SaveAsync(
            AccessUser user,
            CancellationToken cancellationToken)
        {
            SaveCount++;
            return Task.CompletedTask;
        }
    }
}
