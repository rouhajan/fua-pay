using FuaPay.Web.BuildingBlocks.Auditing;

namespace FuaPay.Web.Tests.BuildingBlocks.Auditing;

public sealed class AuditEntryTests
{
    private static readonly DateTimeOffset OccurredAt =
        new(2026, 7, 29, 16, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ForUser_CreatesUserActorEntry()
    {
        var userId = Guid.NewGuid();

        var entry = AuditEntry.ForUser(
            userId,
            "job.updated",
            "job",
            Guid.NewGuid().ToString(),
            "Zakázka byla upravena.",
            OccurredAt);

        Assert.Equal(userId, entry.ActorUserId);
        Assert.Null(entry.ActorProcessName);
        Assert.Equal(OccurredAt, entry.OccurredAt);
    }

    [Fact]
    public void ForProcess_CreatesProcessActorEntry()
    {
        var entry = AuditEntry.ForProcess(
            "development-seeder",
            "seed.completed",
            "development-data",
            "default",
            "Vývojová data byla vytvořena.",
            OccurredAt);

        Assert.Null(entry.ActorUserId);
        Assert.Equal("development-seeder", entry.ActorProcessName);
    }

    [Fact]
    public void Constructor_RejectsTwoActors()
    {
        Assert.Throws<ArgumentException>(
            () => new AuditEntry(
                Guid.NewGuid(),
                OccurredAt,
                Guid.NewGuid(),
                "process",
                "action",
                "entity",
                "id",
                "description"));
    }

    [Fact]
    public void Constructor_RejectsNoActor()
    {
        Assert.Throws<ArgumentException>(
            () => new AuditEntry(
                Guid.NewGuid(),
                OccurredAt,
                null,
                null,
                "action",
                "entity",
                "id",
                "description"));
    }
}
