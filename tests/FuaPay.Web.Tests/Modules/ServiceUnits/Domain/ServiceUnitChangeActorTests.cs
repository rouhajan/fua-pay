using FuaPay.Web.Modules.ServiceUnits.Domain;

namespace FuaPay.Web.Tests.Modules.ServiceUnits.Domain;

public sealed class ServiceUnitChangeActorTests
{
    [Fact]
    public void ForUser_CreatesUserActor()
    {
        var userId = Guid.NewGuid();

        var actor = ServiceUnitChangeActor.ForUser(userId);

        Assert.Equal(ServiceUnitChangeActorType.User, actor.Type);
        Assert.Equal(userId, actor.UserId);
        Assert.Null(actor.ProcessName);
    }

    [Fact]
    public void ForProcess_TrimsName()
    {
        var actor = ServiceUnitChangeActor.ForProcess("  import  ");

        Assert.Equal(ServiceUnitChangeActorType.Process, actor.Type);
        Assert.Equal("import", actor.ProcessName);
        Assert.Null(actor.UserId);
    }
}
