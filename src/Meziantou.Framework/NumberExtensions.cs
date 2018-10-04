using System;
using System.Globalization;
using System.Diagnostics.Contracts;

namespace Meziantou.Framework
{
    public static class NumberExtensions
    {
        [Pure]
        public static decimal MakeSameSignAs(this decimal number, decimal sign)
        {
            return Math.Abs(number) * Math.Sign(sign);
        }

        [Pure]
        public static int MakeSameSignAs(this int number, int sign)
        {
            return Math.Abs(number) * Math.Sign(sign);
        }

        [Pure]
        public static long MakeSameSignAs(this long number, long sign)
        {
            return Math.Abs(number) * Math.Sign(sign);
        }

        [Pure]
        public static float MakeSameSignAs(this float number, float sign)
        {
            return Math.Abs(number) * Math.Sign(sign);
        }

        [Pure]
        public static double MakeSameSignAs(this double number, double sign)
        {
            return Math.Abs(number) * Math.Sign(sign);
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

        [Pure]
        public static string ToStringInvariant(this byte number)
        {
            return ToStringInvariant(number, null);
        }

        [Pure]
        public static string ToStringInvariant(this byte number, string format)
        {
            if (format != null)
                return number.ToString(format, CultureInfo.InvariantCulture);

            return number.ToString(CultureInfo.InvariantCulture);
        }

        [Pure]
        public static string ToStringInvariant(this sbyte number)
        {
            return ToStringInvariant(number, null);
        }

        [Pure]
        public static string ToStringInvariant(this sbyte number, string format)
        {
            if (format != null)
                return number.ToString(format, CultureInfo.InvariantCulture);

            return number.ToString(CultureInfo.InvariantCulture);
        }

        [Pure]
        public static string ToStringInvariant(this short number)
        {
            return ToStringInvariant(number, null);
        }

        [Pure]
        public static string ToStringInvariant(this short number, string format)
        {
            if (format != null)
                return number.ToString(format, CultureInfo.InvariantCulture);

            return number.ToString(CultureInfo.InvariantCulture);
        }

        [Pure]
        public static string ToStringInvariant(this ushort number)
        {
            return ToStringInvariant(number, null);
        }

        [Pure]
        public static string ToStringInvariant(this ushort number, string format)
        {
            if (format != null)
                return number.ToString(format, CultureInfo.InvariantCulture);

            return number.ToString(CultureInfo.InvariantCulture);
        }

        [Pure]
        public static string ToStringInvariant(this int number)
        {
            return ToStringInvariant(number, null);
        }

        [Pure]
        public static string ToStringInvariant(this int number, string format)
        {
            if (format != null)
                return number.ToString(format, CultureInfo.InvariantCulture);

            return number.ToString(CultureInfo.InvariantCulture);
        }

        [Pure]
        public static string ToStringInvariant(this uint number)
        {
            return ToStringInvariant(number, null);
        }

        [Pure]
        public static string ToStringInvariant(this uint number, string format)
        {
            if (format != null)
                return number.ToString(format, CultureInfo.InvariantCulture);

            return number.ToString(CultureInfo.InvariantCulture);
        }

        [Pure]
        public static string ToStringInvariant(this long number)
        {
            return ToStringInvariant(number, null);
        }

        [Pure]
        public static string ToStringInvariant(this long number, string format)
        {
            if (format != null)
                return number.ToString(format, CultureInfo.InvariantCulture);

            return number.ToString(CultureInfo.InvariantCulture);
        }

        [Pure]
        public static string ToStringInvariant(this ulong number)
        {
            return ToStringInvariant(number, null);
        }

        [Pure]
        public static string ToStringInvariant(this ulong number, string format)
        {
            if (format != null)
                return number.ToString(format, CultureInfo.InvariantCulture);

            return number.ToString(CultureInfo.InvariantCulture);
        }

        [Pure]
        public static string ToStringInvariant(this double number)
        {
            return ToStringInvariant(number, null);
        }

        [Pure]
        public static string ToStringInvariant(this double number, string format)
        {
            if (format != null)
                return number.ToString(format, CultureInfo.InvariantCulture);

            return number.ToString(CultureInfo.InvariantCulture);
        }

        [Pure]
        public static string ToStringInvariant(this float number)
        {
            return ToStringInvariant(number, null);
        }

        [Pure]
        public static string ToStringInvariant(this float number, string format)
        {
            if (format != null)
                return number.ToString(format, CultureInfo.InvariantCulture);

            return number.ToString(CultureInfo.InvariantCulture);
        }

        [Pure]
        public static string ToStringInvariant(this decimal number)
        {
            return ToStringInvariant(number, null);
        }

        [Pure]
        public static string ToStringInvariant(this decimal number, string format)
        {
            if (format != null)
                return number.ToString(format, CultureInfo.InvariantCulture);

            return number.ToString(CultureInfo.InvariantCulture);
        }
    }
}
