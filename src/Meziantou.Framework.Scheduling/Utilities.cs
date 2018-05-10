using System;
using System.Globalization;

namespace Meziantou.Framework.Scheduling
{
    internal static class Utilities
    {
        public const string DateTimeFormat = "yyyyMMddTHHmmsszzz";
        public const string UtcDateTimeFormat = "yyyyMMddTHHmmssZ";

        public static DateTime ParseDateTime(string str)
        {
            string[] formats = {
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
                "yyyyMMdd"
                };

            var dateTime = DateTime.ParseExact(str, formats, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);
            return dateTime;
        }

        public static string DayOfWeekToString(DayOfWeek dayOfWeek)
        {
            switch (dayOfWeek)
            {
                case DayOfWeek.Sunday:
                    return "SU";
                case DayOfWeek.Monday:
                    return "MO";
                case DayOfWeek.Tuesday:
                    return "TU";
                case DayOfWeek.Wednesday:
                    return "WE";
                case DayOfWeek.Thursday:
                    return "TH";
                case DayOfWeek.Friday:
                    return "FR";
                case DayOfWeek.Saturday:
                    return "SA";
                default:
                    throw new ArgumentOutOfRangeException(nameof(dayOfWeek), dayOfWeek, null);
            }
        }

        public static string DateTimeToString(DateTime dt)
        {
            if (dt.Kind == DateTimeKind.Utc)
            {
                return dt.ToString(UtcDateTimeFormat);
            }

            return dt.ToString(DateTimeFormat);
        }

        public static string StatusToString(EventStatus status)
        {
            switch (status)
            {
                case EventStatus.Tentative:
                    return "TENTATIVE";
                case EventStatus.Confirmed:
                    return "CONFIRMED";
                case EventStatus.Cancelled:
                    return "CANCELLED";
                default:
                    throw new ArgumentOutOfRangeException(nameof(status), status, null);
            }
        }
    }
}
