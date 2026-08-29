namespace Meziantou.Framework.Scheduling;

internal static class Utilities
{
    public const string FloatingDateTimeFormat = "yyyyMMddTHHmmss";
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

    /// <summary>Formats a date-time using one of the forms defined in RFC 5545 section 3.3.5.</summary>
    public static string DateTimeToString(DateTime dt)
    {
        return dt.Kind switch
        {
            // Form 2: a date-time in UTC.
            DateTimeKind.Utc => dt.ToString(UtcDateTimeFormat, CultureInfo.InvariantCulture),

            // A local time is only meaningful to a reader that also knows the offset, and the
            // output carries no TZID parameter to convey it, so it is written as UTC.
            DateTimeKind.Local => dt.ToUniversalTime().ToString(UtcDateTimeFormat, CultureInfo.InvariantCulture),

            // Form 1: a floating date-time, interpreted in the reader's own time zone.
            _ => dt.ToString(FloatingDateTimeFormat, CultureInfo.InvariantCulture),
        };
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
