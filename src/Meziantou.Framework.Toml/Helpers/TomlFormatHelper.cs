using System;
using System.Globalization;
using Meziantou.Framework.Toml.Model;
using Meziantou.Framework.Toml.Text;

namespace Meziantou.Framework.Toml.Helpers;

/// <summary>
/// Helper methods to format values into TOML-compliant strings.
/// </summary>
public static class TomlFormatHelper
{
    /// <summary>
    /// Converts a boolean to its TOML representation.
    /// </summary>
    /// <param name="b">The boolean value.</param>
    /// <returns>The TOML string.</returns>
    public static string ToString(bool b) => b ? "true" : "false";

    /// <summary>
    /// Converts a string to its TOML representation using the provided display kind.
    /// </summary>
    /// <param name="s">The string value.</param>
    /// <param name="displayKind">The display kind.</param>
    /// <returns>The TOML string.</returns>
    public static string ToString(string s, TomlPropertyDisplayKind displayKind)
    {
        switch (displayKind)
        {
            case TomlPropertyDisplayKind.StringMulti:
                return $"\"\"\"{s.EscapeForToml(true)}\"\"\"";
            case TomlPropertyDisplayKind.StringLiteral:
                if (IsSafeStringLiteral(s))
                {
                    return $"'{s}'";
                }
                else
                {
                    goto default;
                }
            case TomlPropertyDisplayKind.StringLiteralMulti:
                if (IsSafeStringLiteralMulti(s))
                {
                    return $"'''{s}'''";
                }
                else
                {
                    goto default;
                }
            default:
                return $"\"{s.EscapeForToml()}\"";
        }
    }

    /// <summary>
    /// Converts a 32-bit integer to its TOML representation.
    /// </summary>
    /// <param name="i32">The integer value.</param>
    /// <param name="displayKind">The display kind.</param>
    /// <returns>The TOML string.</returns>
    public static string ToString(int i32, TomlPropertyDisplayKind displayKind)
    {
        return displayKind switch
        {
            TomlPropertyDisplayKind.IntegerHexadecimal => $"0x{i32:x8}",
            TomlPropertyDisplayKind.IntegerOctal => $"0o{Convert.ToString(i32, 8)}",
            TomlPropertyDisplayKind.IntegerBinary => $"0b{Convert.ToString(i32, 2)}",
            _ => (i32.ToString(CultureInfo.InvariantCulture))
        };
    }

    /// <summary>
    /// Converts a 64-bit integer to its TOML representation.
    /// </summary>
    /// <param name="i64">The integer value.</param>
    /// <param name="displayKind">The display kind.</param>
    /// <returns>The TOML string.</returns>
    public static string ToString(long i64, TomlPropertyDisplayKind displayKind)
    {
        return displayKind switch
        {
            TomlPropertyDisplayKind.IntegerHexadecimal => $"0x{i64:x16}",
            TomlPropertyDisplayKind.IntegerOctal => $"0o{Convert.ToString(i64, 8)}",
            TomlPropertyDisplayKind.IntegerBinary => $"0b{Convert.ToString(i64, 2)}",
            _ => i64.ToString(CultureInfo.InvariantCulture)
        };
    }

    /// <summary>
    /// Converts an unsigned 32-bit integer to its TOML representation.
    /// </summary>
    /// <param name="u32">The integer value.</param>
    /// <param name="displayKind">The display kind.</param>
    /// <returns>The TOML string.</returns>
    public static string ToString(uint u32, TomlPropertyDisplayKind displayKind)
    {
        return displayKind switch
        {
            TomlPropertyDisplayKind.IntegerHexadecimal => $"0x{u32:x8}",
            TomlPropertyDisplayKind.IntegerOctal => $"0o{Convert.ToString(u32, 8)}",
            TomlPropertyDisplayKind.IntegerBinary => $"0b{Convert.ToString(u32, 2)}",
            _ => u32.ToString(CultureInfo.InvariantCulture)
        };
    }

    /// <summary>
    /// Converts an unsigned 64-bit integer to its TOML representation.
    /// </summary>
    /// <param name="u64">The integer value.</param>
    /// <param name="displayKind">The display kind.</param>
    /// <returns>The TOML string.</returns>
    public static string ToString(ulong u64, TomlPropertyDisplayKind displayKind)
    {
        return ToString(unchecked((long) u64), displayKind);
    }

    /// <summary>
    /// Converts an 8-bit signed integer to its TOML representation.
    /// </summary>
    /// <param name="i8">The integer value.</param>
    /// <param name="displayKind">The display kind.</param>
    /// <returns>The TOML string.</returns>
    public static string ToString(sbyte i8, TomlPropertyDisplayKind displayKind)
    {
        return displayKind switch
        {
            TomlPropertyDisplayKind.IntegerHexadecimal => $"0x{i8:x2}",
            TomlPropertyDisplayKind.IntegerOctal => $"0o{Convert.ToString(i8, 8)}",
            TomlPropertyDisplayKind.IntegerBinary => $"0b{Convert.ToString(i8, 2)}",
            _ => i8.ToString(CultureInfo.InvariantCulture)
        };
    }

    /// <summary>
    /// Converts an 8-bit unsigned integer to its TOML representation.
    /// </summary>
    /// <param name="u8">The integer value.</param>
    /// <param name="displayKind">The display kind.</param>
    /// <returns>The TOML string.</returns>
    public static string ToString(byte u8, TomlPropertyDisplayKind displayKind)
    {
        return displayKind switch
        {
            TomlPropertyDisplayKind.IntegerHexadecimal => $"0x{u8:x2}",
            TomlPropertyDisplayKind.IntegerOctal => $"0o{Convert.ToString(u8, 8)}",
            TomlPropertyDisplayKind.IntegerBinary => $"0b{Convert.ToString(u8, 2)}",
            _ => u8.ToString(CultureInfo.InvariantCulture)
        };
    }
    /// <summary>
    /// Converts a 16-bit signed integer to its TOML representation.
    /// </summary>
    /// <param name="i16">The integer value.</param>
    /// <param name="displayKind">The display kind.</param>
    /// <returns>The TOML string.</returns>
    public static string ToString(short i16, TomlPropertyDisplayKind displayKind)
    {
        return displayKind switch
        {
            TomlPropertyDisplayKind.IntegerHexadecimal => $"0x{i16:x4}",
            TomlPropertyDisplayKind.IntegerOctal => $"0o{Convert.ToString(i16, 8)}",
            TomlPropertyDisplayKind.IntegerBinary => $"0b{Convert.ToString(i16, 2)}",
            _ => i16.ToString(CultureInfo.InvariantCulture)
        };
    }

    /// <summary>
    /// Converts a 16-bit unsigned integer to its TOML representation.
    /// </summary>
    /// <param name="u16">The integer value.</param>
    /// <param name="displayKind">The display kind.</param>
    /// <returns>The TOML string.</returns>
    public static string ToString(ushort u16, TomlPropertyDisplayKind displayKind)
    {
        return displayKind switch
        {
            TomlPropertyDisplayKind.IntegerHexadecimal => $"0x{u16:x4}",
            TomlPropertyDisplayKind.IntegerOctal => $"0o{Convert.ToString(u16, 8)}",
            TomlPropertyDisplayKind.IntegerBinary => $"0b{Convert.ToString(u16, 2)}",
            _ => u16.ToString(CultureInfo.InvariantCulture)
        };
    }

    /// <summary>
    /// Converts a single-precision floating point value to its TOML representation.
    /// </summary>
    /// <param name="value">The float value.</param>
    /// <returns>The TOML string.</returns>
    public static string ToString(float value)
    {
        if (float.IsNaN(value))
        {
            return "nan";
        }
        if (float.IsPositiveInfinity(value))
        {
            return "+inf";
        }
        if (float.IsNegativeInfinity(value))
        {
            return "-inf";
        }
        return AppendDecimalPoint(value.ToString("g9", CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Converts a double-precision floating point value to its TOML representation.
    /// </summary>
    /// <param name="value">The double value.</param>
    /// <returns>The TOML string.</returns>
    public static string ToString(double value)
    {
        if (double.IsNaN(value))
        {
            return "nan";
        }
        if (double.IsPositiveInfinity(value))
        {
            return "+inf";
        }
        if (double.IsNegativeInfinity(value))
        {
            return "-inf";
        }
        // Use a round-trippable format to avoid precision loss when serializing and reading back.
        return AppendDecimalPoint(value.ToString("R", CultureInfo.InvariantCulture));
    }


    /// <summary>
    /// Converts a <see cref="TomlDateTime"/> to its TOML representation.
    /// </summary>
    /// <param name="tomlDateTime">The value to format.</param>
    /// <returns>The TOML string.</returns>
    public static string ToString(TomlDateTime tomlDateTime) => tomlDateTime.ToString();


    /// <summary>
    /// Converts a <see cref="DateTime"/> to its TOML representation.
    /// </summary>
    /// <param name="dateTime">The date/time value.</param>
    /// <param name="displayKind">The display kind.</param>
    /// <returns>The TOML string.</returns>
    public static string ToString(DateTime dateTime, TomlPropertyDisplayKind displayKind) => ToTomlDateTime(dateTime, displayKind).ToString();

    /// <summary>
    /// Converts a <see cref="DateTimeOffset"/> to its TOML representation.
    /// </summary>
    /// <param name="dateTimeOffset">The date/time value.</param>
    /// <param name="displayKind">The display kind.</param>
    /// <returns>The TOML string.</returns>
    public static string ToString(DateTimeOffset dateTimeOffset, TomlPropertyDisplayKind displayKind) => ToTomlDateTime(dateTimeOffset, displayKind).ToString();

    /// <summary>
    /// Converts a <see cref="DateOnly"/> to its TOML representation.
    /// </summary>
    /// <param name="dateOnly">The date value.</param>
    /// <param name="displayKind">The display kind.</param>
    /// <returns>The TOML string.</returns>
    public static string ToString(DateOnly dateOnly, TomlPropertyDisplayKind displayKind) => ToTomlDateTime(dateOnly, displayKind).ToString();

    /// <summary>
    /// Converts a <see cref="TimeOnly"/> to its TOML representation.
    /// </summary>
    /// <param name="timeOnly">The time value.</param>
    /// <param name="displayKind">The display kind.</param>
    /// <returns>The TOML string.</returns>
    public static string ToString(TimeOnly timeOnly, TomlPropertyDisplayKind displayKind) => ToTomlDateTime(timeOnly, displayKind).ToString();

    // A UTC DateTime is an offset date-time with Z, a local DateTime keeps the offset of the machine, and an unspecified
    // DateTime is a local date-time. Local kinds keep the wall-clock value and never depend on the machine time zone.
    internal static TomlDateTime ToTomlDateTime(DateTime value, TomlPropertyDisplayKind displayKind = TomlPropertyDisplayKind.Default)
    {
        var kind = displayKind == TomlPropertyDisplayKind.Default
            ? value.Kind switch
            {
                DateTimeKind.Utc => TomlDateTimeKind.OffsetDateTimeByZ,
                DateTimeKind.Local => TomlDateTimeKind.OffsetDateTimeByNumber,
                _ => TomlDateTimeKind.LocalDateTime,
            }
            : GetDateTimeDisplayKind(displayKind);

        var dateTimeOffset = value.Kind == DateTimeKind.Local && kind is TomlDateTimeKind.OffsetDateTimeByZ or TomlDateTimeKind.OffsetDateTimeByNumber
            ? new DateTimeOffset(value)
            : new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Unspecified), TimeSpan.Zero);
        return new TomlDateTime(dateTimeOffset, GetSecondPrecision(value.Ticks), kind);
    }

    internal static TomlDateTime ToTomlDateTime(DateTimeOffset value, TomlPropertyDisplayKind displayKind = TomlPropertyDisplayKind.Default)
    {
        var kind = displayKind == TomlPropertyDisplayKind.Default
            ? value.Offset == TimeSpan.Zero ? TomlDateTimeKind.OffsetDateTimeByZ : TomlDateTimeKind.OffsetDateTimeByNumber
            : GetDateTimeDisplayKind(displayKind);

        var dateTimeOffset = kind is TomlDateTimeKind.OffsetDateTimeByZ or TomlDateTimeKind.OffsetDateTimeByNumber
            ? value
            : new DateTimeOffset(value.DateTime, TimeSpan.Zero);
        return new TomlDateTime(dateTimeOffset, GetSecondPrecision(value.Ticks), kind);
    }

    internal static TomlDateTime ToTomlDateTime(DateOnly value, TomlPropertyDisplayKind displayKind = TomlPropertyDisplayKind.Default)
    {
        var kind = displayKind == TomlPropertyDisplayKind.Default ? TomlDateTimeKind.LocalDate : GetDateTimeDisplayKind(displayKind);
        return new TomlDateTime(new DateTimeOffset(value.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero), 0, kind);
    }

    internal static TomlDateTime ToTomlDateTime(TimeOnly value, TomlPropertyDisplayKind displayKind = TomlPropertyDisplayKind.Default)
    {
        var kind = displayKind == TomlPropertyDisplayKind.Default ? TomlDateTimeKind.LocalTime : GetDateTimeDisplayKind(displayKind);
        return new TomlDateTime(new DateTimeOffset(DateOnly.MinValue.ToDateTime(value), TimeSpan.Zero), GetSecondPrecision(value.Ticks), kind);
    }

    // The number of significant fractional-second digits, up to 7 (ticks)
    internal static int GetSecondPrecision(long ticks)
    {
        var fraction = ticks % TimeSpan.TicksPerSecond;
        if (fraction == 0)
        {
            return 0;
        }

        var precision = 7;
        while (fraction % 10 == 0)
        {
            fraction /= 10;
            precision--;
        }

        return precision;
    }

    private static string AppendDecimalPoint(string text)
    {
        if (text == "0") return "0.0";
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            // Do not append a decimal point if floating point type value
            // - is in exponential form, or
            // - already has a decimal point
            if (c == 'e' || c == 'E' || c == '.')
            {
                return text;
            }
        }
        return text + ".0";
    }

    private static bool IsSafeStringLiteral(string text)
    {
        foreach (var c in text)
        {
            if (char.IsControl(c) || c == '\'') return false;
        }
        return true;
    }

    private static bool IsSafeStringLiteralMulti(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            // new line are permitted
            if ((c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') || c == '\n')
            {
                continue;
            }
            if (char.IsControl(c)) return false;

            // Sequence of 3 or more ' are not permitted
            if (c == '\'')
            {
                if (i + 1 < text.Length && text[i + 1] == '\'' &&
                    i + 2 < text.Length && text[i + 2] == '\'')
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static TomlDateTimeKind GetDateTimeDisplayKind(TomlPropertyDisplayKind kind)
    {
        switch (kind)
        {
            default:
            case TomlPropertyDisplayKind.OffsetDateTimeByZ:
                return TomlDateTimeKind.OffsetDateTimeByZ;
            case TomlPropertyDisplayKind.OffsetDateTimeByNumber:
                return TomlDateTimeKind.OffsetDateTimeByNumber;
            case TomlPropertyDisplayKind.LocalDateTime:
                return TomlDateTimeKind.LocalDateTime;
            case TomlPropertyDisplayKind.LocalDate:
                return TomlDateTimeKind.LocalDate;
            case TomlPropertyDisplayKind.LocalTime:
                return TomlDateTimeKind.LocalTime;
        }
    }

}
