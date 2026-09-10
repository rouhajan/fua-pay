using System.Globalization;

namespace FuaPay.Web.Pages.Admin.Audit;

internal static class AuditTimeDisplay
{
    private const string IanaTimeZoneId = "Europe/Prague";
    private const string WindowsTimeZoneId = "Central Europe Standard Time";

    private static readonly TimeZoneInfo PragueTimeZone =
        ResolvePragueTimeZone();

    public static string Format(DateTimeOffset occurredAt)
    {
        return ToPragueTime(occurredAt).ToString(
            "dd. MM. yyyy HH:mm:ss",
            CultureInfo.InvariantCulture);
    }

    internal static DateTimeOffset ToPragueTime(DateTimeOffset occurredAt)
    {
        return TimeZoneInfo.ConvertTime(occurredAt, PragueTimeZone);
    }

    private static TimeZoneInfo ResolvePragueTimeZone()
    {
        var timeZoneIds = OperatingSystem.IsWindows()
            ? new[] { WindowsTimeZoneId, IanaTimeZoneId }
            : new[] { IanaTimeZoneId, WindowsTimeZoneId };

        foreach (var timeZoneId in timeZoneIds)
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            }
            catch (TimeZoneNotFoundException)
            {
            }
            catch (InvalidTimeZoneException)
            {
            }
        }

        throw new TimeZoneNotFoundException(
            "Europe/Prague time zone is not available on this system.");
    }
}
