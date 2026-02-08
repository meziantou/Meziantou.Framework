namespace Meziantou.Framework.Scheduling;

internal static class Utilities
{
    public const string DateTimeFormat = "yyyyMMddTHHmmsszzz";
    public const string UtcDateTimeFormat = "yyyyMMddTHHmmssZ";

    public static DateTime ParseDateTime(string str)
    {
        string[] formats =
        [
            // Basic formats
            "yyyyMMddTHHmmsszzz",
            "yyyyMMddTHHmmsszz",
            "yyyyMMddTHHmmssZ",
            // Extended formats
            "yyyy-MM-ddTHH:mm:sszzz",
            "yyyy-MM-ddTHH:mm:sszz",
            "yyyy-MM-ddTHH:mm:ssZ",
            // All of the above with reduced accuracy
            "yyyyMMddTHHmmzzz",
            "yyyyMMddTHHmmzz",
            "yyyyMMddTHHmmZ",
            "yyyy-MM-ddTHH:mmzzz",
            "yyyy-MM-ddTHH:mmzz",
            "yyyy-MM-ddTHH:mmZ",
            // Accuracy reduced to hours
            "yyyyMMddTHHzzz",
            "yyyyMMddTHHzz",
            "yyyyMMddTHHZ",
            "yyyy-MM-ddTHHzzz",
            "yyyy-MM-ddTHHzz",
            "yyyy-MM-ddTHHZ",
            // Accuracy reduced to date
            "yyyyMMdd",
        ];

        var dateTime = DateTime.ParseExact(str, formats, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);
        return dateTime;
    }

    public static string DayOfWeekToString(DayOfWeek dayOfWeek)
    {
        return dayOfWeek switch
        {
            DayOfWeek.Sunday => "SU",
            DayOfWeek.Monday => "MO",
            DayOfWeek.Tuesday => "TU",
            DayOfWeek.Wednesday => "WE",
            DayOfWeek.Thursday => "TH",
            DayOfWeek.Friday => "FR",
            DayOfWeek.Saturday => "SA",
            _ => throw new ArgumentOutOfRangeException(nameof(dayOfWeek), dayOfWeek, message: null),
        };
    }

    public static string DateTimeToString(DateTime dt)
    {
        if (dt.Kind is DateTimeKind.Utc)
        {
            return dt.ToString(UtcDateTimeFormat, CultureInfo.InvariantCulture);
        }

        return dt.ToString(DateTimeFormat, CultureInfo.InvariantCulture);
    }

    public static string StatusToString(EventStatus status)
    {
        return status switch
        {
            EventStatus.Tentative => "TENTATIVE",
            EventStatus.Confirmed => "CONFIRMED",
            EventStatus.Cancelled => "CANCELLED",
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, message: null),
        };
    }
}
