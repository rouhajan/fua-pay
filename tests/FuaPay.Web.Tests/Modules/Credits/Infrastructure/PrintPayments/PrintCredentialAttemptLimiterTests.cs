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
    public void RandomIdentifiersCannotGrowStateBeyondHardBounds()
    {
        var limiter = new PrintCredentialAttemptLimiter();
        var now = new DateTimeOffset(2026, 9, 14, 8, 0, 0, TimeSpan.Zero);
        var attempts = PrintCredentialAttemptLimiter.MaximumEmailEntries + 100;

        for (var attempt = 0; attempt < attempts; attempt++)
        {
            Assert.True(limiter.TryRecordFailure(
                Guid.NewGuid(),
                $"random-{attempt}@tul.cz",
                now));
        }

        Assert.Equal(
            PrintCredentialAttemptLimiter.MaximumSourceEntries +
            PrintCredentialAttemptLimiter.MaximumEmailEntries,
            limiter.EntryCount);
    }
}
