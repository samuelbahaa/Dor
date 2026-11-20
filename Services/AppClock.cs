namespace Dor.Api.Services;

// "Today" for a queue means today in Egypt, not in UTC.
// (Otherwise tickets would restart at 2 AM instead of midnight.)
public static class AppClock
{
    private static readonly TimeZoneInfo Zone = FindZone();

    private static TimeZoneInfo FindZone()
    {
        foreach (var id in new[] { "Africa/Cairo", "Egypt Standard Time" })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (TimeZoneNotFoundException)
            {
            }
            catch (InvalidTimeZoneException)
            {
            }
        }

        return TimeZoneInfo.CreateCustomTimeZone("EG", TimeSpan.FromHours(2), "Egypt", "Egypt");
    }

    public static DateOnly Today()
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Zone);
        return DateOnly.FromDateTime(local);
    }
}
