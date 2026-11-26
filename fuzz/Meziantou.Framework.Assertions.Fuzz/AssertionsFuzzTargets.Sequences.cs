using System.Buffers.Binary;
using System.Collections;
using Meziantou.Framework.Fuzzing;

namespace Meziantou.Framework.Assertions.Fuzz;

/// <summary>
/// <c>assertions-sequences</c>: two collections of simple values are compared with <c>Assert.Equivalent</c>, the
/// non-generic <c>Assert.Equal</c> and <c>Assert.EqualUnordered</c>. Each result must match the result of the same
/// assertion on the same items enumerated lazily (the fast paths only apply to arrays and lists), and, when both
/// collections have the same item type, the result of an item-by-item comparison with <see cref="object.Equals(object?)"/>.
/// The items are also compared one by one with <c>Assert.Equal</c> and <c>Assert.NotEqual</c>, as values and boxed.
/// </summary>
/// <remarks>
/// The input starts with three option bytes: the item type, the flags (<see cref="SequenceOptions"/>) and the index of
/// the item to replace. The rest of the input is read as the items (little-endian values, or texts separated by
/// <c>\0</c>), split into the expected and the actual collections depending on the mode.
/// </remarks>
public static partial class AssertionsFuzzTargets
{
    private const int MaxSequenceItems = 1024;
    private const int MaxValuePairs = 8;
    private const char StringSeparator = '\0';

    private const byte IgnoreStringCaseFlag = 0x01;
    private const byte IgnoreCollectionOrderFlag = 0x02;
    private const byte HolderFlag = 0x04;
    private const byte ActualListFlag = 0x08;
    private const byte TwinTypeFlag = 0x10;
    private const int ModeShift = 5;
    private const byte ExpectedListFlag = 0x80;

    private delegate T ReadValue<T>(ReadOnlySpan<byte> bytes);

    private enum SequenceType
    {
        Byte,
        Int32,
        Int64,
        Double,
        Single,
        Decimal,
        DateTime,
        DateTimeOffset,
        Guid,
        Enum,
        Char,
        Boolean,
        Half,
        TimeSpan,
        Int128,
        String,
    }

    private enum SequenceMode
    {
        /// <summary>The actual collection is a copy of the expected one.</summary>
        Copy,

        /// <summary>The last item replaces the item at the index of the options in a copy of the other items.</summary>
        ReplaceOne,

        /// <summary>The first half of the items is the expected collection, the rest the actual one.</summary>
        Halves,

        /// <summary>The actual collection has the items in reverse order.</summary>
        Reversed,
    }

    private sealed record SequenceOptions(byte Flags, byte Index)
    {
        public bool IgnoreStringCase => (Flags & IgnoreStringCaseFlag) is not 0;

        public bool IgnoreCollectionOrder => (Flags & IgnoreCollectionOrderFlag) is not 0;

        /// <summary>Gets a value indicating whether the collections are compared as a member of an object.</summary>
        public bool InHolder => (Flags & HolderFlag) is not 0;

        public bool ExpectedList => (Flags & ExpectedListFlag) is not 0;

        public bool ActualList => (Flags & ActualListFlag) is not 0;

        /// <summary>Gets a value indicating whether the actual items have another type of the same size (<c>int</c> and <c>uint</c>, an enum and <c>int</c>, ...).</summary>
        public bool TwinType => (Flags & TwinTypeFlag) is not 0;

        public SequenceMode Mode => (SequenceMode)((Flags >> ModeShift) & 3);
    }

    private static void Sequences(ReadOnlySpan<byte> data)
    {
        var type = (SequenceType)(FuzzInput.ReadByte(ref data) % (int)(SequenceType.String + 1));
        var flags = FuzzInput.ReadByte(ref data);
        var options = new SequenceOptions(flags, FuzzInput.ReadByte(ref data));
        switch (type)
        {
            case SequenceType.Byte:
                Check(Read(data, sizeof(byte), bytes => bytes[0]), options, value => unchecked((sbyte)value));
                break;
            case SequenceType.Int32:
                Check(Read(data, sizeof(int), BinaryPrimitives.ReadInt32LittleEndian), options, value => unchecked((uint)value));
                break;
            case SequenceType.Int64:
                Check(Read(data, sizeof(long), BinaryPrimitives.ReadInt64LittleEndian), options, value => unchecked((ulong)value));
                break;
            case SequenceType.Double:
                Check(Read(data, sizeof(double), BinaryPrimitives.ReadDoubleLittleEndian), options, BitConverter.DoubleToInt64Bits);
                break;
            case SequenceType.Single:
                Check(Read(data, sizeof(float), BinaryPrimitives.ReadSingleLittleEndian), options, value => (double)value);
                break;
            case SequenceType.Decimal:
                Check(Read(data, DecimalSize, ReadDecimal), options);
                break;
            case SequenceType.DateTime:
                Check(Read(data, DateTimeSize, ReadDateTime), options, value => new DateTimeOffset(value.Ticks, TimeSpan.Zero));
                break;
            case SequenceType.DateTimeOffset:
                Check(Read(data, DateTimeOffsetSize, ReadDateTimeOffset), options, value => value.UtcDateTime);
                break;
            case SequenceType.Guid:
                Check(Read(data, 16, bytes => new Guid(bytes[..16])), options);
                break;
            case SequenceType.Enum:
                Check(Read(data, sizeof(int), bytes => (DayOfWeek)BinaryPrimitives.ReadInt32LittleEndian(bytes)), options, value => (int)value);
                break;
            case SequenceType.Char:
                Check(Read(data, sizeof(char), bytes => (char)BinaryPrimitives.ReadUInt16LittleEndian(bytes)), options, value => (ushort)value);
                break;
            case SequenceType.Boolean:
                Check(Read(data, sizeof(bool), bytes => (bytes[0] & 1) is not 0), options);
                break;
            case SequenceType.Half:
                Check(Read(data, 2, BinaryPrimitives.ReadHalfLittleEndian), options, value => (float)value);
                break;
            case SequenceType.TimeSpan:
                Check(Read(data, sizeof(long), bytes => new TimeSpan(BinaryPrimitives.ReadInt64LittleEndian(bytes))), options, value => value.Ticks);
                break;
            case SequenceType.Int128:
                Check(Read(data, 16, BinaryPrimitives.ReadInt128LittleEndian), options, value => unchecked((UInt128)value));
                break;
            default:
                Check(ReadStrings(data), options);
                break;
        }
    }

    private static void Check<T>(T[] values, SequenceOptions options) => Check<T, T>(values, options, twin: null);

    private static void Check<T, TTwin>(T[] values, SequenceOptions options, Converter<T, TTwin>? twin)
    {
        var (expected, actual) = Split(values, options);
        if (twin is not null && options.TwinType)
        {
            CheckSequences(expected, Array.ConvertAll(actual, twin), options);
        }
        else
        {
            CheckSequences(expected, actual, options);
        }
    }

    private static (T[] Expected, T[] Actual) Split<T>(T[] values, SequenceOptions options)
    {
        switch (options.Mode)
        {
            case SequenceMode.ReplaceOne when values.Length >= 2:
                var expected = values[..^1];
                var actual = values[..^1];
                actual[options.Index % actual.Length] = values[^1];
                return (expected, actual);

            case SequenceMode.Halves:
                var half = values.Length / 2;
                return (values[..half], values[half..]);

            case SequenceMode.Reversed:
                var reversed = values[..];
                Array.Reverse(reversed);
                return (values, reversed);

            default:
                return (values, values[..]);
        }
    }

    private static void CheckSequences<TExpected, TActual>(TExpected[] expected, TActual[] actual, SequenceOptions options)
    {
        object expectedCollection = options.ExpectedList ? new List<TExpected>(expected) : expected;
        object actualCollection = options.ActualList ? new List<TActual>(actual) : actual;
        var description = $"{Describe<TExpected>(options.ExpectedList, expected.Length)} and {Describe<TActual>(options.ActualList, actual.Length)}";

        // When the items have the same type, the collections are equal when their items are, one by one. An `as` cast
        // would not do: the runtime casts a uint[] to an int[] (and an enum array to its underlying type).
        var actualSameType = typeof(TExpected) == typeof(TActual) ? (TExpected[])(object)actual : null;
        var itemsEqual = actualSameType is not null && ItemsEqual(expected, actualSameType, options.IgnoreStringCase);
        var itemsEqualOrdinal = actualSameType is not null && ItemsEqual(expected, actualSameType, ignoreStringCase: false);

        // Equivalent
        var equivalentOptions = new EquivalentOptions { IgnoreStringCase = options.IgnoreStringCase, IgnoreCollectionOrder = options.IgnoreCollectionOrder };
        var equivalent = Passes(() => Assert.Equivalent(Wrap(expectedCollection, options), Wrap(actualCollection, options), equivalentOptions));
        var lazyEquivalent = Passes(() => Assert.Equivalent(Wrap(Lazy(expected), options), Wrap(Lazy(actual), options), equivalentOptions));
        FuzzAssert.Equal(lazyEquivalent, equivalent, $"Equivalent of {description} differs from the comparison of the same items enumerated lazily");
        FuzzAssert.Equal(!equivalent, Passes(() => Assert.NotEquivalent(Wrap(expectedCollection, options), Wrap(actualCollection, options), equivalentOptions)), $"NotEquivalent of {description} is not the opposite of Equivalent");
        if (actualSameType is not null)
        {
            if (options.IgnoreCollectionOrder)
            {
                FuzzAssert.True(!itemsEqual || equivalent, $"Equivalent of {description} ignoring the order fails although the items are equal one by one");
            }
            else
            {
                FuzzAssert.Equal(itemsEqual, equivalent, $"Equivalent of {description} does not match the comparison of the items one by one");
            }
        }

        // The non-generic Equal compares strings ordinally
        var equal = Passes(() => Assert.Equal((IEnumerable)expectedCollection, (IEnumerable)actualCollection));
        var lazyEqual = Passes(() => Assert.Equal(LazyNonGeneric(expected), LazyNonGeneric(actual)));
        FuzzAssert.Equal(lazyEqual, equal, $"Equal of {description} differs from the comparison of the same items enumerated lazily");
        if (actualSameType is not null)
        {
            FuzzAssert.Equal(itemsEqualOrdinal, equal, $"Equal of {description} does not match the comparison of the items one by one");
        }

        if (actualSameType is null)
            return;

        // EqualUnordered compares the items in order first
        var unordered = Passes(() => Assert.EqualUnordered((IEnumerable<TExpected>)expectedCollection, (IEnumerable<TExpected>)actualCollection));
        var lazyUnordered = Passes(() => Assert.EqualUnordered(Lazy(expected), Lazy(actualSameType)));
        FuzzAssert.Equal(lazyUnordered, unordered, $"EqualUnordered of {description} differs from the comparison of the same items enumerated lazily");
        FuzzAssert.True(!itemsEqualOrdinal || unordered, $"EqualUnordered of {description} fails although the items are equal one by one");

        // The items, as values and boxed, and in value types that hold collections (compared by content)
        var pairCount = Math.Min(MaxValuePairs, Math.Min(expected.Length, actualSameType.Length));
        for (var i = 0; i < pairCount; i++)
        {
            CheckValuePair(expected[i], actualSameType[i], $"items at index {i} of {description}");
        }

        if (pairCount > 0)
        {
            CheckValuePair((expected[0], expected), (actualSameType[0], actualSameType), $"tuples holding {description}");
            CheckValuePair(new KeyValuePair<TExpected, List<TExpected>>(expected[0], [.. expected]), new KeyValuePair<TExpected, List<TExpected>>(actualSameType[0], [.. actualSameType]), $"key-value pairs holding {description}");
        }
    }

    /// <summary>
    /// <c>Equal</c> and <c>NotEqual</c> give opposite results, and <c>NotEqual</c> gives the same result on the values
    /// and on the boxed values (value types only box when they may hold a collection).
    /// </summary>
    private static void CheckValuePair<T>(T expected, T actual, string description)
    {
        var equal = Passes(() => Assert.Equal<T>(expected, actual));
        var notEqual = Passes(() => Assert.NotEqual<T>(expected, actual));
        FuzzAssert.Equal(!equal, notEqual, $"NotEqual of {description} is not the opposite of Equal");
        FuzzAssert.Equal(notEqual, Passes(() => Assert.NotEqual<object?>(expected, actual)), $"NotEqual of {description} differs from NotEqual of the boxed values");
    }

    private static bool ItemsEqual<T>(T[] expected, T[] actual, bool ignoreStringCase)
    {
        if (expected.Length != actual.Length)
            return false;

        var comparer = ignoreStringCase && typeof(T) == typeof(string) ? (IEqualityComparer<T>)StringComparer.OrdinalIgnoreCase : EqualityComparer<T>.Default;
        for (var i = 0; i < expected.Length; i++)
        {
            if (!comparer.Equals(expected[i], actual[i]))
                return false;
        }

        return true;
    }

    private static object Wrap(object collection, SequenceOptions options) => options.InHolder ? new SequenceHolder(collection) : collection;

    private static string Describe<T>(bool isList, int count) => isList ? $"List<{typeof(T).Name}>({count})" : $"{typeof(T).Name}[{count}]";

    private static IEnumerable<T> Lazy<T>(T[] values)
    {
        foreach (var value in values)
        {
            yield return value;
        }
    }

    private static IEnumerable LazyNonGeneric(IEnumerable values)
    {
        foreach (var value in values)
        {
            yield return value;
        }
    }

    private static T[] Read<T>(ReadOnlySpan<byte> data, int size, ReadValue<T> read)
    {
        var values = new T[Math.Min(MaxSequenceItems, data.Length / size)];
        for (var i = 0; i < values.Length; i++)
        {
            values[i] = read(data.Slice(i * size, size));
        }

        return values;
    }

    // An empty input is an empty collection; NullStringMarker is a null item
    private static string?[] ReadStrings(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
            return [];

        var items = FuzzText.Decode(data).Split(StringSeparator);
        return [.. items.Take(MaxSequenceItems).Select(item => item is NullStringMarker ? null : item)];
    }

    private const int DecimalSize = 13;

    // Low, middle and high 32 bits of the integer, then the sign (high bit) and the scale (0 to 28)
    private static decimal ReadDecimal(ReadOnlySpan<byte> bytes)
    {
        return new decimal(
            BinaryPrimitives.ReadInt32LittleEndian(bytes),
            BinaryPrimitives.ReadInt32LittleEndian(bytes[4..]),
            BinaryPrimitives.ReadInt32LittleEndian(bytes[8..]),
            isNegative: (bytes[12] & 0x80) is not 0,
            scale: (byte)((bytes[12] & 0x7F) % 29));
    }

    private const int DateTimeSize = 9;

    // The ticks, then the kind
    private static DateTime ReadDateTime(ReadOnlySpan<byte> bytes)
    {
        var ticks = (long)(BinaryPrimitives.ReadUInt64LittleEndian(bytes) % (ulong)(DateTime.MaxValue.Ticks + 1));
        return new DateTime(ticks, (DateTimeKind)(bytes[8] % 3));
    }

    private const int DateTimeOffsetSize = 10;

    // The local ticks, then the offset in minutes (at most 14 hours); the ticks are clamped so that the UTC time is valid
    private static DateTimeOffset ReadDateTimeOffset(ReadOnlySpan<byte> bytes)
    {
        var offsetMinutes = BinaryPrimitives.ReadInt16LittleEndian(bytes[8..]) % 841;
        var offsetTicks = offsetMinutes * TimeSpan.TicksPerMinute;
        var ticks = (long)(BinaryPrimitives.ReadUInt64LittleEndian(bytes) % (ulong)(DateTime.MaxValue.Ticks + 1));
        ticks = Math.Clamp(ticks, Math.Max(0, offsetTicks), Math.Min(DateTime.MaxValue.Ticks, DateTime.MaxValue.Ticks + offsetTicks));
        return new DateTimeOffset(ticks, TimeSpan.FromMinutes(offsetMinutes));
    }

    private static IEnumerable<FuzzSeed> GetSequenceSeeds()
    {
        // Every item type in every mode, with lengths around the vector widths
        var bytes = new byte[16 * 37];
        for (var i = 0; i < bytes.Length; i++)
        {
            bytes[i] = (byte)((i * 37) + 11);
        }

        for (var type = SequenceType.Byte; type <= SequenceType.String; type++)
        {
            for (var mode = SequenceMode.Copy; mode <= SequenceMode.Reversed; mode++)
            {
                yield return Seed($"{type}-{mode}", type, mode, flags: 0, index: 3, bytes);
            }

            yield return Seed($"{type}-twin-lists", type, SequenceMode.Copy, TwinTypeFlag | ActualListFlag | ExpectedListFlag, index: 0, bytes);
            yield return Seed($"{type}-holder-unordered", type, SequenceMode.Reversed, HolderFlag | IgnoreCollectionOrderFlag, index: 0, bytes);
            yield return Seed($"{type}-empty", type, SequenceMode.Copy, ActualListFlag, index: 0, []);
        }

        // The values of the unit tests (AssertEqualByStructureTests, AssertEqualTests, AssertEqualUnorderedTests)
        yield return Seed("int32-equal", SequenceType.Int32, SequenceMode.Copy, flags: 0, index: 0, Int32s(1, 2, 3));
        yield return Seed("int32-list", SequenceType.Int32, SequenceMode.Copy, ActualListFlag, index: 0, Int32s(1, 2, 3));
        yield return Seed("int32-uint32", SequenceType.Int32, SequenceMode.Copy, TwinTypeFlag, index: 0, Int32s(1, 2, -1));
        yield return Seed("int32-differs", SequenceType.Int32, SequenceMode.Halves, flags: 0, index: 0, Int32s(1, 2, 3, 1, 2, 4));
        yield return Seed("int32-reversed", SequenceType.Int32, SequenceMode.Reversed, IgnoreCollectionOrderFlag, index: 0, Int32s(0, 1, 2, 3, 4, 5, 6, 7));
        yield return Seed("double-negative-zero-nan", SequenceType.Double, SequenceMode.ReplaceOne, flags: 0, index: 0, Doubles(0.0, double.NaN, -0.0));
        yield return Seed("double-nan-payload", SequenceType.Double, SequenceMode.ReplaceOne, flags: 0, index: 1, Doubles(1.0, double.NaN, BitConverter.Int64BitsToDouble(unchecked((long)0xFFF8000000000001))));
        yield return Seed("decimal-scale", SequenceType.Decimal, SequenceMode.Halves, flags: 0, index: 0, Decimals(1.5m, 2m, 1.50m, 2.0m));
        yield return Seed("datetimeoffset-same-instant", SequenceType.DateTimeOffset, SequenceMode.ReplaceOne, flags: 0, index: 0, DateTimeOffsets(new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2024, 1, 1, 2, 0, 0, TimeSpan.FromHours(2))));
        yield return Seed("enum", SequenceType.Enum, SequenceMode.Copy, TwinTypeFlag, index: 0, Int32s((int)DayOfWeek.Monday, (int)DayOfWeek.Friday));
        yield return Seed("strings-case", SequenceType.String, SequenceMode.Halves, flags: 0, index: 0, Strings("a", null, "B", "A", null, "b"));
        yield return Seed("strings-ignore-case", SequenceType.String, SequenceMode.Halves, IgnoreStringCaseFlag | ActualListFlag, index: 0, Strings("a", null, "B", "A", null, "b"));
        yield return Seed("strings-null-item", SequenceType.String, SequenceMode.ReplaceOne, HolderFlag, index: 1, Strings("a", "b", null));
    }

    private static FuzzSeed Seed(string name, SequenceType type, SequenceMode mode, int flags, byte index, byte[] items) => new($"sequence-{name}", (byte[])[(byte)type, (byte)(flags | ((int)mode << ModeShift)), index, .. items]);

    private static byte[] Int32s(params int[] values)
    {
        var bytes = new byte[values.Length * sizeof(int)];
        for (var i = 0; i < values.Length; i++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(i * sizeof(int)), values[i]);
        }

        return bytes;
    }

    private static byte[] Doubles(params double[] values)
    {
        var bytes = new byte[values.Length * sizeof(double)];
        for (var i = 0; i < values.Length; i++)
        {
            BinaryPrimitives.WriteDoubleLittleEndian(bytes.AsSpan(i * sizeof(double)), values[i]);
        }

        return bytes;
    }

    private static byte[] Decimals(params decimal[] values)
    {
        var bytes = new byte[values.Length * DecimalSize];
        for (var i = 0; i < values.Length; i++)
        {
            var bits = decimal.GetBits(values[i]);
            var span = bytes.AsSpan(i * DecimalSize);
            BinaryPrimitives.WriteInt32LittleEndian(span, bits[0]);
            BinaryPrimitives.WriteInt32LittleEndian(span[4..], bits[1]);
            BinaryPrimitives.WriteInt32LittleEndian(span[8..], bits[2]);
            span[12] = (byte)(((bits[3] >> 16) & 0x7F) | (bits[3] < 0 ? 0x80 : 0));
        }

        return bytes;
    }

    private static byte[] DateTimeOffsets(params DateTimeOffset[] values)
    {
        var bytes = new byte[values.Length * DateTimeOffsetSize];
        for (var i = 0; i < values.Length; i++)
        {
            var span = bytes.AsSpan(i * DateTimeOffsetSize);
            BinaryPrimitives.WriteInt64LittleEndian(span, values[i].Ticks);
            BinaryPrimitives.WriteInt16LittleEndian(span[8..], (short)values[i].TotalOffsetMinutes);
        }

        return bytes;
    }

    private static byte[] Strings(params string?[] values) => FuzzText.Encode(string.Join(StringSeparator, values.Select(value => value ?? NullStringMarker)));

    /// <summary>An object whose member holds a collection, so that the collection is compared as a member.</summary>
    private sealed class SequenceHolder(object values)
    {
        public object Values { get; } = values;
    }
}
