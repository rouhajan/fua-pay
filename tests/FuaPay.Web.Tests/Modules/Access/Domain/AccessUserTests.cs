using FuaPay.Web.Modules.Access.Domain;

namespace FuaPay.Web.Tests.Modules.Access.Domain;

public sealed class AccessUserTests
{
    private static readonly DateTimeOffset CreatedAt =
        new(2026, 7, 26, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void NewUser_IsActiveAndHasNoAssignedRoles()
    {
        var user = CreateUser();

        Assert.Equal(
            AccessUserStatus.Active,
            user.Status);

        Assert.Empty(user.AssignedRoles);
        Assert.Empty(user.RoleAssignments);

        Assert.False(
            user.HasEffectiveRole(
                AccessRole.Customer));
    }

    [Fact]
    public void Constructor_TrimsProfileValues()
    {
        var user = new AccessUser(
            Guid.NewGuid(),
            "  Testovací uživatel  ",
            "  user@example.invalid  ",
            CreatedAt);

        Assert.Equal(
            "Testovací uživatel",
            user.DisplayName);

        Assert.Equal(
            "user@example.invalid",
            user.Email);

        Assert.Equal(
            CreatedAt,
            user.LastSeenAt);
    }

    [Fact]
    public void Constructor_AllowsMissingEmail()
    {
        var user = new AccessUser(
            Guid.NewGuid(),
            "Uživatel bez e-mailu",
            null,
            CreatedAt);

        Assert.Null(user.Email);
    }

    [Fact]
    public void Constructor_RejectsOverlongDisplayName()
    {
        Action action = () =>
            _ = new AccessUser(
                Guid.NewGuid(),
                new string('A', 257),
                null,
                CreatedAt);

        Assert.Throws<ArgumentException>(action);
    }

    [Fact]
    public void Constructor_RejectsOverlongEmail()
    {
        Action action = () =>
            _ = new AccessUser(
                Guid.NewGuid(),
                "Uživatel",
                new string('a', 321),
                CreatedAt);

        Assert.Throws<ArgumentException>(action);
    }

    [Fact]
    public void Constructor_RejectsEmptyUserId()
    {
        Action action = () =>
            _ = new AccessUser(
                Guid.Empty,
                "Uživatel",
                null,
                CreatedAt);

        Assert.Throws<ArgumentException>(action);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Constructor_RejectsBlankDisplayName(
        string displayName)
    {
        Action action = () =>
            _ = new AccessUser(
                Guid.NewGuid(),
                displayName,
                null,
                CreatedAt);

        Assert.Throws<ArgumentException>(action);
    }

    [Fact]
    public void GrantRole_BeforeUserCreationIsRejected()
    {
        var user = CreateUser();

        Action action = () =>
            user.GrantRole(
                Guid.NewGuid(),
                AccessRole.Customer,
                CreatedAt.AddTicks(-1),
                RoleChangeActor.ForProcess(
                    "invalid-import"));

        Assert.Throws<ArgumentOutOfRangeException>(
            action);

        Assert.Empty(user.AssignedRoles);
        Assert.Empty(user.RoleAssignments);
    }

    [Fact]
    public void GrantRole_AddsAuditedAssignment()
    {
        var user = CreateUser();
        var assignmentId = Guid.NewGuid();
        var administratorId = Guid.NewGuid();

        var actor =
            RoleChangeActor.ForUser(
                administratorId);

        var assignment = user.GrantRole(
            assignmentId,
            AccessRole.Requester,
            CreatedAt.AddMinutes(1),
            actor);

        Assert.True(
            user.HasEffectiveRole(
                AccessRole.Requester));

        Assert.Contains(
            AccessRole.Requester,
            user.AssignedRoles);

        Assert.Equal(
            assignmentId,
            assignment.Id);

        Assert.Equal(
            actor,
            assignment.GrantedBy);

        Assert.True(assignment.IsActive);
        Assert.Null(assignment.RevokedAt);
        Assert.Null(assignment.RevokedBy);
    }

    [Fact]
    public void GrantRole_WhenRoleIsAlreadyActive_Throws()
    {
        var user = CreateUser();

        user.GrantRole(
            Guid.NewGuid(),
            AccessRole.Customer,
            CreatedAt.AddMinutes(1),
            RoleChangeActor.ForProcess(
                "first-login"));

        var exception =
            Assert.Throws<DuplicateAccessRoleException>(
                () =>
                    user.GrantRole(
                        Guid.NewGuid(),
                        AccessRole.Customer,
                        CreatedAt.AddMinutes(2),
                        RoleChangeActor.ForProcess(
                            "duplicate")));

        Assert.Equal(user.Id, exception.UserId);

        Assert.Equal(
            AccessRole.Customer,
            exception.Role);

        Assert.Single(user.RoleAssignments);
    }

    [Fact]
    public void RevokeRole_PreservesHistoryAndRemovesEffectiveRole()
    {
        var user = CreateUser();

        var assignment =
            user.GrantRole(
                Guid.NewGuid(),
                AccessRole.Admin,
                CreatedAt.AddMinutes(1),
                RoleChangeActor.ForUser(
                    Guid.NewGuid()));

        var revokedAt =
            CreatedAt.AddMinutes(2);

        var revokedBy =
            RoleChangeActor.ForUser(
                Guid.NewGuid());

        var revokedAssignment =
            user.RevokeRole(
                AccessRole.Admin,
                revokedAt,
                revokedBy);

        Assert.Same(
            assignment,
            revokedAssignment);

        Assert.False(
            user.HasEffectiveRole(
                AccessRole.Admin));

        Assert.Empty(user.AssignedRoles);
        Assert.Single(user.RoleAssignments);
        Assert.False(assignment.IsActive);
        Assert.Equal(revokedAt, assignment.RevokedAt);
        Assert.Equal(revokedBy, assignment.RevokedBy);
    }

    [Fact]
    public void RevokedRole_CanBeGrantedAgainWithNewHistoryEntry()
    {
        var user = CreateUser();

        user.GrantRole(
            Guid.NewGuid(),
            AccessRole.Requester,
            CreatedAt.AddMinutes(1),
            RoleChangeActor.ForProcess(
                "initial-import"));

        user.RevokeRole(
            AccessRole.Requester,
            CreatedAt.AddMinutes(2),
            RoleChangeActor.ForUser(
                Guid.NewGuid()));

        user.GrantRole(
            Guid.NewGuid(),
            AccessRole.Requester,
            CreatedAt.AddMinutes(3),
            RoleChangeActor.ForUser(
                Guid.NewGuid()));

        Assert.True(
            user.HasEffectiveRole(
                AccessRole.Requester));

        Assert.Equal(
            2,
            user.RoleAssignments.Count);

        Assert.False(
            user.RoleAssignments[0].IsActive);

        Assert.True(
            user.RoleAssignments[1].IsActive);
    }

    [Fact]
    public void RevokedRole_CanBeGrantedAgainAtSameTimestamp()
    {
        var user = CreateUser();
        var transitionTime =
            CreatedAt.AddMinutes(10);

        user.GrantRole(
            Guid.NewGuid(),
            AccessRole.Requester,
            CreatedAt.AddMinutes(1),
            RoleChangeActor.ForProcess(
                "initial-import"));

        user.RevokeRole(
            AccessRole.Requester,
            transitionTime,
            RoleChangeActor.ForUser(
                Guid.NewGuid()));

        user.GrantRole(
            Guid.NewGuid(),
            AccessRole.Requester,
            transitionTime,
            RoleChangeActor.ForUser(
                Guid.NewGuid()));

        Assert.True(
            user.HasEffectiveRole(
                AccessRole.Requester));

        Assert.Equal(
            2,
            user.RoleAssignments.Count);
    }

    [Fact]
    public void GrantRole_WhenPreviousAssignmentWasRevokedLater_Throws()
    {
        var user = CreateUser();

        user.GrantRole(
            Guid.NewGuid(),
            AccessRole.Requester,
            CreatedAt.AddMinutes(10),
            RoleChangeActor.ForProcess(
                "initial-import"));

        user.RevokeRole(
            AccessRole.Requester,
            CreatedAt.AddMinutes(20),
            RoleChangeActor.ForUser(
                Guid.NewGuid()));

        Action action = () =>
            user.GrantRole(
                Guid.NewGuid(),
                AccessRole.Requester,
                CreatedAt.AddMinutes(15),
                RoleChangeActor.ForUser(
                    Guid.NewGuid()));

        Assert.Throws<ArgumentOutOfRangeException>(
            action);

        Assert.Single(user.RoleAssignments);
        Assert.Empty(user.AssignedRoles);
    }

    [Fact]
    public void RevokeRole_WhenRoleIsNotAssigned_Throws()
    {
        var user = CreateUser();

        var exception =
            Assert.Throws<AccessRoleNotAssignedException>(
                () =>
                    user.RevokeRole(
                        AccessRole.Admin,
                        CreatedAt.AddMinutes(1),
                        RoleChangeActor.ForUser(
                            Guid.NewGuid())));

        Assert.Equal(user.Id, exception.UserId);

        Assert.Equal(
            AccessRole.Admin,
            exception.Role);
    }

    [Fact]
    public void Block_PreservesRoleHistoryButRemovesEffectiveRoles()
    {
        var user = CreateUser();

        user.GrantRole(
            Guid.NewGuid(),
            AccessRole.Customer,
            CreatedAt.AddMinutes(1),
            RoleChangeActor.ForProcess(
                "first-login"));

        user.Block();

        Assert.Equal(
            AccessUserStatus.Blocked,
            user.Status);

        Assert.Contains(
            AccessRole.Customer,
            user.AssignedRoles);

        Assert.False(
            user.HasEffectiveRole(
                AccessRole.Customer));

        Assert.Single(user.RoleAssignments);

        user.Activate();

        Assert.Equal(
            AccessUserStatus.Active,
            user.Status);

        Assert.True(
            user.HasEffectiveRole(
                AccessRole.Customer));
    }

    [Fact]
    public void SynchronizeProfile_UpdatesCurrentAttributes()
    {
        var user = CreateUser();
        var observedAt = CreatedAt.AddHours(1);

        user.SynchronizeProfile(
            "  Aktualizované jméno  ",
            "  new@example.cz  ",
            observedAt);

        Assert.Equal(
            "Aktualizované jméno",
            user.DisplayName);

        Assert.Equal(
            "new@example.cz",
            user.Email);

        Assert.Equal(
            observedAt,
            user.LastSeenAt);
    }

    [Fact]
    public void SynchronizeProfile_RejectsOlderObservation()
    {
        var user = CreateUser();

        user.SynchronizeProfile(
            "Novější jméno",
            null,
            CreatedAt.AddHours(1));

        Action action = () =>
            user.SynchronizeProfile(
                "Zastaralé jméno",
                null,
                CreatedAt.AddMinutes(30));

        Assert.Throws<
            ArgumentOutOfRangeException>(
            action);

        Assert.Equal(
            "Novější jméno",
            user.DisplayName);
    }

    [Fact]
    public void AccessRole_DoesNotContainUnknown()
    {
        Assert.DoesNotContain(
            "Unknown",
            Enum.GetNames<AccessRole>());
    }

    private static AccessUser CreateUser()
    {
        return new AccessUser(
            Guid.NewGuid(),
            "Testovací uživatel",
            "user@example.cz",
            CreatedAt);
    }
}
