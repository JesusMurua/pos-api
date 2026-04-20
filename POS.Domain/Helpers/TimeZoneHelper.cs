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

    /// <summary>
    /// Returns the half-open UTC range <c>[startUtc, endUtc)</c> that covers the
    /// local calendar day <paramref name="localDate"/> in <paramref name="ianaTimeZone"/>.
    /// Both outputs carry <see cref="DateTimeKind.Utc"/> so they can be passed safely
    /// to Npgsql-backed queries against <c>timestamptz</c> columns.
    /// </summary>
    /// <remarks>
    /// Anchors at local midnight and adds 24 wall-clock hours for the end. This
    /// means DST-transition days naturally yield a 23-hour span (spring-forward)
    /// or a 25-hour span (fall-back); midnight is neither invalid nor ambiguous
    /// in IANA zones of interest, so no DST resolution policy is required.
    /// </remarks>
    public static (DateTime startUtc, DateTime endUtc) GetUtcRangeForLocalDate(
        DateOnly localDate,
        string ianaTimeZone)
    {
        var tz = GetTimeZoneInfo(ianaTimeZone);

        var localStart = new DateTime(
            localDate.Year, localDate.Month, localDate.Day,
            0, 0, 0, DateTimeKind.Unspecified);
        var localEnd = localStart.AddDays(1);

        var startUtc = TimeZoneInfo.ConvertTimeToUtc(localStart, tz);
        var endUtc = TimeZoneInfo.ConvertTimeToUtc(localEnd, tz);

        if (endUtc <= startUtc)
            throw new InvalidOperationException(
                $"UTC range collapsed for {localDate:yyyy-MM-dd} in {ianaTimeZone}.");

        return (startUtc, endUtc);
    }
}
