using FuaPay.Web.Modules.Access.Application;
using FuaPay.Web.Modules.Access.Domain;

namespace FuaPay.Web.Tests.Modules.Access.Application;

public sealed class AccessIdentityServiceTests
{
    private static readonly DateTimeOffset CurrentTime =
        new(2026, 7, 26, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ResolveAsync_WhenIdentityIsNew_CreatesCustomer()
    {
        var repository =
            new FakeAccessUserRepository();

        var identity =
            CreateIdentity();

        var service =
            CreateService(repository);

        var result =
            await service.ResolveAsync(identity);

        Assert.True(result.IsNewUser);
        Assert.Same(repository.User, result.User);
        Assert.NotEqual(Guid.Empty, result.User.Id);

        Assert.Equal(
            identity.DisplayName,
            result.User.DisplayName);

        Assert.Equal(
            identity.Email,
            result.User.Email);

        Assert.Equal(
            CurrentTime,
            result.User.CreatedAt);

        Assert.Equal(
            CurrentTime,
            result.User.LastSeenAt);

        Assert.Equal(
            AccessUserStatus.Active,
            result.User.Status);

        Assert.True(
            result.User.HasEffectiveRole(
                AccessRole.Customer));

        Assert.False(
            result.User.HasEffectiveRole(
                AccessRole.Requester));

        Assert.False(
            result.User.HasEffectiveRole(
                AccessRole.Admin));

        var assignment =
            Assert.Single(
                result.User.RoleAssignments);

        Assert.Equal(
            AccessRole.Customer,
            assignment.Role);

        Assert.Equal(
            CurrentTime,
            assignment.GrantedAt);

        Assert.Equal(
            RoleChangeActorType.Process,
            assignment.GrantedBy.Type);

        Assert.Equal(
            "first-login",
            assignment.GrantedBy.ProcessName);

        Assert.Equal(identity.Key, repository.IdentityKey);
        Assert.Equal(1, repository.AddCalls);
        Assert.Equal(0, repository.SaveCalls);
    }

    [Fact]
    public async Task ResolveAsync_WhenIdentityExists_SynchronizesProfile()
    {
        var user =
            CreateExistingUser();

        user.GrantRole(
            Guid.NewGuid(),
            AccessRole.Customer,
            CurrentTime.AddDays(-1),
            RoleChangeActor.ForProcess(
                "existing-user-import"));

        var repository =
            new FakeAccessUserRepository
            {
                User = user,
                IdentityKey = CreateKey()
            };

        var service =
            CreateService(repository);

        var identity =
            new VerifiedExternalIdentity(
                repository.IdentityKey,
                "  Aktualizované jméno  ",
                "  new@example.cz  ");

        var result =
            await service.ResolveAsync(identity);

        Assert.False(result.IsNewUser);
        Assert.Same(user, result.User);

        Assert.Equal(
            "Aktualizované jméno",
            user.DisplayName);

        Assert.Equal(
            "new@example.cz",
            user.Email);

        Assert.Equal(
            CurrentTime,
            user.LastSeenAt);

        Assert.Single(user.RoleAssignments);
        Assert.Equal(0, repository.AddCalls);
        Assert.Equal(1, repository.SaveCalls);
    }

    [Fact]
    public async Task ResolveAsync_WhenUserIsBlocked_ThrowsWithoutSaving()
    {
        var user =
            CreateExistingUser();

        user.Block();

        var originalName =
            user.DisplayName;

        var originalLastSeenAt =
            user.LastSeenAt;

        var repository =
            new FakeAccessUserRepository
            {
                User = user,
                IdentityKey = CreateKey()
            };

        var service =
            CreateService(repository);

        var exception =
            await Assert.ThrowsAsync<AccessUserBlockedException>(
                async () =>
                {
                    _ = await service.ResolveAsync(
                        new VerifiedExternalIdentity(
                            repository.IdentityKey,
                            "Nové jméno",
                            "new@example.cz"));
                });

        Assert.Equal(user.Id, exception.UserId);
        Assert.Equal(originalName, user.DisplayName);

        Assert.Equal(
            originalLastSeenAt,
            user.LastSeenAt);

        Assert.Equal(0, repository.AddCalls);
        Assert.Equal(0, repository.SaveCalls);
    }

    [Fact]
    public async Task ResolveAsync_WhenExistingUserHasNoRole_DoesNotGrantOne()
    {
        var user =
            CreateExistingUser();

        var repository =
            new FakeAccessUserRepository
            {
                User = user,
                IdentityKey = CreateKey()
            };

        var service =
            CreateService(repository);

        var result =
            await service.ResolveAsync(
                CreateIdentity());

        Assert.False(result.IsNewUser);
        Assert.Empty(user.AssignedRoles);
        Assert.Empty(user.RoleAssignments);
        Assert.Equal(0, repository.AddCalls);
        Assert.Equal(1, repository.SaveCalls);
    }

    [Fact]
    public async Task ResolveAsync_WhenConcurrentCreationAlreadyCommitted_ReturnsExistingUser()
    {
        var identity =
            CreateIdentity();

        var winningUser =
            CreateExistingUser();

        winningUser.GrantRole(
            Guid.NewGuid(),
            AccessRole.Customer,
            CurrentTime,
            RoleChangeActor.ForProcess(
                "first-login"));

        var repository =
            new FakeAccessUserRepository
            {
                AddException =
                    new AccessIdentityConcurrencyException(
                        identity.Key),

                UserAfterAddFailure =
                    winningUser
            };

        var service =
            CreateService(repository);

        var result =
            await service.ResolveAsync(identity);

        Assert.False(result.IsNewUser);
        Assert.Same(winningUser, result.User);
        Assert.Equal(2, repository.FindCalls);
        Assert.Equal(1, repository.AddCalls);
        Assert.Equal(0, repository.SaveCalls);
    }

    [Fact]
    public async Task ResolveAsync_WhenConcurrentCreationCannotBeReloaded_Rethrows()
    {
        var identity =
            CreateIdentity();

        var expectedException =
            new AccessIdentityConcurrencyException(
                identity.Key);

        var repository =
            new FakeAccessUserRepository
            {
                AddException =
                    expectedException
            };

        var service =
            CreateService(repository);

        var exception =
            await Assert.ThrowsAsync<
                AccessIdentityConcurrencyException>(
                () => service.ResolveAsync(identity));

        Assert.Same(expectedException, exception);
        Assert.Equal(2, repository.FindCalls);
        Assert.Equal(1, repository.AddCalls);
        Assert.Equal(0, repository.SaveCalls);
    }

    [Fact]
    public async Task ResolveAsync_ProviderCaseVariation_UsesExistingIdentity()
    {
        var storedKey = new ExternalIdentityKey(
            "microsoft-entra",
            "tenant-a",
            "subject-a");

        var user = CreateExistingUser();

        var repository = new FakeAccessUserRepository
        {
            User = user,
            IdentityKey = storedKey
        };

        var service = CreateService(repository);

        var result = await service.ResolveAsync(
            new VerifiedExternalIdentity(
                new ExternalIdentityKey(
                    "  MICROSOFT-ENTRA  ",
                    "tenant-a",
                    "subject-a"),
                "Aktualizovaný uživatel",
                "updated@example.cz"));

        Assert.False(result.IsNewUser);
        Assert.Same(user, result.User);
        Assert.Equal(0, repository.AddCalls);
        Assert.Equal(1, repository.SaveCalls);
    }

    [Fact]
    public async Task ResolveAsync_UsesFullExternalIdentityKey()
    {
        var storedKey =
            new ExternalIdentityKey(
                "entra-id",
                "other-tenant",
                "subject-123");

        var repository =
            new FakeAccessUserRepository
            {
                User = CreateExistingUser(),
                IdentityKey = storedKey
            };

        var requestedIdentity =
            CreateIdentity();

        var service =
            CreateService(repository);

        var result =
            await service.ResolveAsync(
                requestedIdentity);

        Assert.True(result.IsNewUser);
        Assert.NotSame(repository.PreviousUser, result.User);
        Assert.Equal(requestedIdentity.Key, repository.IdentityKey);
        Assert.Equal(1, repository.AddCalls);
        Assert.Equal(0, repository.SaveCalls);
    }

    private static AccessIdentityService CreateService(
        IAccessUserRepository repository)
    {
        return new AccessIdentityService(
            repository,
            new FixedTimeProvider(CurrentTime));
    }

    private static AccessUser CreateExistingUser()
    {
        return new AccessUser(
            Guid.NewGuid(),
            "Původní jméno",
            "old@example.cz",
            CurrentTime.AddDays(-1));
    }

    private static VerifiedExternalIdentity CreateIdentity()
    {
        return new VerifiedExternalIdentity(
            CreateKey(),
            "Testovací uživatel",
            "user@example.cz");
    }

    private static ExternalIdentityKey CreateKey()
    {
        return new ExternalIdentityKey(
            "entra-id",
            "tul-tenant",
            "subject-123");
    }

    private sealed class FixedTimeProvider : TimeProvider
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

    private sealed class FakeAccessUserRepository :
        IAccessUserRepository
    {
        public AccessUser? User { get; set; }

        public AccessUser? PreviousUser { get; private set; }

        public ExternalIdentityKey? IdentityKey { get; set; }

        public AccessIdentityConcurrencyException? AddException
        {
            get;
            set;
        }

        public AccessUser? UserAfterAddFailure
        {
            get;
            set;
        }

        public int FindCalls { get; private set; }

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
            cancellationToken.ThrowIfCancellationRequested();

            FindCalls++;

            var result =
                IdentityKey == identityKey
                    ? User
                    : null;

            PreviousUser = User;

            return Task.FromResult(result);
        }

        public Task AddAsync(
            AccessUser user,
            ExternalIdentityKey identityKey,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            AddCalls++;

            if (AddException is not null)
            {
                PreviousUser = User;
                User = UserAfterAddFailure;

                IdentityKey =
                    UserAfterAddFailure is null
                        ? null
                        : identityKey;

                throw AddException;
            }

            PreviousUser = User;
            User = user;
            IdentityKey = identityKey;

            return Task.CompletedTask;
        }

        public Task SaveAsync(
            AccessUser user,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            Assert.Same(User, user);
            SaveCalls++;

            return Task.CompletedTask;
        }
    }
}
