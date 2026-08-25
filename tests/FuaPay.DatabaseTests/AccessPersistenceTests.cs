using FuaPay.Web.BuildingBlocks.Auditing;
using FuaPay.Web.BuildingBlocks.Persistence;
using FuaPay.Web.Modules.Access.Application;
using FuaPay.Web.Modules.Access.Domain;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FuaPay.DatabaseTests;

public sealed class AccessPersistenceTests :
    IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly DateTimeOffset TestTime =
        new(
            2026,
            7,
            26,
            12,
            0,
            0,
            TimeSpan.Zero);

    private readonly WebApplicationFactory<Program> _factory;

    public AccessPersistenceTests(
        WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ResolveAsync_FirstLoginAndProfileSynchronization_RoundTripThroughPostgreSql()
    {
        var identityKey =
            CreateIdentityKey();

        try
        {
            Guid userId;

            using (var firstScope = _factory.Services.CreateScope())
            {
                var repository =
                    firstScope.ServiceProvider
                        .GetRequiredService<
                            IAccessUserRepository>();

                var service =
                    new AccessIdentityService(
                        repository,
                        firstScope.ServiceProvider
                            .GetRequiredService<IAuditTrail>(),
                        new FixedTimeProvider(
                            TestTime));

                var result =
                    await service.ResolveAsync(
                        new VerifiedExternalIdentity(
                            identityKey,
                            " První uživatel ",
                            "first@example.cz"));

                Assert.True(result.IsNewUser);
                Assert.Equal(
                    "První uživatel",
                    result.User.DisplayName);
                Assert.Equal(
                    "first@example.cz",
                    result.User.Email);
                Assert.Equal(
                    AccessUserStatus.Active,
                    result.User.Status);
                Assert.Equal(
                    TestTime,
                    result.User.CreatedAt);
                Assert.Equal(
                    TestTime,
                    result.User.LastSeenAt);
                Assert.True(
                    result.User.HasEffectiveRole(
                        AccessRole.Customer));
                Assert.False(
                    result.User.HasEffectiveRole(
                        AccessRole.Requester));
                Assert.False(
                    result.User.HasEffectiveRole(
                        AccessRole.Admin));

                var customerAssignment =
                    Assert.Single(
                        result.User.RoleAssignments);

                Assert.Equal(
                    AccessRole.Customer,
                    customerAssignment.Role);
                Assert.Equal(
                    RoleChangeActorType.Process,
                    customerAssignment.GrantedBy.Type);
                Assert.Equal(
                    "first-login",
                    customerAssignment
                        .GrantedBy
                        .ProcessName);

                userId = result.User.Id;
            }

            using (var secondScope = _factory.Services.CreateScope())
            {
                var repository =
                    secondScope.ServiceProvider
                        .GetRequiredService<
                            IAccessUserRepository>();

                var service =
                    new AccessIdentityService(
                        repository,
                        secondScope.ServiceProvider
                            .GetRequiredService<IAuditTrail>(),
                        new FixedTimeProvider(
                            TestTime.AddMinutes(5)));

                var equivalentIdentityKey =
                    new ExternalIdentityKey(
                        "  DATABASE-TESTS  ",
                        identityKey.Tenant,
                        identityKey.Subject);

                var result =
                    await service.ResolveAsync(
                        new VerifiedExternalIdentity(
                            equivalentIdentityKey,
                            "Aktualizovaný uživatel",
                            "updated@example.cz"));

                Assert.False(result.IsNewUser);
                Assert.Equal(
                    userId,
                    result.User.Id);
                Assert.Equal(
                    "Aktualizovaný uživatel",
                    result.User.DisplayName);
                Assert.Equal(
                    "updated@example.cz",
                    result.User.Email);
                Assert.Equal(
                    TestTime,
                    result.User.CreatedAt);
                Assert.Equal(
                    TestTime.AddMinutes(5),
                    result.User.LastSeenAt);
                Assert.Single(
                    result.User.RoleAssignments);
            }

            using var verificationScope =
                _factory.Services.CreateScope();

            var verificationRepository =
                verificationScope.ServiceProvider
                    .GetRequiredService<
                        IAccessUserRepository>();

            var persistedUser =
                Assert.IsType<AccessUser>(
                    await verificationRepository
                        .FindByExternalIdentityAsync(
                            identityKey,
                            CancellationToken.None));

            Assert.Equal(userId, persistedUser.Id);
            Assert.Equal(
                "Aktualizovaný uživatel",
                persistedUser.DisplayName);
            Assert.Equal(
                "updated@example.cz",
                persistedUser.Email);
            Assert.Equal(
                TestTime,
                persistedUser.CreatedAt);
            Assert.Equal(
                TestTime.AddMinutes(5),
                persistedUser.LastSeenAt);
            Assert.True(
                persistedUser.HasEffectiveRole(
                    AccessRole.Customer));
            Assert.Single(
                persistedUser.RoleAssignments);

            await AssertProvisioningAuditCountAsync(
                userId,
                expectedCount: 1);
        }
        finally
        {
            await DeleteIdentityAsync(
                identityKey);
        }
    }

    [Fact]
    public async Task RoleHistory_RevokeAndRegrantAtSameTime_RoundTripsThroughPostgreSql()
    {
        var identityKey =
            CreateIdentityKey();

        var firstRequesterAssignmentId =
            Guid.NewGuid();

        var secondRequesterAssignmentId =
            Guid.NewGuid();

        var transitionTime =
            TestTime.AddMinutes(20);

        try
        {
            await SeedUserAsync(identityKey);

            using (var grantScope = _factory.Services.CreateScope())
            {
                var repository =
                    grantScope.ServiceProvider
                        .GetRequiredService<
                            IAccessUserRepository>();

                var user =
                    Assert.IsType<AccessUser>(
                        await repository
                            .FindByExternalIdentityAsync(
                                identityKey,
                                CancellationToken.None));

                user.GrantRole(
                    firstRequesterAssignmentId,
                    AccessRole.Requester,
                    TestTime.AddMinutes(10),
                    RoleChangeActor.ForProcess(
                        "manual-grant"));

                await repository.SaveAsync(
                    user,
                    CancellationToken.None);
            }

            using (var transitionScope =
                _factory.Services.CreateScope())
            {
                var repository =
                    transitionScope.ServiceProvider
                        .GetRequiredService<
                            IAccessUserRepository>();

                var user =
                    Assert.IsType<AccessUser>(
                        await repository
                            .FindByExternalIdentityAsync(
                                identityKey,
                                CancellationToken.None));

                user.RevokeRole(
                    AccessRole.Requester,
                    transitionTime,
                    RoleChangeActor.ForProcess(
                        "manual-revoke"));

                user.GrantRole(
                    secondRequesterAssignmentId,
                    AccessRole.Requester,
                    transitionTime,
                    RoleChangeActor.ForProcess(
                        "manual-restore"));

                await repository.SaveAsync(
                    user,
                    CancellationToken.None);
            }

            using var verificationScope =
                _factory.Services.CreateScope();

            var verificationRepository =
                verificationScope.ServiceProvider
                    .GetRequiredService<
                        IAccessUserRepository>();

            var persistedUser =
                Assert.IsType<AccessUser>(
                    await verificationRepository
                        .FindByExternalIdentityAsync(
                            identityKey,
                            CancellationToken.None));

            Assert.True(
                persistedUser.HasEffectiveRole(
                    AccessRole.Customer));

            Assert.True(
                persistedUser.HasEffectiveRole(
                    AccessRole.Requester));

            Assert.Equal(
                3,
                persistedUser.RoleAssignments.Count);

            var requesterAssignments =
                persistedUser.RoleAssignments
                    .Where(
                        assignment =>
                            assignment.Role ==
                            AccessRole.Requester)
                    .ToArray();

            Assert.Equal(
                2,
                requesterAssignments.Length);

            var revokedAssignment =
                Assert.Single(
                    requesterAssignments,
                    assignment =>
                        assignment.Id ==
                        firstRequesterAssignmentId);

            Assert.False(
                revokedAssignment.IsActive);
            Assert.Equal(
                transitionTime,
                revokedAssignment.RevokedAt);
            Assert.Equal(
                RoleChangeActorType.Process,
                revokedAssignment.RevokedBy!.Type);
            Assert.Equal(
                "manual-revoke",
                revokedAssignment
                    .RevokedBy
                    .ProcessName);

            var activeAssignment =
                Assert.Single(
                    requesterAssignments,
                    assignment =>
                        assignment.Id ==
                        secondRequesterAssignmentId);

            Assert.True(
                activeAssignment.IsActive);
            Assert.Equal(
                transitionTime,
                activeAssignment.GrantedAt);
            Assert.Equal(
                RoleChangeActorType.Process,
                activeAssignment.GrantedBy.Type);
            Assert.Equal(
                "manual-restore",
                activeAssignment
                    .GrantedBy
                    .ProcessName);
        }
        finally
        {
            await DeleteIdentityAsync(
                identityKey);
        }
    }

    [Fact]
    public async Task BlockedUser_RoundTripsAndCannotResolveAgain()
    {
        var identityKey =
            CreateIdentityKey();

        Guid userId = Guid.Empty;

        try
        {
            userId =
                await SeedUserAsync(
                    identityKey);

            using (var blockScope = _factory.Services.CreateScope())
            {
                var repository =
                    blockScope.ServiceProvider
                        .GetRequiredService<
                            IAccessUserRepository>();

                var user =
                    Assert.IsType<AccessUser>(
                        await repository
                            .FindByExternalIdentityAsync(
                                identityKey,
                                CancellationToken.None));

                user.Block();

                await repository.SaveAsync(
                    user,
                    CancellationToken.None);
            }

            using (var resolveScope =
                _factory.Services.CreateScope())
            {
                var repository =
                    resolveScope.ServiceProvider
                        .GetRequiredService<
                            IAccessUserRepository>();

                var service =
                    new AccessIdentityService(
                        repository,
                        resolveScope.ServiceProvider
                            .GetRequiredService<IAuditTrail>(),
                        new FixedTimeProvider(
                            TestTime.AddMinutes(30)));

                var exception =
                    await Assert.ThrowsAsync<
                        AccessUserBlockedException>(
                        () =>
                            service.ResolveAsync(
                                new VerifiedExternalIdentity(
                                    identityKey,
                                    "Pokus o změnu",
                                    "changed@example.cz")));

                Assert.Equal(
                    userId,
                    exception.UserId);
            }

            using var verificationScope =
                _factory.Services.CreateScope();

            var verificationRepository =
                verificationScope.ServiceProvider
                    .GetRequiredService<
                        IAccessUserRepository>();

            var persistedUser =
                Assert.IsType<AccessUser>(
                    await verificationRepository
                        .FindByExternalIdentityAsync(
                            identityKey,
                            CancellationToken.None));

            Assert.Equal(
                AccessUserStatus.Blocked,
                persistedUser.Status);
            Assert.Equal(
                "Databázový uživatel",
                persistedUser.DisplayName);
            Assert.Equal(
                "database@example.cz",
                persistedUser.Email);
            Assert.Equal(
                TestTime,
                persistedUser.LastSeenAt);
            Assert.False(
                persistedUser.HasEffectiveRole(
                    AccessRole.Customer));
            Assert.Contains(
                AccessRole.Customer,
                persistedUser.AssignedRoles);

            var accessQueries =
                verificationScope.ServiceProvider
                    .GetRequiredService<IAccessUserQueries>();

            Assert.False(
                await accessQueries.IsActiveAsync(userId));
        }
        finally
        {
            await DeleteIdentityAsync(
                identityKey);
        }
    }

    [Fact]
    public async Task SessionQueries_ReturnCurrentProfileStatusAndOnlyActiveRoles()
    {
        var identityKey = CreateIdentityKey();

        try
        {
            var userId = await SeedUserAsync(identityKey);

            using (var updateScope = _factory.Services.CreateScope())
            {
                var repository =
                    updateScope.ServiceProvider
                        .GetRequiredService<IAccessUserRepository>();

                var user =
                    Assert.IsType<AccessUser>(
                        await repository.FindByExternalIdentityAsync(
                            identityKey,
                            CancellationToken.None));

                user.GrantRole(
                    Guid.NewGuid(),
                    AccessRole.Requester,
                    TestTime.AddMinutes(10),
                    RoleChangeActor.ForProcess("session-test"));

                user.RevokeRole(
                    AccessRole.Requester,
                    TestTime.AddMinutes(20),
                    RoleChangeActor.ForProcess("session-test"));

                user.SynchronizeProfile(
                    "Aktuální profil",
                    "current@example.cz",
                    TestTime.AddMinutes(20));

                await repository.SaveAsync(
                    user,
                    CancellationToken.None);
            }

            using var queryScope = _factory.Services.CreateScope();

            var queries =
                queryScope.ServiceProvider
                    .GetRequiredService<IAccessSessionQueries>();

            var snapshot = Assert.IsType<AccessSessionSnapshot>(
                await queries.FindAsync(userId));

            Assert.Equal(userId, snapshot.UserId);
            Assert.Equal("Aktuální profil", snapshot.DisplayName);
            Assert.Equal("current@example.cz", snapshot.Email);
            Assert.Equal(AccessUserStatus.Active, snapshot.Status);
            Assert.Equal(
                new[] { AccessRole.Customer },
                snapshot.Roles);
        }
        finally
        {
            await DeleteIdentityAsync(identityKey);
        }
    }

    [Fact]
    public async Task SaveAsync_WhenUserWasChangedAfterLoad_ThrowsAndKeepsWinningState()
    {
        var identityKey =
            CreateIdentityKey();

        Guid userId = Guid.Empty;

        try
        {
            userId =
                await SeedUserAsync(
                    identityKey);

            using (
                var winningScope =
                    _factory.Services.CreateScope())
            using (
                var staleScope =
                    _factory.Services.CreateScope())
            {
                var winningRepository =
                    winningScope.ServiceProvider
                        .GetRequiredService<
                            IAccessUserRepository>();

                var staleRepository =
                    staleScope.ServiceProvider
                        .GetRequiredService<
                            IAccessUserRepository>();

                var winningUser =
                    Assert.IsType<AccessUser>(
                        await winningRepository
                            .FindByExternalIdentityAsync(
                                identityKey,
                                CancellationToken.None));

                var staleUser =
                    Assert.IsType<AccessUser>(
                        await staleRepository
                            .FindByExternalIdentityAsync(
                                identityKey,
                                CancellationToken.None));

                winningUser.SynchronizeProfile(
                    "Vítězná změna",
                    "winner@example.cz",
                    TestTime.AddMinutes(40));

                staleUser.SynchronizeProfile(
                    "Zastaralá změna",
                    "stale@example.cz",
                    TestTime.AddMinutes(50));

                await winningRepository.SaveAsync(
                    winningUser,
                    CancellationToken.None);

                var exception =
                    await Assert.ThrowsAsync<
                        AccessUserConcurrencyException>(
                        () =>
                            staleRepository.SaveAsync(
                                staleUser,
                                CancellationToken.None));

                Assert.Equal(
                    userId,
                    exception.UserId);
            }

            using var verificationScope =
                _factory.Services.CreateScope();

            var verificationRepository =
                verificationScope.ServiceProvider
                    .GetRequiredService<
                        IAccessUserRepository>();

            var persistedUser =
                Assert.IsType<AccessUser>(
                    await verificationRepository
                        .FindByExternalIdentityAsync(
                            identityKey,
                            CancellationToken.None));

            Assert.Equal(
                "Vítězná změna",
                persistedUser.DisplayName);
            Assert.Equal(
                "winner@example.cz",
                persistedUser.Email);
            Assert.Equal(
                TestTime.AddMinutes(40),
                persistedUser.LastSeenAt);
        }
        finally
        {
            await DeleteIdentityAsync(
                identityKey);
        }
    }

    [Fact]
    public async Task ResolveAsync_WhenTwoFirstLoginsRace_CreatesSingleUserAndReturnsSameIdentity()
    {
        var identityKey =
            CreateIdentityKey();

        try
        {
            using var firstScope =
                _factory.Services.CreateScope();

            using var secondScope =
                _factory.Services.CreateScope();

            var coordinator =
                new InitialLookupCoordinator(
                    participantCount: 2);

            var firstRepository =
                new CoordinatedAccessUserRepository(
                    firstScope.ServiceProvider
                        .GetRequiredService<
                            IAccessUserRepository>(),
                    coordinator);

            var secondRepository =
                new CoordinatedAccessUserRepository(
                    secondScope.ServiceProvider
                        .GetRequiredService<
                            IAccessUserRepository>(),
                    coordinator);

            var firstService =
                new AccessIdentityService(
                    firstRepository,
                    firstScope.ServiceProvider
                        .GetRequiredService<IAuditTrail>(),
                    new FixedTimeProvider(
                        TestTime.AddHours(1)));

            var secondService =
                new AccessIdentityService(
                    secondRepository,
                    secondScope.ServiceProvider
                        .GetRequiredService<IAuditTrail>(),
                    new FixedTimeProvider(
                        TestTime.AddHours(1)));

            var verifiedIdentity =
                new VerifiedExternalIdentity(
                    identityKey,
                    "Souběžný uživatel",
                    "race@example.cz");

            using var cancellationSource =
                new CancellationTokenSource(
                    TimeSpan.FromSeconds(10));

            var results =
                await Task.WhenAll(
                    firstService.ResolveAsync(
                        verifiedIdentity,
                        cancellationSource.Token),
                    secondService.ResolveAsync(
                        verifiedIdentity,
                        cancellationSource.Token));

            Assert.Equal(
                1,
                results.Count(
                    result =>
                        result.IsNewUser));

            Assert.Equal(
                1,
                results.Count(
                    result =>
                        !result.IsNewUser));

            Assert.Equal(
                results[0].User.Id,
                results[1].User.Id);

            using var verificationScope =
                _factory.Services.CreateScope();

            var verificationRepository =
                verificationScope.ServiceProvider
                    .GetRequiredService<
                        IAccessUserRepository>();

            var persistedUser =
                Assert.IsType<AccessUser>(
                    await verificationRepository
                        .FindByExternalIdentityAsync(
                            identityKey,
                            CancellationToken.None));

            Assert.Equal(
                results[0].User.Id,
                persistedUser.Id);
            Assert.Equal(
                "Souběžný uživatel",
                persistedUser.DisplayName);
            Assert.Equal(
                "race@example.cz",
                persistedUser.Email);
            Assert.True(
                persistedUser.HasEffectiveRole(
                    AccessRole.Customer));

            var assignment =
                Assert.Single(
                    persistedUser.RoleAssignments);

            Assert.Equal(
                AccessRole.Customer,
                assignment.Role);
            Assert.Equal(
                "first-login",
                assignment.GrantedBy.ProcessName);

            await AssertProvisioningAuditCountAsync(
                persistedUser.Id,
                expectedCount: 1);
        }
        finally
        {
            await DeleteIdentityAsync(
                identityKey);
        }
    }

    [Fact]
    public async Task ResolveAsync_ProvisioningAuditFailure_RollsBackNewIdentity()
    {
        var identityKey = CreateIdentityKey();
        var duplicateAudit = AuditEntry.ForProcess(
            "database-test",
            "test.audit-duplicate",
            "test",
            Guid.NewGuid().ToString(),
            "Audit row used to force provisioning rollback.",
            TestTime);

        try
        {
            using (var auditScope = _factory.Services.CreateScope())
            {
                await auditScope.ServiceProvider
                    .GetRequiredService<IAuditTrail>()
                    .WriteAsync(duplicateAudit);
            }

            using (var provisioningScope =
                _factory.Services.CreateScope())
            {
                var service = new AccessIdentityService(
                    provisioningScope.ServiceProvider
                        .GetRequiredService<IAccessUserRepository>(),
                    new DuplicateAuditTrail(
                        provisioningScope.ServiceProvider
                            .GetRequiredService<IAuditTrail>(),
                        duplicateAudit),
                    new FixedTimeProvider(TestTime));

                await Assert.ThrowsAsync<DbUpdateException>(
                    () => service.ResolveAsync(
                        new VerifiedExternalIdentity(
                            identityKey,
                            "Rollback uživatel",
                            "rollback@example.cz")));
            }

            using var verificationScope =
                _factory.Services.CreateScope();
            var persisted = await verificationScope.ServiceProvider
                .GetRequiredService<IAccessUserRepository>()
                .FindByExternalIdentityAsync(
                    identityKey,
                    CancellationToken.None);

            Assert.Null(persisted);
        }
        finally
        {
            using (var auditCleanupScope =
                _factory.Services.CreateScope())
            {
                await auditCleanupScope.ServiceProvider
                    .GetRequiredService<FuaPayDbContext>()
                    .Database.ExecuteSqlInterpolatedAsync(
                        $"DELETE FROM audit.events WHERE id = {duplicateAudit.Id}");
            }

            await DeleteIdentityAsync(identityKey);
        }
    }

    [Fact]
    public async Task ExternalIdentityLinkRepository_RejectsSecondIdentityForSameTenant()
    {
        var identityKey = CreateIdentityKey();
        var tenant = Guid.NewGuid().ToString("D");
        var firstEntraIdentity =
            ExternalIdentityKey.FromGuidIdentifiers(
                "microsoft-entra",
                tenant,
                Guid.NewGuid().ToString("D"));
        var secondEntraIdentity =
            ExternalIdentityKey.FromGuidIdentifiers(
                "microsoft-entra",
                tenant,
                Guid.NewGuid().ToString("D"));

        try
        {
            var userId = await SeedUserAsync(identityKey);

            using (var firstScope = _factory.Services.CreateScope())
            {
                var repository = firstScope.ServiceProvider
                    .GetRequiredService<IExternalIdentityLinkRepository>();

                await repository.AttachAsync(
                    userId,
                    firstEntraIdentity);
            }

            using (var secondScope = _factory.Services.CreateScope())
            {
                var repository = secondScope.ServiceProvider
                    .GetRequiredService<IExternalIdentityLinkRepository>();

                var exception = await Assert.ThrowsAsync<
                    ExternalIdentityProviderAlreadyAssignedException>(
                        () => repository.AttachAsync(
                            userId,
                            secondEntraIdentity));

                Assert.Equal(userId, exception.UserId);
                Assert.Equal("microsoft-entra", exception.Provider);
                Assert.Equal(tenant, exception.Tenant);
            }

            using var verificationScope = _factory.Services.CreateScope();
            var userRepository = verificationScope.ServiceProvider
                .GetRequiredService<IAccessUserRepository>();

            var linkedUser = Assert.IsType<AccessUser>(
                await userRepository.FindByExternalIdentityAsync(
                    firstEntraIdentity,
                    CancellationToken.None));

            Assert.Equal(userId, linkedUser.Id);
            Assert.Null(
                await userRepository.FindByExternalIdentityAsync(
                    secondEntraIdentity,
                    CancellationToken.None));
        }
        finally
        {
            await DeleteIdentityAsync(identityKey);
        }
    }

    private async Task<Guid> SeedUserAsync(
        ExternalIdentityKey identityKey)
    {
        using var scope =
            _factory.Services.CreateScope();

        var repository =
            scope.ServiceProvider
                .GetRequiredService<
                    IAccessUserRepository>();

        var user =
            new AccessUser(
                Guid.NewGuid(),
                "Databázový uživatel",
                "database@example.cz",
                TestTime);

        user.GrantRole(
            Guid.NewGuid(),
            AccessRole.Customer,
            TestTime,
            RoleChangeActor.ForProcess(
                "first-login"));

        await repository.AddAsync(
            user,
            identityKey,
            CancellationToken.None);

        return user.Id;
    }

    private async Task DeleteIdentityAsync(
        ExternalIdentityKey identityKey)
    {
        using var scope =
            _factory.Services.CreateScope();

        var dbContext =
            scope.ServiceProvider
                .GetRequiredService<
                    FuaPayDbContext>();

        await using var transaction =
            await dbContext.Database
                .BeginTransactionAsync();

        var userIds =
            await dbContext.Database
                .SqlQuery<Guid>(
                    $"""
                    SELECT user_id AS "Value"
                    FROM access.external_identities
                    WHERE provider = {identityKey.Provider}
                      AND tenant = {identityKey.Tenant}
                      AND subject = {identityKey.Subject}
                    """)
                .ToArrayAsync();

        foreach (var userId in userIds.Distinct())
        {
            await dbContext.Database
                .ExecuteSqlInterpolatedAsync(
                    $"""
                    DELETE FROM audit.events
                    WHERE entity_type = 'access-user'
                      AND entity_id = {userId.ToString()}
                    """);

            await dbContext.Database
                .ExecuteSqlInterpolatedAsync(
                    $"""
                    DELETE FROM access.role_assignments
                    WHERE user_id = {userId}
                       OR granted_by_user_id = {userId}
                       OR revoked_by_user_id = {userId}
                    """);

            await dbContext.Database
                .ExecuteSqlInterpolatedAsync(
                    $"""
                    DELETE FROM access.external_identities
                    WHERE user_id = {userId}
                    """);
        }

        await dbContext.Database
            .ExecuteSqlInterpolatedAsync(
                $"""
                DELETE FROM access.external_identities
                WHERE provider = {identityKey.Provider}
                  AND tenant = {identityKey.Tenant}
                  AND subject = {identityKey.Subject}
                """);

        foreach (var userId in userIds.Distinct())
        {
            await dbContext.Database
                .ExecuteSqlInterpolatedAsync(
                    $"""
                    DELETE FROM access.users
                    WHERE id = {userId}
                    """);
        }

        await transaction.CommitAsync();
    }

    private async Task AssertProvisioningAuditCountAsync(
        Guid userId,
        int expectedCount)
    {
        using var scope = _factory.Services.CreateScope();
        var count = await scope.ServiceProvider
            .GetRequiredService<FuaPayDbContext>()
            .Database.SqlQuery<int>(
                $"""
                SELECT COUNT(*)::int AS "Value"
                FROM audit.events
                WHERE action = 'access.user-provisioned'
                  AND actor_process_name = 'first-login'
                  AND entity_type = 'access-user'
                  AND entity_id = {userId.ToString()}
                """)
            .SingleAsync();

        Assert.Equal(expectedCount, count);
    }

    private static ExternalIdentityKey CreateIdentityKey()
    {
        return new ExternalIdentityKey(
            "database-tests",
            "fuapay",
            Guid.NewGuid().ToString("N"));
    }

    private sealed class FixedTimeProvider :
        TimeProvider
    {
        private readonly DateTimeOffset _utcNow;

        internal FixedTimeProvider(
            DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public override DateTimeOffset GetUtcNow()
        {
            return _utcNow;
        }
    }

    private sealed class InitialLookupCoordinator
    {
        private readonly TaskCompletionSource
            _allParticipantsReady =
                new(
                    TaskCreationOptions
                        .RunContinuationsAsynchronously);

        private int _remainingParticipants;

        internal InitialLookupCoordinator(
            int participantCount)
        {
            if (participantCount <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(participantCount));
            }

            _remainingParticipants =
                participantCount;
        }

        internal async Task WaitAsync(
            CancellationToken cancellationToken)
        {
            if (
                Interlocked.Decrement(
                    ref _remainingParticipants) == 0)
            {
                _allParticipantsReady.TrySetResult();
            }

            await _allParticipantsReady
                .Task
                .WaitAsync(cancellationToken);
        }
    }

    private sealed class CoordinatedAccessUserRepository :
        IAccessUserRepository
    {
        private readonly IAccessUserRepository _inner;
        private readonly InitialLookupCoordinator
            _coordinator;

        private int _findCalls;

        internal CoordinatedAccessUserRepository(
            IAccessUserRepository inner,
            InitialLookupCoordinator coordinator)
        {
            ArgumentNullException.ThrowIfNull(inner);
            ArgumentNullException.ThrowIfNull(coordinator);

            _inner = inner;
            _coordinator = coordinator;
        }

        public Task<AccessUser?> FindByIdAsync(
            Guid userId,
            CancellationToken cancellationToken)
        {
            return _inner.FindByIdAsync(
                userId,
                cancellationToken);
        }

        public async Task<AccessUser?>
            FindByExternalIdentityAsync(
                ExternalIdentityKey identityKey,
                CancellationToken cancellationToken)
        {
            var user =
                await _inner
                    .FindByExternalIdentityAsync(
                        identityKey,
                        cancellationToken);

            if (
                Interlocked.Increment(
                    ref _findCalls) == 1)
            {
                if (user is not null)
                {
                    throw new InvalidOperationException(
                        "Externí identita již existovala před " +
                        "zahájením testu souběhu.");
                }

                await _coordinator.WaitAsync(
                    cancellationToken);
            }

            return user;
        }

        public Task AddAsync(
            AccessUser user,
            ExternalIdentityKey identityKey,
            CancellationToken cancellationToken)
        {
            return _inner.AddAsync(
                user,
                identityKey,
                cancellationToken);
        }

        public Task SaveAsync(
            AccessUser user,
            CancellationToken cancellationToken)
        {
            return _inner.SaveAsync(
                user,
                cancellationToken);
        }
    }

    private sealed class DuplicateAuditTrail : IAuditTrail
    {
        private readonly IAuditTrail _inner;
        private readonly AuditEntry _duplicate;

        public DuplicateAuditTrail(
            IAuditTrail inner,
            AuditEntry duplicate)
        {
            _inner = inner;
            _duplicate = duplicate;
        }

        public void Stage(AuditEntry entry) =>
            _inner.Stage(_duplicate);

        public Task WriteAsync(
            AuditEntry entry,
            CancellationToken cancellationToken = default) =>
            _inner.WriteAsync(_duplicate, cancellationToken);
    }
}
