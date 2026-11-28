using System.Globalization;

namespace FuaPay.Web.Modules.Reporting.Application;

internal sealed record AccountingPeriod(DateOnly Start, DateOnly End, DateTimeOffset From, DateTimeOffset Until)
{
    private static readonly TimeZoneInfo Prague =
        TimeZoneInfo.TryFindSystemTimeZoneById("Europe/Prague", out var zone)
        ? zone : TimeZoneInfo.FindSystemTimeZoneById("Central Europe Standard Time");

    public static string CurrentMonth(DateTimeOffset now) =>
        Local(now).ToString("yyyy-MM", CultureInfo.InvariantCulture);

    public static DateTime Local(DateTimeOffset instant) => TimeZoneInfo.ConvertTime(instant, Prague).DateTime;

    public static AccountingPeriod? Parse(string? month, string? from, string? to)
    {
        DateOnly start;
        DateOnly end;
        if (!string.IsNullOrEmpty(month))
        {
            if (month.Length != 7 || !string.IsNullOrEmpty(from) || !string.IsNullOrEmpty(to) ||
                !TryDate(month + "-01", out start))
            {
                return null;
            }

            end = new DateOnly(start.Year, start.Month, DateTime.DaysInMonth(start.Year, start.Month));
        }
        else if (!TryDate(from, out start) || !TryDate(to, out end))
        {
            return null;
        }

        // Excel's serial dates and the exclusive upper boundary must both be representable.
        if (start.Year < 1900 || end < start || end == DateOnly.MaxValue)
        {
            return null;
        }

        var first = Midnight(start);
        var afterLast = Midnight(end.AddDays(1));
        return first.HasValue && afterLast.HasValue ? new(start, end, first.Value, afterLast.Value) : null;
    }

    public bool IsClosedMonth(DateTimeOffset snapshot) =>
        snapshot >= Until && Start.Day == 1 &&
        End == new DateOnly(Start.Year, Start.Month, DateTime.DaysInMonth(Start.Year, Start.Month));

    private static bool TryDate(string? value, out DateOnly date) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

    private static DateTimeOffset? Midnight(DateOnly date)
    {
        var time = date.ToDateTime(TimeOnly.MinValue);
        return Prague.IsInvalidTime(time) || Prague.IsAmbiguousTime(time) ? null :
            new DateTimeOffset(time, Prague.GetUtcOffset(time)).ToUniversalTime();
    }
}
