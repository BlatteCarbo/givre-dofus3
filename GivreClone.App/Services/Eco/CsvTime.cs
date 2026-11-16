using System.Globalization;

namespace GivreClone.App.Services.Eco;

public static class CsvTime
{
    public static string Local(long unixMs, TimeZoneInfo zone)
        => TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeMilliseconds(unixMs), zone).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    public static string Utc(long unixMs)
        => DateTimeOffset.FromUnixTimeMilliseconds(unixMs).UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
}
