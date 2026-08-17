using FuaPay.Web.Modules.ServiceUnits.Domain;

namespace FuaPay.Web.Tests.Modules.ServiceUnits.Domain;

public sealed class RequesterServiceUnitAssignmentTests
{
    private static readonly DateTimeOffset GrantedAt =
        new(2026, 7, 29, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void NewAssignment_IsActiveAndAudited()
    {
        var actor = ServiceUnitChangeActor.ForProcess("test");

        var assignment = new RequesterServiceUnitAssignment(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            GrantedAt,
            actor);

        Assert.True(assignment.IsActive);
        Assert.Equal(actor, assignment.GrantedBy);
        Assert.Null(assignment.RevokedAt);
        Assert.Null(assignment.RevokedBy);
    }

    [Fact]
    public void Revoke_PreservesHistory()
    {
        var assignment = new RequesterServiceUnitAssignment(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            GrantedAt,
            ServiceUnitChangeActor.ForProcess("grant"));

        var revokedAt = GrantedAt.AddHours(1);
        var actor = ServiceUnitChangeActor.ForUser(Guid.NewGuid());

        assignment.Revoke(revokedAt, actor);

        Assert.False(assignment.IsActive);
        Assert.Equal(revokedAt, assignment.RevokedAt);
        Assert.Equal(actor, assignment.RevokedBy);
    }

    [Fact]
    public void Revoke_BeforeGrantIsRejected()
    {
        var assignment = new RequesterServiceUnitAssignment(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            GrantedAt,
            ServiceUnitChangeActor.ForProcess("grant"));

        Assert.Throws<ArgumentOutOfRangeException>(
            () =>
                assignment.Revoke(
                    GrantedAt.AddTicks(-1),
                    ServiceUnitChangeActor.ForProcess("revoke")));
    }
}
