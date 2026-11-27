using System.Numerics;

namespace Meziantou.Framework.Csv.Serialization.Converters;

/// <summary>The converters of the types the serializer supports without a converter of the user.</summary>
/// <remarks>The converters are selected with <see langword="typeof"/> comparisons, so this works without reflection with Native AOT.</remarks>
internal static class CsvDefaultConverters
{
    private static readonly StringCsvConverter String = new();
    private static readonly BooleanCsvConverter Boolean = new();
    private static readonly CharCsvConverter Char = new();
    private static readonly Utf8SpanParsableCsvConverter<byte> Byte = new();
    private static readonly Utf8SpanParsableCsvConverter<sbyte> SByte = new();
    private static readonly Utf8SpanParsableCsvConverter<short> Int16 = new();
    private static readonly Utf8SpanParsableCsvConverter<ushort> UInt16 = new();
    private static readonly Utf8SpanParsableCsvConverter<int> Int32 = new();
    private static readonly Utf8SpanParsableCsvConverter<uint> UInt32 = new();
    private static readonly Utf8SpanParsableCsvConverter<long> Int64 = new();
    private static readonly Utf8SpanParsableCsvConverter<ulong> UInt64 = new();
    private static readonly Utf8SpanParsableCsvConverter<Int128> Int128 = new();
    private static readonly Utf8SpanParsableCsvConverter<UInt128> UInt128 = new();
    private static readonly Utf8SpanParsableCsvConverter<nint> IntPtr = new();
    private static readonly Utf8SpanParsableCsvConverter<nuint> UIntPtr = new();
    private static readonly FloatingPointCsvConverter<Half> Half = new();
    private static readonly FloatingPointCsvConverter<float> Single = new();
    private static readonly FloatingPointCsvConverter<double> Double = new();
    private static readonly DecimalCsvConverter Decimal = new();
    private static readonly SpanParsableCsvConverter<BigInteger> BigInteger = new();
    private static readonly Utf8SpanParsableCsvConverter<Guid> Guid = new();
    private static readonly DateTimeCsvConverter DateTime = new();
    private static readonly DateTimeOffsetCsvConverter DateTimeOffset = new();
    private static readonly DateOnlyCsvConverter DateOnly = new();
    private static readonly TimeOnlyCsvConverter TimeOnly = new();
    private static readonly TimeSpanCsvConverter TimeSpan = new();
    private static readonly UriCsvConverter Uri = new();
    private static readonly VersionCsvConverter Version = new();
    private static readonly ByteArrayCsvConverter ByteArray = new();

    public static CsvConverter<T>? Get<T>() => Cache<T>.Converter;

    private static CsvConverter? Create<T>()
    {
        var type = typeof(T);
        if (type == typeof(string)) return String;
        if (type == typeof(bool)) return Boolean;
        if (type == typeof(char)) return Char;
        if (type == typeof(byte)) return Byte;
        if (type == typeof(sbyte)) return SByte;
        if (type == typeof(short)) return Int16;
        if (type == typeof(ushort)) return UInt16;
        if (type == typeof(int)) return Int32;
        if (type == typeof(uint)) return UInt32;
        if (type == typeof(long)) return Int64;
        if (type == typeof(ulong)) return UInt64;
        if (type == typeof(Int128)) return Int128;
        if (type == typeof(UInt128)) return UInt128;
        if (type == typeof(nint)) return IntPtr;
        if (type == typeof(nuint)) return UIntPtr;
        if (type == typeof(Half)) return Half;
        if (type == typeof(float)) return Single;
        if (type == typeof(double)) return Double;
        if (type == typeof(decimal)) return Decimal;
        if (type == typeof(BigInteger)) return BigInteger;
        if (type == typeof(Guid)) return Guid;
        if (type == typeof(DateTime)) return DateTime;
        if (type == typeof(DateTimeOffset)) return DateTimeOffset;
        if (type == typeof(DateOnly)) return DateOnly;
        if (type == typeof(TimeOnly)) return TimeOnly;
        if (type == typeof(TimeSpan)) return TimeSpan;
        if (type == typeof(Uri)) return Uri;
        if (type == typeof(Version)) return Version;
        if (type == typeof(byte[])) return ByteArray;

        if (type == typeof(bool?)) return new NullableCsvConverter<bool>(Boolean);
        if (type == typeof(char?)) return new NullableCsvConverter<char>(Char);
        if (type == typeof(byte?)) return new NullableCsvConverter<byte>(Byte);
        if (type == typeof(sbyte?)) return new NullableCsvConverter<sbyte>(SByte);
        if (type == typeof(short?)) return new NullableCsvConverter<short>(Int16);
        if (type == typeof(ushort?)) return new NullableCsvConverter<ushort>(UInt16);
        if (type == typeof(int?)) return new NullableCsvConverter<int>(Int32);
        if (type == typeof(uint?)) return new NullableCsvConverter<uint>(UInt32);
        if (type == typeof(long?)) return new NullableCsvConverter<long>(Int64);
        if (type == typeof(ulong?)) return new NullableCsvConverter<ulong>(UInt64);
        if (type == typeof(Int128?)) return new NullableCsvConverter<Int128>(Int128);
        if (type == typeof(UInt128?)) return new NullableCsvConverter<UInt128>(UInt128);
        if (type == typeof(nint?)) return new NullableCsvConverter<nint>(IntPtr);
        if (type == typeof(nuint?)) return new NullableCsvConverter<nuint>(UIntPtr);
        if (type == typeof(Half?)) return new NullableCsvConverter<Half>(Half);
        if (type == typeof(float?)) return new NullableCsvConverter<float>(Single);
        if (type == typeof(double?)) return new NullableCsvConverter<double>(Double);
        if (type == typeof(decimal?)) return new NullableCsvConverter<decimal>(Decimal);
        if (type == typeof(BigInteger?)) return new NullableCsvConverter<BigInteger>(BigInteger);
        if (type == typeof(Guid?)) return new NullableCsvConverter<Guid>(Guid);
        if (type == typeof(DateTime?)) return new NullableCsvConverter<DateTime>(DateTime);
        if (type == typeof(DateTimeOffset?)) return new NullableCsvConverter<DateTimeOffset>(DateTimeOffset);
        if (type == typeof(DateOnly?)) return new NullableCsvConverter<DateOnly>(DateOnly);
        if (type == typeof(TimeOnly?)) return new NullableCsvConverter<TimeOnly>(TimeOnly);
        if (type == typeof(TimeSpan?)) return new NullableCsvConverter<TimeSpan>(TimeSpan);

        return null;
    }

    private static class Cache<T>
    {
        public static readonly CsvConverter<T>? Converter = (CsvConverter<T>?)Create<T>();
    }
}
