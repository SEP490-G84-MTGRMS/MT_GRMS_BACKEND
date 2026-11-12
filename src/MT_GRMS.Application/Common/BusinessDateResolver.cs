namespace MT_GRMS.Application.Common;

public static class BusinessDateResolver
{
    private static readonly TimeZoneInfo StoreTimeZone = ResolveStoreTimeZone();

    public static DateOnly Resolve(DateTime utcTime, TimeSpan businessDayStartTime)
    {
        var normalizedUtc = utcTime.Kind == DateTimeKind.Utc ? utcTime : DateTime.SpecifyKind(utcTime, DateTimeKind.Utc);
        var local = TimeZoneInfo.ConvertTimeFromUtc(normalizedUtc, StoreTimeZone);
        var date = local.TimeOfDay < businessDayStartTime ? local.Date.AddDays(-1) : local.Date;
        return DateOnly.FromDateTime(date);
    }

    private static TimeZoneInfo ResolveStoreTimeZone()
    {
        foreach (var id in new[] { "SE Asia Standard Time", "Asia/Ho_Chi_Minh" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }
        return TimeZoneInfo.CreateCustomTimeZone("UTC+07", TimeSpan.FromHours(7), "UTC+07", "UTC+07");
    }
}
