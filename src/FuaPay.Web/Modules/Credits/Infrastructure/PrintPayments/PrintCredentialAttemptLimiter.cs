using System.Collections.Concurrent;

using FuaPay.Web.Modules.Credits.Application;

namespace FuaPay.Web.Modules.Credits.Infrastructure.PrintPayments;

internal sealed class PrintCredentialAttemptLimiter :
    IPrintCredentialAttemptLimiter
{
    internal const int SourcePermitLimit = 30;
    internal const int EmailPermitLimit = 6;
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    private readonly ConcurrentDictionary<string, Counter> _counters = [];

    public bool TryAcquire(
        Guid printSourceId,
        string sourceAddress,
        string normalizedEmail,
        DateTimeOffset now)
    {
        var sourceKey = $"source:{printSourceId:D}:{sourceAddress}";
        var emailKey = $"email:{printSourceId:D}:{normalizedEmail}";

        return Acquire(sourceKey, SourcePermitLimit, now) &&
            Acquire(emailKey, EmailPermitLimit, now);
    }

    private bool Acquire(string key, int limit, DateTimeOffset now)
    {
        var counter = _counters.GetOrAdd(key, _ => new Counter(now));

        lock (counter)
        {
            if (now - counter.WindowStartedAt >= Window)
            {
                counter.WindowStartedAt = now;
                counter.Count = 0;
            }

            if (counter.Count >= limit)
            {
                return false;
            }

            counter.Count++;
            return true;
        }
    }

    private sealed class Counter(DateTimeOffset windowStartedAt)
    {
        public DateTimeOffset WindowStartedAt { get; set; } = windowStartedAt;

        public int Count { get; set; }
    }
}
