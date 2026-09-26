// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using System;
using System.Globalization;
using Tomlyn.Model;

namespace Tomlyn.Helpers
{
    internal static class DateTimeRFC3339
    {
        // https://www.ietf.org/rfc/rfc3339.txt

        //date-fullyear   = 4DIGIT
        //date-month      = 2DIGIT  ; 01-12
        //date-mday       = 2DIGIT  ; 01-28, 01-29, 01-30, 01-31 based on
        //                          ; month/year
        //time-hour       = 2DIGIT  ; 00-23
        //time-minute     = 2DIGIT  ; 00-59
        //time-second     = 2DIGIT  ; 00-58, 00-59, 00-60 based on leap second
        //                          ; rules
        //time-secfrac    = "." 1*DIGIT
        //time-numoffset  = ("+" / "-") time-hour ":" time-minute
        //time-offset     = "Z" / time-numoffset

        //partial-time    = time-hour ":" time-minute ":" time-second
        //                  [time-secfrac]
        //full-date       = date-fullyear "-" date-month "-" date-mday
        //full-time       = partial-time time-offset

        //date-time       = full-date "T" full-time
        private static readonly string[] OffsetDateTimeFormatsByZ = new[]
        {
            "yyyy-MM-ddTHH:mm:ssZ",            // With Z postfix
            "yyyy-MM-ddTHH:mmZ",
            "yyyy-MM-ddTHH:mm:ss.fZ",
            "yyyy-MM-ddTHH:mm:ss.ffZ",
            "yyyy-MM-ddTHH:mm:ss.fffZ",
            "yyyy-MM-ddTHH:mm:ss.ffffZ",
            "yyyy-MM-ddTHH:mm:ss.fffffZ",
            "yyyy-MM-ddTHH:mm:ss.ffffffZ",
            "yyyy-MM-ddTHH:mm:ss.fffffffZ",

            // Specs says that T might be omitted
            "yyyy-MM-dd HH:mm:ssZ",            // With Z postfix
            "yyyy-MM-dd HH:mmZ",
            "yyyy-MM-dd HH:mm:ss.fZ",
            "yyyy-MM-dd HH:mm:ss.ffZ",
            "yyyy-MM-dd HH:mm:ss.fffZ",
            "yyyy-MM-dd HH:mm:ss.ffffZ",
            "yyyy-MM-dd HH:mm:ss.fffffZ",
            "yyyy-MM-dd HH:mm:ss.ffffffZ",
            "yyyy-MM-dd HH:mm:ss.fffffffZ",
        };

        private static readonly string[] OffsetDateTimeFormatsByNumber = new[]
        {
            "yyyy-MM-ddTHH:mm:sszzz",          // With time-numoffset
            "yyyy-MM-ddTHH:mmzzz",
            "yyyy-MM-ddTHH:mm:ss.fzzz",
            "yyyy-MM-ddTHH:mm:ss.ffzzz",
            "yyyy-MM-ddTHH:mm:ss.fffzzz",
            "yyyy-MM-ddTHH:mm:ss.ffffzzz",
            "yyyy-MM-ddTHH:mm:ss.fffffzzz",
            "yyyy-MM-ddTHH:mm:ss.ffffffzzz",
            "yyyy-MM-ddTHH:mm:ss.fffffffzzz",

            "yyyy-MM-dd HH:mm:sszzz",          // With time-numoffset
            "yyyy-MM-dd HH:mmzzz",
            "yyyy-MM-dd HH:mm:ss.fzzz",
            "yyyy-MM-dd HH:mm:ss.ffzzz",
            "yyyy-MM-dd HH:mm:ss.fffzzz",
            "yyyy-MM-dd HH:mm:ss.ffffzzz",
            "yyyy-MM-dd HH:mm:ss.fffffzzz",
            "yyyy-MM-dd HH:mm:ss.ffffffzzz",
            "yyyy-MM-dd HH:mm:ss.fffffffzzz",
        };


        private static readonly string[] LocalDateTimeFormats = new[]
        {
            "yyyy-MM-ddTHH:mm:ss",
            "yyyy-MM-ddTHH:mm",
            "yyyy-MM-ddTHH:mm:ss.f",
            "yyyy-MM-ddTHH:mm:ss.ff",
            "yyyy-MM-ddTHH:mm:ss.fff",
            "yyyy-MM-ddTHH:mm:ss.ffff",
            "yyyy-MM-ddTHH:mm:ss.fffff",
            "yyyy-MM-ddTHH:mm:ss.ffffff",
            "yyyy-MM-ddTHH:mm:ss.fffffff",

            // Specs says that T might be omitted
            "yyyy-MM-dd HH:mm:ss",
            "yyyy-MM-dd HH:mm",
            "yyyy-MM-dd HH:mm:ss.f",
            "yyyy-MM-dd HH:mm:ss.ff",
            "yyyy-MM-dd HH:mm:ss.fff",
            "yyyy-MM-dd HH:mm:ss.ffff",
            "yyyy-MM-dd HH:mm:ss.fffff",
            "yyyy-MM-dd HH:mm:ss.ffffff",
            "yyyy-MM-dd HH:mm:ss.fffffff",
        };

        // Local Time
        private static readonly string[] LocalTimeFormats = new[]
        {
            "HH:mm:ss",
            "HH:mm",
            "HH:mm:ss.f",
            "HH:mm:ss.ff",
            "HH:mm:ss.fff",
            "HH:mm:ss.ffff",
            "HH:mm:ss.fffff",
            "HH:mm:ss.ffffff",
            "HH:mm:ss.fffffff",
        };

        public static bool TryParseOffsetDateTime(string str, out TomlDateTime time)
        {
            var upper = str.ToUpperInvariant();

            // Enforce RFC 3339/TOML offset format: "Z" or ±HH:MM.
            // DateTimeOffset parsing is permissive and may accept invalid forms like "+0900"/"+0909".
            if (!upper.EndsWith("Z", StringComparison.Ordinal))
            {
                if (upper.Length < 6)
                {
                    time = default;
                    return false;
                }

                var signIndex = upper.Length - 6;
                var sign = upper[signIndex];
                if (sign != '+' && sign != '-')
                {
                    time = default;
                    return false;
                }

                if (upper[signIndex + 3] != ':' ||
                    !char.IsDigit(upper[signIndex + 1]) ||
                    !char.IsDigit(upper[signIndex + 2]) ||
                    !char.IsDigit(upper[signIndex + 4]) ||
                    !char.IsDigit(upper[signIndex + 5]))
                {
                    time = default;
                    return false;
                }
            }

            if (!TryParseExactWithPrecision(upper, OffsetDateTimeFormatsByZ,
                    TryParseDateTimeOffset, DateTimeStyles.None, TomlDateTimeKind.OffsetDateTimeByZ, out time))
            {
                return TryParseExactWithPrecision(upper, OffsetDateTimeFormatsByNumber,
                    TryParseDateTimeOffset, DateTimeStyles.None, TomlDateTimeKind.OffsetDateTimeByNumber, out time);
            }

            return true;
        }

        public static bool TryParseLocalDateTime(string str, out TomlDateTime time)
        {
            return TryParseExactWithPrecision(str.ToUpperInvariant(), LocalDateTimeFormats, TryParseDateTime, DateTimeStyles.None, TomlDateTimeKind.LocalDateTime,  out time);
        }

        public static bool TryParseLocalDate(string str, out TomlDateTime time)
        {
            if (DateTime.TryParseExact(str.ToUpperInvariant(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var rawtime))
            {
                var unspecified = DateTime.SpecifyKind(rawtime, DateTimeKind.Unspecified);
                time = new TomlDateTime(new DateTimeOffset(unspecified, TimeSpan.Zero), 0, TomlDateTimeKind.LocalDate);
                return true;
            }

            time = default;
            return false;
        }

        public static bool TryParseLocalTime(string str, out TomlDateTime time)
        {
            return TryParseExactWithPrecision(str.ToUpperInvariant(), LocalTimeFormats, TryParseDateTime, DateTimeStyles.None, TomlDateTimeKind.LocalTime, out time);
        }

        private static readonly ParseDelegate TryParseDateTime = (string text, string format, CultureInfo culture, DateTimeStyles style, out DateTimeOffset time) =>
        {
            time = default;
            if (DateTime.TryParseExact(text, format, culture, style, out var rawTime))
            {
                // Local TOML date/time values do not carry a timezone. Avoid applying the machine
                // local timezone offset which can overflow for extreme values (e.g. year 0001).
                var unspecified = DateTime.SpecifyKind(rawTime, DateTimeKind.Unspecified);
                time = new DateTimeOffset(unspecified, TimeSpan.Zero);
                return true;
            }

            return false;
        };
        private static readonly ParseDelegate TryParseDateTimeOffset = DateTimeOffset.TryParseExact;

        private delegate bool ParseDelegate(string text, string format, CultureInfo culture, DateTimeStyles style, out DateTimeOffset time);

        private static bool TryParseExactWithPrecision(string str, string[] formats, ParseDelegate parser, DateTimeStyles style, TomlDateTimeKind kind, out TomlDateTime time)
        {
            time = default;
            for (int i = 0; i < formats.Length; i++)
            {
                var format = formats[i];
                if (parser(str, format, CultureInfo.InvariantCulture, style, out var rawTime))
                {
                    // Since .NET 11, "HH" accepts 24 (ISO 8601 end of day), which RFC 3339 and TOML do not allow.
                    // The formats have a fixed width before the hour, so the hour is at the same index in the text.
                    var hourIndex = format.IndexOf("HH", StringComparison.Ordinal);
                    if (hourIndex >= 0 && str.AsSpan(hourIndex, 2) is "24")
                    {
                        continue;
                    }

                    var precision = 0;
                    var dotIndex = format.IndexOf('.', StringComparison.Ordinal);
                    if (dotIndex >= 0)
                    {
                        for (var j = dotIndex + 1; j < format.Length && format[j] == 'f'; j++)
                        {
                            precision++;
                        }
                    }

                    time = new TomlDateTime(rawTime, precision, kind);
                    return true;
                }
            }

            return false;
        }
    }
}
