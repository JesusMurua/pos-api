namespace POS.Domain.Helpers;

/// <summary>
/// Utility for resolving client timezone from IANA identifiers.
/// </summary>
public static class TimeZoneHelper
{
    public const string DefaultTimeZone = "America/Mexico_City";

    /// <summary>
    /// Returns today's date in the client's local timezone.
    /// </summary>
    public static DateOnly GetLocalToday(string? ianaTimeZone)
    {
        var tz = GetTimeZoneInfo(ianaTimeZone);
        return DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz));
    }

    /// <summary>
    /// Resolves IANA timezone to TimeZoneInfo. Falls back to Mexico City.
    /// </summary>
    public static TimeZoneInfo GetTimeZoneInfo(string? ianaTimeZone)
    {
        if (string.IsNullOrEmpty(ianaTimeZone))
            return TimeZoneInfo.FindSystemTimeZoneById(DefaultTimeZone);
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(ianaTimeZone);
        }
        catch
        {
            return TimeZoneInfo.FindSystemTimeZoneById(DefaultTimeZone);
        }
    }
}
