using System.Globalization;

namespace FuaPay.Web.Modules.FinancialDocuments.Application;

internal sealed record FinancialDocumentArchivePeriod(
    DateOnly From, DateOnly To, DateTimeOffset FromUtc, DateTimeOffset ToUtcExclusive)
{
    public string FileName => FormattableString.Invariant(
        $"doklady-o-uhrade-{From:yyyy-MM-dd}--{To:yyyy-MM-dd}.zip");

    public static FinancialDocumentArchivePeriod? Parse(
        string? month, string? from, string? to)
    {
        DateOnly first;
        DateOnly last;
        if (!string.IsNullOrEmpty(month))
        {
            if (!string.IsNullOrEmpty(from) || !string.IsNullOrEmpty(to) ||
                !TryParse($"{month}-01", out first))
            {
                return null;
            }

            last = new DateOnly(first.Year, first.Month,
                DateTime.DaysInMonth(first.Year, first.Month));
        }
        else if (!TryParse(from, out first) || !TryParse(to, out last))
        {
            return null;
        }

        var days = last.DayNumber - first.DayNumber + 1;
        if (days is < 1 or > 31 || last == DateOnly.MaxValue)
        {
            return null;
        }

        if (!TimeZoneInfo.TryFindSystemTimeZoneById("Europe/Prague", out var zone) &&
            !TimeZoneInfo.TryFindSystemTimeZoneById("Central Europe Standard Time", out zone))
        {
            throw new TimeZoneNotFoundException("Europe/Prague is unavailable.");
        }

        var start = GetMidnightUtc(first, zone);
        var end = GetMidnightUtc(last.AddDays(1), zone);
        return start.HasValue && end.HasValue
            ? new FinancialDocumentArchivePeriod(first, last, start.Value, end.Value)
            : null;
    }

    private static bool TryParse(string? value, out DateOnly date) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out date);

    private static DateTimeOffset? GetMidnightUtc(DateOnly day, TimeZoneInfo zone)
    {
        var midnight = day.ToDateTime(TimeOnly.MinValue);
        if (zone.IsAmbiguousTime(midnight) || zone.IsInvalidTime(midnight))
        {
            return null;
        }

        try
        {
            // DateTimeOffset rejects out-of-range UTC instants instead of clamping them.
            return new DateTimeOffset(midnight, zone.GetUtcOffset(midnight)).ToUniversalTime();
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}
