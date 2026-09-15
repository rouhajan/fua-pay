using FuaPay.Web.Modules.Credits.Application;

namespace FuaPay.Web.Modules.Credits.Infrastructure.PrintPayments;

internal sealed class PrintCredentialAttemptLimiter :
    IPrintCredentialAttemptLimiter
{
    internal const int SourceFailureLimit = 30;
    internal const int EmailFailureLimit = 6;
    internal const int MaximumSourceEntries = 1_024;
    internal const int MaximumEmailEntries = 4_096;
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    private readonly object _sync = new();
    private readonly Dictionary<string, Counter> _sourceCounters = [];
    private readonly Dictionary<string, Counter> _emailCounters = [];

    internal int EntryCount
    {
        get
        {
            lock (_sync)
            {
                return _sourceCounters.Count + _emailCounters.Count;
            }
        }
    }

    internal int SourceEntryCount
    {
        get
        {
            lock (_sync)
            {
                return _sourceCounters.Count;
            }
        }
    }

    internal int EmailEntryCount
    {
        get
        {
            lock (_sync)
            {
                return _emailCounters.Count;
            }
        }
    }

    public bool IsBlocked(
        Guid printSourceId,
        string? normalizedEmail,
        DateTimeOffset now)
    {
        lock (_sync)
        {
            RemoveExpired(now);

            return IsAtLimit(
                    _sourceCounters,
                    SourceKey(printSourceId),
                    SourceFailureLimit) ||
                normalizedEmail is not null &&
                IsAtLimit(
                    _emailCounters,
                    EmailKey(printSourceId, normalizedEmail),
                    EmailFailureLimit);
        }
    }

    public bool TryRecordFailure(
        Guid printSourceId,
        string? normalizedEmail,
        DateTimeOffset now)
    {
        lock (_sync)
        {
            RemoveExpired(now);

            var sourceKey = SourceKey(printSourceId);
            var emailKey = normalizedEmail is null
                ? null
                : EmailKey(printSourceId, normalizedEmail);

            if (
                IsAtLimit(
                    _sourceCounters,
                    sourceKey,
                    SourceFailureLimit) ||
                emailKey is not null &&
                IsAtLimit(
                    _emailCounters,
                    emailKey,
                    EmailFailureLimit))
            {
                return false;
            }

            if (
                !CanRecordNewKey(
                    _sourceCounters,
                    sourceKey,
                    MaximumSourceEntries) ||
                emailKey is not null &&
                !CanRecordNewKey(
                    _emailCounters,
                    emailKey,
                    MaximumEmailEntries))
            {
                return false;
            }

            Increment(
                _sourceCounters,
                sourceKey,
                now);
            if (emailKey is not null)
            {
                Increment(
                    _emailCounters,
                    emailKey,
                    now);
            }

            return true;
        }
    }

    private static bool IsAtLimit(
        IReadOnlyDictionary<string, Counter> counters,
        string key,
        int limit) =>
        counters.TryGetValue(key, out var counter) &&
        counter.Count >= limit;

    private static void Increment(
        Dictionary<string, Counter> counters,
        string key,
        DateTimeOffset now)
    {
        if (counters.TryGetValue(key, out var counter))
        {
            counter.Count++;
            return;
        }

        counters.Add(key, new Counter(1, now + Window));
    }

    private static bool CanRecordNewKey(
        IReadOnlyDictionary<string, Counter> counters,
        string key,
        int maximumEntries) =>
        counters.ContainsKey(key) || counters.Count < maximumEntries;

    private void RemoveExpired(DateTimeOffset now)
    {
        RemoveExpired(_sourceCounters, now);
        RemoveExpired(_emailCounters, now);
    }

    private static void RemoveExpired(
        Dictionary<string, Counter> counters,
        DateTimeOffset now)
    {
        foreach (var key in counters
                     .Where(pair => pair.Value.ExpiresAt <= now)
                     .Select(pair => pair.Key)
                     .ToArray())
        {
            counters.Remove(key);
        }
    }

    private static string SourceKey(Guid printSourceId) =>
        printSourceId.ToString("D");

    private static string EmailKey(
        Guid printSourceId,
        string normalizedEmail) =>
        $"{printSourceId:D}:{normalizedEmail}";

    private sealed class Counter(
        int count,
        DateTimeOffset expiresAt)
    {
        public int Count { get; set; } = count;

        public DateTimeOffset ExpiresAt { get; } = expiresAt;
    }
}
