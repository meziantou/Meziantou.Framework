using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Globalization;

namespace Meziantou.Framework.Scheduling
{
    internal static class Extensions
    {
        [Pure]
        public static string? GetValue(this IDictionary<string, string> dict, string key, string? defaultValue)
        {
            if (dict == null)
                throw new ArgumentNullException(nameof(dict));

            if (dict.TryGetValue(key, out var value))
                return value;

            return defaultValue;
        }

        [Pure]
        public static int GetValue(this IDictionary<string, string> dict, string key, int defaultValue)
        {
            if (dict == null)
                throw new ArgumentNullException(nameof(dict));

            if (dict.TryGetValue(key, out var value) && value != null && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i))
                return i;

            return defaultValue;
        }

        [Pure]
        public static int? GetValue(this IDictionary<string, string> dict, string key, int? defaultValue)
        {
            if (dict == null)
                throw new ArgumentNullException(nameof(dict));

            if (dict.TryGetValue(key, out var value) && value != null && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i))
                return i;

            return defaultValue;
        }

        [Pure]
        public static Frequency GetValue(this IDictionary<string, string> dict, string key, Frequency defaultValue)
        {
            if (dict == null)
                throw new ArgumentNullException(nameof(dict));

            if (dict.TryGetValue(key, out var value) && value != null && Enum.TryParse<Frequency>(value, ignoreCase: true, out var enumValue))
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
            return StartOfMonth(dt, keepTime: false);
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
            return StartOfYear(dt, keepTime: false);
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
            if (num <= 0)
                return num.ToString(CultureInfo.CurrentCulture);

            return num.ToString(CultureInfo.CurrentCulture) + (num % 100) switch
            {
                11 or 12 or 13 => "th",
                _ => (num % 10) switch
                {
                    1 => "st",
                    2 => "nd",
                    3 => "rd",
                    _ => "th",
                },
            };
        }

        [Pure]
        public static string ToFrenchOrdinal(int num)
        {
            if (num <= 0)
                return num.ToString(CultureInfo.CurrentCulture);

            return num.ToString(CultureInfo.CurrentCulture) + num switch
            {
                1 => "er",
                _ => "e",
            };
        }
    }
}
