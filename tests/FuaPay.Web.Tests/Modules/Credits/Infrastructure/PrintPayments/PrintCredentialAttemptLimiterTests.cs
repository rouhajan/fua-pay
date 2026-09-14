using FuaPay.Web.Modules.Credits.Infrastructure.PrintPayments;

namespace FuaPay.Web.Tests.Modules.Credits.Infrastructure.PrintPayments;

public sealed class PrintCredentialAttemptLimiterTests
{
    [Fact]
    public void EmailBoundaryLimitsGuessesAndResetsAfterWindow()
    {
        var limiter = new PrintCredentialAttemptLimiter();
        var sourceId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        for (var attempt = 0; attempt < PrintCredentialAttemptLimiter.EmailPermitLimit; attempt++)
        {
            Assert.True(limiter.TryAcquire(sourceId, "127.0.0.1", "student@tul.cz", now));
        }

        Assert.False(limiter.TryAcquire(sourceId, "127.0.0.1", "student@tul.cz", now));
        Assert.True(limiter.TryAcquire(
            sourceId,
            "127.0.0.1",
            "student@tul.cz",
            now.AddMinutes(1)));
    }
}
