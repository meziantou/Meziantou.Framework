using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Meziantou.Framework.Toml.Helpers;

namespace Meziantou.Framework.Toml;
using System;
using System.Runtime.InteropServices;

/// <summary>
/// A datetime value that can represent a TOML
/// - An offset DateTime with Zero offset or Number offset.
/// - A local DateTime.
/// - A local Date.
/// - A local Time.
/// </summary>
/// <param name="DateTime">The datetime offset.</param>
/// <param name="SecondPrecision">The precision of milliseconds.</param>
/// <param name="Kind">The kind of datetime offset.</param>
[StructLayout(LayoutKind.Auto)]
public record struct TomlDateTime(DateTimeOffset DateTime, int SecondPrecision, TomlDateTimeKind Kind) : IConvertible
{
    /// <summary>
    /// Initializes a local-date TOML value from a date component.
    /// </summary>
    /// <param name="year">The year component.</param>
    /// <param name="month">The month component.</param>
    /// <param name="day">The day component.</param>
    public TomlDateTime(int year, int month, int day) : this(new DateTimeOffset(System.DateTime.SpecifyKind(new System.DateTime(year, month, day), DateTimeKind.Unspecified), TimeSpan.Zero), 0, TomlDateTimeKind.LocalDate)
    {
    }

    /// <summary>
    /// Initializes a local-date-time TOML value from a <see cref="DateTime"/>.
    /// </summary>
    /// <param name="datetime">The date/time value. Its wall-clock value is used, whatever its <see cref="DateTime.Kind"/>, and its fractional seconds are kept.</param>
    public TomlDateTime(DateTime datetime) : this(new DateTimeOffset(System.DateTime.SpecifyKind(datetime, DateTimeKind.Unspecified), TimeSpan.Zero), TomlFormatHelper.GetSecondPrecision(datetime.Ticks), TomlDateTimeKind.LocalDateTime)
    {
    }

    /// <inheritdoc />
    public override string ToString() => ((IConvertible)this).ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Indicates whether two values are the same TOML value: the same <see cref="Kind"/> and the same <see cref="DateTime"/>.
    /// </summary>
    /// <remarks>
    /// <see cref="SecondPrecision"/> only affects how the value is written, so <c>07:32:00.5</c> equals <c>07:32:00.50</c>.
    /// </remarks>
    public readonly bool Equals(TomlDateTime other) => Kind == other.Kind && DateTime.Equals(other.DateTime);

    /// <inheritdoc />
    public override readonly int GetHashCode() => HashCode.Combine(Kind, DateTime);

    [ExcludeFromCodeCoverage]
    TypeCode IConvertible.GetTypeCode()
    {
        return TypeCode.Object;
    }

    [ExcludeFromCodeCoverage]
    bool IConvertible.ToBoolean(IFormatProvider? provider)
    {
        throw new NotSupportedException();
    }

    [ExcludeFromCodeCoverage]
    byte IConvertible.ToByte(IFormatProvider? provider)
    {
        throw new NotSupportedException();
    }

    [ExcludeFromCodeCoverage]
    char IConvertible.ToChar(IFormatProvider? provider)
    {
        throw new NotSupportedException();
    }

    [ExcludeFromCodeCoverage]
    DateTime IConvertible.ToDateTime(IFormatProvider? provider)
    {
        return DateTime.DateTime;
    }

    [ExcludeFromCodeCoverage]
    decimal IConvertible.ToDecimal(IFormatProvider? provider)
    {
        throw new NotSupportedException();
    }

    [ExcludeFromCodeCoverage]
    double IConvertible.ToDouble(IFormatProvider? provider)
    {
        throw new NotSupportedException();
    }

    [ExcludeFromCodeCoverage]
    short IConvertible.ToInt16(IFormatProvider? provider)
    {
        throw new NotSupportedException();
    }

    [ExcludeFromCodeCoverage]
    int IConvertible.ToInt32(IFormatProvider? provider)
    {
        throw new NotSupportedException();
    }

    [ExcludeFromCodeCoverage]
    long IConvertible.ToInt64(IFormatProvider? provider)
    {
        throw new NotSupportedException();
    }

    [ExcludeFromCodeCoverage]
    sbyte IConvertible.ToSByte(IFormatProvider? provider)
    {
        throw new NotSupportedException();
    }

    [ExcludeFromCodeCoverage]
    float IConvertible.ToSingle(IFormatProvider? provider)
    {
        throw new NotSupportedException();
    }

    string IConvertible.ToString(IFormatProvider? provider)
    {
        // DateTimeOffset stores 7 fractional digits
        var precision = Math.Clamp(SecondPrecision, 0, 7);
        switch (Kind)
        {
            case TomlDateTimeKind.LocalDateTime:
                if (precision == 0)
                    return DateTime.ToString("yyyy-MM-dd'T'HH:mm:ss", provider);
                return DateTime.ToString($"yyyy-MM-dd'T'HH:mm:ss.{GetFormatPrecision(precision)}", provider);
            case TomlDateTimeKind.LocalDate:
                return DateTime.ToString("yyyy-MM-dd", provider);
            case TomlDateTimeKind.LocalTime:
                if (precision == 0)
                    return DateTime.ToString("HH:mm:ss", provider);
                return DateTime.ToString($"HH:mm:ss.{GetFormatPrecision(precision)}", provider);
            case TomlDateTimeKind.OffsetDateTimeByNumber:
            {
                var time = DateTime;
                if (precision == 0)
                    return time.ToString("yyyy-MM-dd'T'HH:mm:sszzz", provider);
                return time.ToString($"yyyy-MM-dd'T'HH:mm:ss.{GetFormatPrecision(precision)}zzz", provider);
            }
            case TomlDateTimeKind.OffsetDateTimeByZ:
            default:
            {
                var time = DateTime.ToUniversalTime();
                if (precision == 0)
                    return time.ToString("yyyy-MM-dd'T'HH:mm:ssZ", provider);
                return time.ToString($"yyyy-MM-dd'T'HH:mm:ss.{GetFormatPrecision(precision)}Z", provider);
            }
        }
    }

    object IConvertible.ToType(Type conversionType, IFormatProvider? provider)
    {
        if (conversionType == typeof(DateTime))
        {
            return DateTime.DateTime;
        }

        if (conversionType == typeof(DateTimeOffset))
        {
            return DateTime;
        }

        if (conversionType == typeof(DateOnly))
        {
            return DateOnly.FromDateTime(DateTime.DateTime);
        }

        if (conversionType == typeof(TimeOnly))
        {
            return TimeOnly.FromDateTime(DateTime.DateTime);
        }

        throw new InvalidCastException($"Unable to convert {nameof(TomlDateTime)} to destination type {conversionType.FullName}");
    }

    [ExcludeFromCodeCoverage]
    ushort IConvertible.ToUInt16(IFormatProvider? provider)
    {
        throw new NotSupportedException();
    }

    [ExcludeFromCodeCoverage]
    uint IConvertible.ToUInt32(IFormatProvider? provider)
    {
        throw new NotSupportedException();
    }

    [ExcludeFromCodeCoverage]
    ulong IConvertible.ToUInt64(IFormatProvider? provider)
    {
        throw new NotSupportedException();
    }

    /// <summary>
    /// Gets the string formatter for the specified precision.
    /// </summary>
    /// <param name="precision">A precision number from 1 to 7. Other numbers will return the default `fff` formatter.</param>
    /// <returns>The string formatter for the specified precision.</returns>
    public static string GetFormatPrecision(int precision)
    {
        switch (precision)
        {
            case 1: return "f";
            case 2: return "ff";
            case 3: return "fff";
            case 4: return "ffff";
            case 5: return "fffff";
            case 6: return "ffffff";
            case 7: return "fffffff";
            default:
                return "fff";
        }
    }

    /// <summary>
    /// Converts a datetime to TomlDateTime, the way the serializer writes it.
    /// </summary>
    /// <remarks>
    /// A <see cref="DateTimeKind.Utc"/> value is an offset date-time with <c>Z</c>, a <see cref="DateTimeKind.Local"/> value an
    /// offset date-time with the offset of the machine, and an <see cref="DateTimeKind.Unspecified"/> value a local
    /// date-time. The fractional seconds are kept.
    /// </remarks>
    public static implicit operator TomlDateTime(DateTime dateTime)
    {
        return TomlFormatHelper.ToTomlDateTime(dateTime);
    }
}
