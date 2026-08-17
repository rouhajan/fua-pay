using System.Threading;

namespace FuaPay.DatabaseTests;

public static class TestJobData
{
    private static readonly string Prefix =
        "DB" + Guid.NewGuid()
            .ToString("N")[..6]
            .ToUpperInvariant();

    private static int _sequence;

    public static string NextJobNumber()
    {
        var value = Interlocked.Increment(ref _sequence);
        return $"{Prefix}-2026-{value:D6}";
    }
}
