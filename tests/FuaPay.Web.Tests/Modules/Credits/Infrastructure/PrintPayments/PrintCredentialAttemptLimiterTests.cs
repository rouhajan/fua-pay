using FuaPay.Web.Modules.Credits.Infrastructure.PrintPayments;

namespace FuaPay.Web.Tests.Modules.Credits.Infrastructure.PrintPayments;

public sealed class PrintCredentialAttemptLimiterTests
{
    [Fact]
    public void FailedAttemptsReachEmailBoundaryAndExpire()
    {
        var limiter = new PrintCredentialAttemptLimiter();
        var sourceId = Guid.NewGuid();
        var now = new DateTimeOffset(2026, 9, 14, 8, 0, 0, TimeSpan.Zero);

        for (var attempt = 0; attempt < PrintCredentialAttemptLimiter.EmailFailureLimit; attempt++)
        {
            Assert.False(limiter.IsBlocked(
                sourceId,
                "student@tul.cz",
                now));
            Assert.True(limiter.TryRecordFailure(
                sourceId,
                "student@tul.cz",
                now));
        }

        Assert.True(limiter.IsBlocked(
            sourceId,
            "student@tul.cz",
            now));
        Assert.False(limiter.IsBlocked(
            sourceId,
            "student@tul.cz",
            now.AddMinutes(1)));
        Assert.Equal(0, limiter.EntryCount);
    }

    [Fact]
    public void SuccessfulChecksDoNotConsumeFailureBudget()
    {
        var limiter = new PrintCredentialAttemptLimiter();
        var sourceId = Guid.NewGuid();
        var now = new DateTimeOffset(2026, 9, 14, 8, 0, 0, TimeSpan.Zero);

        for (var attempt = 0; attempt < 100; attempt++)
        {
            Assert.False(limiter.IsBlocked(
                sourceId,
                "student@tul.cz",
                now));
        }

        Assert.Equal(0, limiter.EntryCount);
    }

    [Fact]
    public void FailedAttemptsReachSourceWideBoundaryAcrossEmails()
    {
        var limiter = new PrintCredentialAttemptLimiter();
        var sourceId = Guid.NewGuid();
        var now = new DateTimeOffset(2026, 9, 14, 8, 0, 0, TimeSpan.Zero);

        for (var attempt = 0; attempt < PrintCredentialAttemptLimiter.SourceFailureLimit; attempt++)
        {
            Assert.True(limiter.TryRecordFailure(
                sourceId,
                $"student-{attempt}@tul.cz",
                now));
        }

        Assert.True(limiter.IsBlocked(
            sourceId,
            "another@tul.cz",
            now));
    }

    [Fact]
    public void EmailSaturationCannotEvictExistingBlockedCounter()
    {
        var limiter = new PrintCredentialAttemptLimiter();
        var now = new DateTimeOffset(2026, 9, 14, 8, 0, 0, TimeSpan.Zero);
        var blocked = FillEmailCapacity(limiter, now);

        Assert.False(limiter.TryRecordFailure(
            blocked.SourceId,
            "capacity-overflow@tul.cz",
            now));
        Assert.True(limiter.TryRecordFailure(
            blocked.SourceId,
            "target-0@tul.cz",
            now));
        Assert.True(limiter.IsBlocked(
            blocked.SourceId,
            blocked.Email,
            now));
    }

    [Fact]
    public void SourceSaturationCannotEvictExistingBlockedCounter()
    {
        var limiter = new PrintCredentialAttemptLimiter();
        var now = new DateTimeOffset(2026, 9, 14, 8, 0, 0, TimeSpan.Zero);
        var blockedSourceId = Guid.NewGuid();

        for (var attempt = 0;
             attempt < PrintCredentialAttemptLimiter.SourceFailureLimit;
             attempt++)
        {
            Assert.True(limiter.TryRecordFailure(
                blockedSourceId,
                null,
                now));
        }

        for (var source = 1;
             source < PrintCredentialAttemptLimiter.MaximumSourceEntries;
             source++)
        {
            Assert.True(limiter.TryRecordFailure(
                Guid.NewGuid(),
                null,
                now));
        }

        Assert.False(limiter.TryRecordFailure(
            Guid.NewGuid(),
            null,
            now));
        Assert.True(limiter.IsBlocked(
            blockedSourceId,
            "unrelated@tul.cz",
            now));
    }

    [Fact]
    public void ExpiredEntriesAreReclaimedAtCapacity()
    {
        var limiter = new PrintCredentialAttemptLimiter();
        var now = new DateTimeOffset(2026, 9, 14, 8, 0, 0, TimeSpan.Zero);

        for (var source = 0;
             source < PrintCredentialAttemptLimiter.MaximumSourceEntries;
             source++)
        {
            Assert.True(limiter.TryRecordFailure(
                Guid.NewGuid(),
                null,
                now));
        }

        Assert.True(limiter.TryRecordFailure(
            Guid.NewGuid(),
            "after-expiry@tul.cz",
            now.AddMinutes(1)));
        Assert.Equal(1, limiter.SourceEntryCount);
        Assert.Equal(1, limiter.EmailEntryCount);
    }

    [Fact]
    public void SaturatedStateNeverExceedsHardBounds()
    {
        var limiter = new PrintCredentialAttemptLimiter();
        var now = new DateTimeOffset(2026, 9, 14, 8, 0, 0, TimeSpan.Zero);
        _ = FillEmailCapacity(limiter, now);

        for (var attempt = 0; attempt < 100; attempt++)
        {
            Assert.False(limiter.TryRecordFailure(
                Guid.NewGuid(),
                $"overflow-{attempt}@tul.cz",
                now));
        }

        Assert.Equal(
            PrintCredentialAttemptLimiter.MaximumSourceEntries,
            limiter.SourceEntryCount);
        Assert.Equal(
            PrintCredentialAttemptLimiter.MaximumEmailEntries,
            limiter.EmailEntryCount);
        Assert.Equal(
            PrintCredentialAttemptLimiter.MaximumSourceEntries +
            PrintCredentialAttemptLimiter.MaximumEmailEntries,
            limiter.EntryCount);
    }

    private static (Guid SourceId, string Email) FillEmailCapacity(
        PrintCredentialAttemptLimiter limiter,
        DateTimeOffset now)
    {
        var blockedSourceId = Guid.NewGuid();
        const string blockedEmail = "blocked@tul.cz";

        for (var attempt = 0;
             attempt < PrintCredentialAttemptLimiter.EmailFailureLimit;
             attempt++)
        {
            Assert.True(limiter.TryRecordFailure(
                blockedSourceId,
                blockedEmail,
                now));
        }

        for (var email = 0; email < 3; email++)
        {
            Assert.True(limiter.TryRecordFailure(
                blockedSourceId,
                $"target-{email}@tul.cz",
                now));
        }

        for (var source = 1;
             source < PrintCredentialAttemptLimiter.MaximumSourceEntries;
             source++)
        {
            var sourceId = Guid.NewGuid();
            for (var email = 0; email < 4; email++)
            {
                Assert.True(limiter.TryRecordFailure(
                    sourceId,
                    $"source-{source}-email-{email}@tul.cz",
                    now));
            }
        }

        return (blockedSourceId, blockedEmail);
    }
}
