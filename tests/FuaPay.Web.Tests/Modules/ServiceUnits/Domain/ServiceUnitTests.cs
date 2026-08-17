using FuaPay.Web.Modules.Jobs.Domain;
using FuaPay.Web.Modules.ServiceUnits.Domain;

namespace FuaPay.Web.Tests.Modules.ServiceUnits.Domain;

public sealed class ServiceUnitTests
{
    private static readonly DateTimeOffset CreatedAt =
        new(2026, 7, 29, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Constructor_NormalizesCodeAndName()
    {
        var unit = new ServiceUnit(
            Guid.NewGuid(),
            "  3d  ",
            "  3D tisk  ",
            ServiceType.ThreeDPrint,
            CreatedAt,
            ServiceUnitChangeActor.ForProcess("test"));

        Assert.Equal("3D", unit.Code);
        Assert.Equal("3D tisk", unit.DisplayName);
        Assert.True(unit.IsActive);
    }

    [Theory]
    [InlineData("")]
    [InlineData("A")]
    [InlineData("TOO-LONG")]
    [InlineData("3D tisk")]
    [InlineData("PLOT!")]
    public void Constructor_RejectsInvalidCode(string code)
    {
        Assert.Throws<ArgumentException>(
            () =>
                new ServiceUnit(
                    Guid.NewGuid(),
                    code,
                    "Pracoviště",
                    ServiceType.Workshop,
                    CreatedAt,
                    ServiceUnitChangeActor.ForProcess("test")));
    }

    [Fact]
    public void Constructor_RejectsUnknownServiceType()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () =>
                new ServiceUnit(
                    Guid.NewGuid(),
                    "OST",
                    "Ostatní",
                    ServiceType.Unknown,
                    CreatedAt,
                    ServiceUnitChangeActor.ForProcess("test")));
    }

    [Fact]
    public void Deactivate_PreservesAuditData()
    {
        var unit = CreateUnit();
        var actor = ServiceUnitChangeActor.ForUser(Guid.NewGuid());
        var deactivatedAt = CreatedAt.AddDays(1);

        unit.Deactivate(deactivatedAt, actor);

        Assert.False(unit.IsActive);
        Assert.Equal(ServiceUnitStatus.Inactive, unit.Status);
        Assert.Equal(deactivatedAt, unit.DeactivatedAt);
        Assert.Equal(actor, unit.DeactivatedBy);
    }

    [Fact]
    public void InactiveUnit_CannotBeUpdated()
    {
        var unit = CreateUnit();

        unit.Deactivate(
            CreatedAt.AddDays(1),
            ServiceUnitChangeActor.ForProcess("test"));

        Assert.Throws<InactiveServiceUnitException>(
            () =>
                unit.UpdateDetails(
                    "Nový název",
                    ServiceType.Workshop));
    }

    private static ServiceUnit CreateUnit()
    {
        return new ServiceUnit(
            Guid.NewGuid(),
            "PLT",
            "Velkoformátový tisk",
            ServiceType.LargeFormatPrint,
            CreatedAt,
            ServiceUnitChangeActor.ForProcess("test"));
    }
}
