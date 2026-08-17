using System.Security.Claims;

using FuaPay.Web.Modules.Access.Application;
using FuaPay.Web.Modules.Access.Domain;
using FuaPay.Web.Modules.Access.Web;

namespace FuaPay.Web.Tests.Modules.Access.Web;

public sealed class AccessSessionSynchronizerTests
{
    [Fact]
    public async Task SynchronizeAsync_UnchangedActiveSession_DoesNotRenew()
    {
        var snapshot = CreateSnapshot(
            AccessRole.Customer,
            AccessRole.Requester);

        var principal =
            AccessClaimsPrincipalFactory.Create(
                snapshot,
                "test");

        var synchronizer = new AccessSessionSynchronizer(
            new StubAccessSessionQueries(snapshot));

        var result = await synchronizer.SynchronizeAsync(
            principal,
            "test");

        Assert.True(result.IsValid);
        Assert.False(result.ShouldRenew);
        Assert.NotNull(result.Principal);
    }

    [Fact]
    public async Task SynchronizeAsync_RevokedRole_RenewsWithoutStaleRole()
    {
        var staleSnapshot = CreateSnapshot(
            AccessRole.Customer,
            AccessRole.Admin);

        var currentPrincipal =
            AccessClaimsPrincipalFactory.Create(
                staleSnapshot,
                "test");

        var currentSnapshot = staleSnapshot with
        {
            Roles = new[] { AccessRole.Customer }
        };

        var synchronizer = new AccessSessionSynchronizer(
            new StubAccessSessionQueries(currentSnapshot));

        var result = await synchronizer.SynchronizeAsync(
            currentPrincipal,
            "test");

        Assert.True(result.IsValid);
        Assert.True(result.ShouldRenew);

        var refreshedPrincipal = Assert.IsType<ClaimsPrincipal>(
            result.Principal);

        Assert.Equal(
            new[] { AccessRole.Customer },
            refreshedPrincipal.FindAccessRoles());

        Assert.False(
            refreshedPrincipal.IsInRole(
                AccessRole.Admin.ToString()));
    }

    [Fact]
    public async Task SynchronizeAsync_GrantedRoleAndProfileChange_RenewsClaims()
    {
        var oldSnapshot = CreateSnapshot(AccessRole.Customer);

        var currentPrincipal =
            AccessClaimsPrincipalFactory.Create(
                oldSnapshot,
                "test");

        var currentSnapshot = oldSnapshot with
        {
            DisplayName = "Aktualizovaný uživatel",
            Email = "updated@example.cz",
            Roles = new[]
            {
                AccessRole.Customer,
                AccessRole.Requester
            }
        };

        var synchronizer = new AccessSessionSynchronizer(
            new StubAccessSessionQueries(currentSnapshot));

        var result = await synchronizer.SynchronizeAsync(
            currentPrincipal,
            "test");

        Assert.True(result.IsValid);
        Assert.True(result.ShouldRenew);

        var refreshedPrincipal = Assert.IsType<ClaimsPrincipal>(
            result.Principal);

        Assert.Equal(
            currentSnapshot.DisplayName,
            refreshedPrincipal.Identity?.Name);

        Assert.Equal(
            currentSnapshot.Email,
            refreshedPrincipal.FindAccessEmail());

        Assert.Equal(
            currentSnapshot.Roles.OrderBy(role => role),
            refreshedPrincipal
                .FindAccessRoles()
                .OrderBy(role => role));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SynchronizeAsync_MissingOrBlockedUser_InvalidatesSession(
        bool userExists)
    {
        var snapshot = CreateSnapshot(AccessRole.Customer);

        var principal =
            AccessClaimsPrincipalFactory.Create(
                snapshot,
                "test");

        AccessSessionSnapshot? currentSnapshot = userExists
            ? snapshot with { Status = AccessUserStatus.Blocked }
            : null;

        var synchronizer = new AccessSessionSynchronizer(
            new StubAccessSessionQueries(currentSnapshot));

        var result = await synchronizer.SynchronizeAsync(
            principal,
            "test");

        Assert.False(result.IsValid);
        Assert.False(result.ShouldRenew);
        Assert.Null(result.Principal);
    }

    [Fact]
    public async Task SynchronizeAsync_DuplicateInternalUserId_InvalidatesSession()
    {
        var snapshot = CreateSnapshot(AccessRole.Customer);

        var principal = AccessClaimsPrincipalFactory.Create(
            snapshot,
            "test");

        var identity = Assert.IsType<ClaimsIdentity>(
            principal.Identity);

        identity.AddClaim(
            new Claim(
                ClaimTypes.NameIdentifier,
                Guid.NewGuid().ToString()));

        var synchronizer = new AccessSessionSynchronizer(
            new StubAccessSessionQueries(snapshot));

        var result = await synchronizer.SynchronizeAsync(
            principal,
            "test");

        Assert.False(result.IsValid);
        Assert.False(result.ShouldRenew);
        Assert.Null(result.Principal);
    }

    [Fact]
    public async Task SynchronizeAsync_UnsupportedRoleClaim_InvalidatesSession()
    {
        var snapshot = CreateSnapshot(AccessRole.Customer);

        var principal = AccessClaimsPrincipalFactory.Create(
            snapshot,
            "test");

        var identity = Assert.IsType<ClaimsIdentity>(
            principal.Identity);

        identity.AddClaim(
            new Claim(
                ClaimTypes.Role,
                "Unsupported"));

        var synchronizer = new AccessSessionSynchronizer(
            new StubAccessSessionQueries(snapshot));

        var result = await synchronizer.SynchronizeAsync(
            principal,
            "test");

        Assert.False(result.IsValid);
        Assert.False(result.ShouldRenew);
        Assert.Null(result.Principal);
    }

    [Fact]
    public async Task SynchronizeAsync_DuplicateRoleClaim_InvalidatesSession()
    {
        var snapshot = CreateSnapshot(AccessRole.Customer);

        var principal = AccessClaimsPrincipalFactory.Create(
            snapshot,
            "test");

        var identity = Assert.IsType<ClaimsIdentity>(
            principal.Identity);

        identity.AddClaim(
            new Claim(
                ClaimTypes.Role,
                AccessRole.Customer.ToString()));

        var synchronizer = new AccessSessionSynchronizer(
            new StubAccessSessionQueries(snapshot));

        var result = await synchronizer.SynchronizeAsync(
            principal,
            "test");

        Assert.False(result.IsValid);
        Assert.False(result.ShouldRenew);
        Assert.Null(result.Principal);
    }

    [Fact]
    public async Task SynchronizeAsync_MissingInternalUserId_InvalidatesSession()
    {
        var principal = new ClaimsPrincipal(
            new ClaimsIdentity(
                [new Claim(ClaimTypes.Name, "Bez ID")],
                "test"));

        var synchronizer = new AccessSessionSynchronizer(
            new StubAccessSessionQueries(null));

        var result = await synchronizer.SynchronizeAsync(
            principal,
            "test");

        Assert.False(result.IsValid);
        Assert.False(result.ShouldRenew);
        Assert.Null(result.Principal);
    }

    private static AccessSessionSnapshot CreateSnapshot(
        params AccessRole[] roles)
    {
        return new AccessSessionSnapshot(
            Guid.NewGuid(),
            "Testovací uživatel",
            "test@example.cz",
            AccessUserStatus.Active,
            roles);
    }

    private sealed class StubAccessSessionQueries :
        IAccessSessionQueries
    {
        private readonly AccessSessionSnapshot? _snapshot;

        public StubAccessSessionQueries(
            AccessSessionSnapshot? snapshot)
        {
            _snapshot = snapshot;
        }

        public Task<AccessSessionSnapshot?> FindAsync(
            Guid userId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_snapshot);
        }
    }
}
