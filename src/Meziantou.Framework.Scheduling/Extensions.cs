using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Globalization;

namespace Meziantou.Framework.Scheduling
{
    internal static class Extensions
    {
        [Pure]
        public static string GetValue(this IDictionary<string, string> dict, string key, string defaultValue)
        {
            if (dict == null) throw new ArgumentNullException(nameof(dict));

            if (dict.TryGetValue(key, out var value))
                return value;

            return defaultValue;
        }

        [Pure]
        public static int GetValue(this IDictionary<string, string> dict, string key, int defaultValue)
        {
            if (dict == null) throw new ArgumentNullException(nameof(dict));

            if (dict.TryGetValue(key, out var value) && value != null && int.TryParse(value, out var i))
                return i;

            return defaultValue;
        }

        [Pure]
        public static int? GetValue(this IDictionary<string, string> dict, string key, int? defaultValue)
        {
            if (dict == null) throw new ArgumentNullException(nameof(dict));

            if (dict.TryGetValue(key, out var value) && value != null && int.TryParse(value, out var i))
                return i;

            return defaultValue;
        }

        [Pure]
        public static Frequency GetValue(this IDictionary<string, string> dict, string key, Frequency defaultValue)
        {
            if (dict == null) throw new ArgumentNullException(nameof(dict));

            if (dict.TryGetValue(key, out var value) && value != null && Enum.TryParse<Frequency>(value, true, out var enumValue))
                return enumValue;

            return defaultValue;
        }

        [Pure]
        public static DateTime StartOfWeek(DateTime dt)
        {
            return StartOfWeek(dt, CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek);
        }

        [Pure]
        public static DateTime StartOfWeek(DateTime dt, DayOfWeek startOfWeek)
        {
            var diff = dt.DayOfWeek - startOfWeek;
            if (diff < 0)
            {
                diff += 7;
            }

            return dt.AddDays(-1 * diff);
        }

        [Pure]
        public static DateTime StartOfMonth(DateTime dt)
        {
            return StartOfMonth(dt, false);
        }

        [Pure]
        public static DateTime StartOfMonth(DateTime dt, bool keepTime)
        {
            if (keepTime)
            {
                return dt.AddDays(-dt.Day + 1);
            }

            return new DateTime(dt.Year, dt.Month, 1);
        }

        [Pure]
        public static DateTime StartOfYear(DateTime dt)
        {
            return StartOfYear(dt, false);
        }

        [Pure]
        public static DateTime StartOfYear(DateTime dt, bool keepTime)
        {
            if (keepTime)
            {
                return dt.AddDays(-dt.DayOfYear + 1);
            }
            else
            {
                return new DateTime(dt.Year, 1, 1);
            }
        }

        [Pure]
        public static string ToEnglishOrdinal(int num)
        {
            if (num <= 0) return num.ToString();

            switch (num % 100)
            {
                case 11:
                case 12:
                case 13:
                    return num + "th";
            }

            switch (num % 10)
            {
                case 1:
                    return num + "st";
                case 2:
                    return num + "nd";
                case 3:
                    return num + "rd";
                default:
                    return num + "th";
            }
        }

        [Pure]
        public static string ToFrenchOrdinal(int num)
        {
            if (num <= 0) return num.ToString();

            switch (num)
            {
                case 1:
                    return num + "er";
                default:
                    return num + "e";
            }
        }
    }
}
