using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using static Meziantou.Framework.Geo.Internals.Parquet.ParquetMetadata;

namespace Meziantou.Framework.Geo.Internals.Parquet;

/// <summary>
/// A leaf column of the schema of a Parquet file: its path, its levels, its physical type, and the conversion of its
/// values to the .NET type of its logical type.
/// </summary>
internal sealed class ParquetColumn
{
    public const int Boolean = 0;
    public const int Int32 = 1;
    public const int Int64 = 2;
    public const int Int96 = 3;
    public const int Float = 4;
    public const int Double = 5;
    public const int ByteArray = 6;
    public const int FixedLenByteArray = 7;

    // The legacy converted types (ConvertedType of parquet.thrift)
    private const int ConvertedUtf8 = 0;
    private const int ConvertedEnum = 4;
    private const int ConvertedDecimal = 5;
    private const int ConvertedDate = 6;
    private const int ConvertedTimeMillis = 7;
    private const int ConvertedTimeMicros = 8;
    private const int ConvertedTimestampMillis = 9;
    private const int ConvertedTimestampMicros = 10;
    private const int ConvertedUInt8 = 11;
    private const int ConvertedUInt16 = 12;
    private const int ConvertedUInt32 = 13;
    private const int ConvertedUInt64 = 14;
    private const int ConvertedInt8 = 15;
    private const int ConvertedInt16 = 16;
    private const int ConvertedJson = 19;

    private static readonly DateTime UnixEpoch = DateTime.UnixEpoch;

    private readonly ValueKind _kind;
    private readonly int _scale;
    private readonly bool _isUtc;

    public ParquetColumn(string[] path, SchemaElement element, int maxDefinitionLevel, int maxRepetitionLevel)
    {
        Path = path;
        Name = element.Name;
        PhysicalType = element.Type ?? throw new FormatException($"The column '{string.Join('.', path)}' of the Parquet file has no type.");
        TypeLength = element.TypeLength;
        MaxDefinitionLevel = maxDefinitionLevel;
        MaxRepetitionLevel = maxRepetitionLevel;
        if (PhysicalType is FixedLenByteArray && TypeLength <= 0)
            throw new FormatException($"The column '{string.Join('.', path)}' of the Parquet file has no length.");

        (_kind, _scale, _isUtc) = GetKind(element);
    }

    private enum ValueKind
    {
        Physical,
        String,
        Date,
        TimeMillis,
        TimeMicros,
        TimeNanos,
        TimestampMillis,
        TimestampMicros,
        TimestampNanos,
        Decimal,
        Int8,
        Int16,
        UInt8,
        UInt16,
        UInt32,
        UInt64,
        Uuid,
    }

    public string[] Path { get; }

    public string Name { get; }

    public int PhysicalType { get; }

    public int TypeLength { get; }

    public int MaxDefinitionLevel { get; }

    public int MaxRepetitionLevel { get; }

    public static object FromBoolean(bool value) => value;

    public object FromInt32(int value) => _kind switch
    {
        ValueKind.Int8 => (sbyte)value,
        ValueKind.Int16 => (short)value,
        ValueKind.UInt8 => (byte)value,
        ValueKind.UInt16 => (ushort)value,
        ValueKind.UInt32 => (uint)value,
        ValueKind.Date => ToDate(value),
        ValueKind.TimeMillis => ToTime(value * TimeSpan.TicksPerMillisecond),
        ValueKind.Decimal => ToDecimal(value),
        _ => value,
    };

    public object FromInt64(long value) => _kind switch
    {
        ValueKind.UInt64 => (ulong)value,
        ValueKind.UInt32 => (uint)value,
        ValueKind.TimeMicros => ToTime(value * 10),
        ValueKind.TimeNanos => ToTime(value / 100),
        ValueKind.TimestampMillis => ToDateTime(checked(value * TimeSpan.TicksPerMillisecond)),
        ValueKind.TimestampMicros => ToDateTime(checked(value * 10)),
        ValueKind.TimestampNanos => ToDateTime(value / 100),
        ValueKind.Decimal => ToDecimal(value),
        _ => value,
    };

    public static object FromFloat(float value) => value;

    public static object FromDouble(double value) => value;

    // The deprecated timestamps of Impala: the nanoseconds of the day, then the Julian day
    public static object FromInt96(ReadOnlySpan<byte> value)
    {
        var nanoseconds = BinaryPrimitives.ReadInt64LittleEndian(value);
        var julianDay = BinaryPrimitives.ReadInt32LittleEndian(value[8..]);
        const int UnixEpochJulianDay = 2_440_588;
        return ToDateTime(checked(((julianDay - UnixEpochJulianDay) * TimeSpan.TicksPerDay) + (nanoseconds / 100)), isUtc: true);
    }

    public object FromBytes(ReadOnlySpan<byte> value) => _kind switch
    {
        ValueKind.String => Encoding.UTF8.GetString(value),
        ValueKind.Decimal => ToDecimal(new BigInteger(value, isUnsigned: false, isBigEndian: true)),
        ValueKind.Uuid when value.Length is 16 => new Guid(value, bigEndian: true),
        _ => value.ToArray(),
    };

    private static DateOnly ToDate(int daysSinceEpoch)
    {
        var dayNumber = (long)DateOnly.FromDateTime(UnixEpoch).DayNumber + daysSinceEpoch;
        if (dayNumber < 0 || dayNumber > DateOnly.MaxValue.DayNumber)
            throw new FormatException("A date of the Parquet file is out of range.");

        return DateOnly.FromDayNumber((int)dayNumber);
    }

    private static TimeOnly ToTime(long ticks) => ticks is >= 0 and < TimeSpan.TicksPerDay ? new TimeOnly(ticks) : throw new FormatException("A time of the Parquet file is out of range.");

    private DateTime ToDateTime(long ticks) => ToDateTime(ticks, _isUtc);

    private static DateTime ToDateTime(long ticks, bool isUtc)
    {
        var result = UnixEpoch.Ticks + ticks;
        if (result < DateTime.MinValue.Ticks || result > DateTime.MaxValue.Ticks)
            throw new FormatException("A timestamp of the Parquet file is out of range.");

        return new DateTime(result, isUtc ? DateTimeKind.Utc : DateTimeKind.Unspecified);
    }

    // A decimal of up to 28 digits after the point and 96 bits, else the nearest double
    private object ToDecimal(BigInteger unscaled)
    {
        if (_scale is >= 0 and <= 28 && BigInteger.Abs(unscaled) < BigInteger.One << 96)
        {
            var magnitude = BigInteger.Abs(unscaled);
            var low = (int)(uint)(magnitude & uint.MaxValue);
            var middle = (int)(uint)((magnitude >> 32) & uint.MaxValue);
            var high = (int)(uint)((magnitude >> 64) & uint.MaxValue);
            return new decimal(low, middle, high, unscaled.Sign < 0, (byte)_scale);
        }

        return ToDouble(unscaled, _scale);
    }

    private static object ToDouble(BigInteger unscaled, int scale) => (double)unscaled * Math.Pow(10, -scale);

    private static (ValueKind Kind, int Scale, bool IsUtc) GetKind(SchemaElement element)
    {
        if (element.LogicalType is { } logicalType)
        {
            switch (logicalType.Kind)
            {
                case LogicalType.String or LogicalType.Enum or LogicalType.Json:
                    return (ValueKind.String, 0, false);
                case LogicalType.Decimal:
                    return (ValueKind.Decimal, logicalType.Scale, false);
                case LogicalType.Date:
                    return (ValueKind.Date, 0, false);
                case LogicalType.Uuid:
                    return (ValueKind.Uuid, 0, false);
                case LogicalType.Time:
                    return (logicalType.TimeUnit switch
                    {
                        LogicalType.Millis => ValueKind.TimeMillis,
                        LogicalType.Micros => ValueKind.TimeMicros,
                        _ => ValueKind.TimeNanos,
                    }, 0, false);
                case LogicalType.Timestamp:
                    return (logicalType.TimeUnit switch
                    {
                        LogicalType.Millis => ValueKind.TimestampMillis,
                        LogicalType.Micros => ValueKind.TimestampMicros,
                        _ => ValueKind.TimestampNanos,
                    }, 0, logicalType.IsAdjustedToUtc);
                case LogicalType.Integer:
                    return ((logicalType.BitWidth, logicalType.IsSigned) switch
                    {
                        (8, true) => ValueKind.Int8,
                        (16, true) => ValueKind.Int16,
                        (8, false) => ValueKind.UInt8,
                        (16, false) => ValueKind.UInt16,
                        (32, false) => ValueKind.UInt32,
                        (64, false) => ValueKind.UInt64,
                        _ => ValueKind.Physical,
                    }, 0, false);
            }
        }

        // The legacy timestamps are adjusted to UTC
        return (element.ConvertedType switch
        {
            ConvertedUtf8 or ConvertedEnum or ConvertedJson => ValueKind.String,
            ConvertedDecimal => ValueKind.Decimal,
            ConvertedDate => ValueKind.Date,
            ConvertedTimeMillis => ValueKind.TimeMillis,
            ConvertedTimeMicros => ValueKind.TimeMicros,
            ConvertedTimestampMillis => ValueKind.TimestampMillis,
            ConvertedTimestampMicros => ValueKind.TimestampMicros,
            ConvertedUInt8 => ValueKind.UInt8,
            ConvertedUInt16 => ValueKind.UInt16,
            ConvertedUInt32 => ValueKind.UInt32,
            ConvertedUInt64 => ValueKind.UInt64,
            ConvertedInt8 => ValueKind.Int8,
            ConvertedInt16 => ValueKind.Int16,
            _ => ValueKind.Physical,
        }, element.Scale, element.ConvertedType is ConvertedTimestampMillis or ConvertedTimestampMicros);
    }
}
