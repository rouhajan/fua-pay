using FuaPay.Web.Modules.Access.Domain;

namespace FuaPay.Web.Tests.Modules.Access.Domain;

public sealed class RoleChangeActorTests
{
    [Fact]
    public void ForUser_CreatesUserActor()
    {
        var userId = Guid.NewGuid();

        var actor =
            RoleChangeActor.ForUser(userId);

        Assert.Equal(
            RoleChangeActorType.User,
            actor.Type);

        Assert.Equal(userId, actor.UserId);
        Assert.Null(actor.ProcessName);
    }

    [Fact]
    public void ForUser_RejectsEmptyId()
    {
        Assert.Throws<ArgumentException>(
            () =>
                RoleChangeActor.ForUser(
                    Guid.Empty));
    }

    [Fact]
    public void ForProcess_TrimsProcessName()
    {
        var actor =
            RoleChangeActor.ForProcess(
                "  first-login  ");

        Assert.Equal(
            RoleChangeActorType.Process,
            actor.Type);

        Assert.Null(actor.UserId);

        Assert.Equal(
            "first-login",
            actor.ProcessName);
    }

    [Fact]
    public void ForProcess_RejectsOverlongName()
    {
        Assert.Throws<ArgumentException>(
            () =>
                RoleChangeActor.ForProcess(
                    new string('p', 129)));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void ForProcess_RejectsBlankName(
        string processName)
    {
        Assert.Throws<ArgumentException>(
            () =>
                RoleChangeActor.ForProcess(
                    processName));
    }
}
