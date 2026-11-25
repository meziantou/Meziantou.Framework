using System.Buffers.Binary;
using System.Text;
using System.Text.Unicode;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Reads the values of the tags of a DNG image file directory as the types chapter 4 of the DNG specification allows
/// them: numbers of any integer, rational or floating-point type, and strings. Every count is bounded before anything is
/// read, and a value of a type the tag does not allow is malformed data.
/// </summary>
internal static class DngTagReader
{
    /// <summary>Reads the numbers of a tag, whatever their numeric type.</summary>
    /// <param name="directory">The directory.</param>
    /// <param name="tag">The tag.</param>
    /// <param name="name">The name of the tag, for messages.</param>
    /// <param name="maxCount">The largest accepted number of values.</param>
    /// <returns>The values, or <see langword="null"/> when the tag is absent.</returns>
    /// <exception cref="InvalidImageContentException">The tag has more values than <paramref name="maxCount"/>, none, a non-numeric type, or a zero denominator.</exception>
    public static double[]? TryReadNumbers(TiffDirectory directory, ushort tag, string name, int maxCount)
    {
        if (directory.Find(tag) is not { } field)
            return null;

        if (field.Count == 0 || field.Count > (ulong)maxCount)
            throw DngFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The DNG tag {name} declares {field.Count} values; 1 to {maxCount} are expected."));

        if (!IsNumeric(field.Type))
            throw DngFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The DNG tag {name} uses {TiffFieldTypes.GetName(field.Type)}, which is not a numeric type."));

        var header = directory.Header;
        var result = new double[(int)field.Count];
        using var buffer = directory.ReadValues(field);
        var span = buffer.Span;
        for (var i = 0; i < result.Length; i++)
        {
            result[i] = field.Type switch
            {
                TiffFieldType.Byte => span[i],
                TiffFieldType.SByte => (sbyte)span[i],
                TiffFieldType.Short => header.ReadUInt16(span[(i * 2)..]),
                TiffFieldType.SShort => (short)header.ReadUInt16(span[(i * 2)..]),
                TiffFieldType.Long or TiffFieldType.Ifd => header.ReadUInt32(span[(i * 4)..]),
                TiffFieldType.SLong => (int)header.ReadUInt32(span[(i * 4)..]),
                TiffFieldType.Rational => ReadRational(header.ReadUInt32(span[(i * 8)..]), header.ReadUInt32(span[((i * 8) + 4)..]), name),
                TiffFieldType.SRational => ReadRational((int)header.ReadUInt32(span[(i * 8)..]), (int)header.ReadUInt32(span[((i * 8) + 4)..]), name),
                TiffFieldType.Float => BitConverter.Int32BitsToSingle((int)header.ReadUInt32(span[(i * 4)..])),
                TiffFieldType.Double => BitConverter.Int64BitsToDouble((long)header.ReadUInt64(span[(i * 8)..])),
                _ => header.ReadUInt64(span[(i * 8)..]),
            };

            if (!double.IsFinite(result[i]))
                throw DngFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The DNG tag {name} stores a value that is not a finite number."));
        }

        return result;
    }

    /// <summary>Reads exactly <paramref name="count"/> numbers of a tag.</summary>
    /// <returns>The values, or <see langword="null"/> when the tag is absent.</returns>
    /// <exception cref="InvalidImageContentException">The tag does not have <paramref name="count"/> numbers.</exception>
    public static double[]? TryReadExactNumbers(TiffDirectory directory, ushort tag, string name, int count)
    {
        var values = TryReadNumbers(directory, tag, name, Math.Max(count, 1));
        if (values is not null && values.Length != count)
            throw DngFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The DNG tag {name} declares {values.Length} values; {count} are expected."));

        return values;
    }

    /// <summary>Reads a single number.</summary>
    public static double? TryReadNumber(TiffDirectory directory, ushort tag, string name) => TryReadExactNumbers(directory, tag, name, 1)?[0];

    /// <summary>Reads the integers of a tag, which must be integers of an unsigned type.</summary>
    /// <returns>The values, or <see langword="null"/> when the tag is absent.</returns>
    public static long[]? TryReadIntegers(TiffDirectory directory, ushort tag, string name, int maxCount)
    {
        if (directory.Find(tag) is not { } field)
            return null;

        if (!TiffFieldTypes.IsUnsignedInteger(field.Type))
            throw DngFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The DNG tag {name} uses {TiffFieldTypes.GetName(field.Type)}; an unsigned integer type is expected."));

        var values = TryReadNumbers(directory, tag, name, maxCount)!;
        var result = new long[values.Length];
        for (var i = 0; i < values.Length; i++)
        {
            if (values[i] > long.MaxValue)
                throw DngFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The DNG tag {name} stores the value {values[i]}, which is too large."));

            result[i] = (long)values[i];
        }

        return result;
    }

    /// <summary>Reads the bytes of a tag of type BYTE, UNDEFINED or ASCII.</summary>
    /// <returns>The bytes, or <see langword="null"/> when the tag is absent.</returns>
    public static byte[]? TryReadBytes(TiffDirectory directory, ushort tag, string name, long maxLength)
    {
        if (directory.Find(tag) is not { } field)
            return null;

        if (field.Type is not (TiffFieldType.Byte or TiffFieldType.Undefined or TiffFieldType.Ascii or TiffFieldType.SByte))
            throw DngFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The DNG tag {name} uses {TiffFieldTypes.GetName(field.Type)}; bytes are expected."));

        if (field.Count > (ulong)maxLength)
            throw DngFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The DNG tag {name} declares {field.Count} bytes; at most {maxLength} are read."));

        using var buffer = directory.ReadValues(field);
        return buffer.Span.ToArray();
    }

    /// <summary>
    /// Reads a string of type ASCII or BYTE (UTF-8 for the DNG tags that allow it): the bytes up to the first null.
    /// A string that is not valid UTF-8 is read as Latin-1, and an empty string is <see langword="null"/>.
    /// </summary>
    public static string? TryReadString(TiffDirectory directory, ushort tag, string name, int maxLength = 4096)
    {
        if (directory.Find(tag) is not { } field || field.Type is not (TiffFieldType.Ascii or TiffFieldType.Byte or TiffFieldType.Undefined))
            return null;

        if (field.Count > (ulong)maxLength)
            return null;

        var bytes = TryReadBytes(directory, tag, name, maxLength).AsSpan();
        var end = bytes.IndexOf((byte)0);
        if (end >= 0)
        {
            bytes = bytes[..end];
        }

        if (bytes.IsEmpty)
            return null;

        return Utf8.IsValid(bytes) ? Encoding.UTF8.GetString(bytes) : Encoding.Latin1.GetString(bytes);
    }

    /// <summary>Reads the offsets of a tag that points to image file directories (SubIFDs, the EXIF IFD).</summary>
    public static ulong[]? TryReadOffsets(TiffDirectory directory, ushort tag, string name, int maxCount)
    {
        if (directory.Find(tag) is not { } field)
            return null;

        if (!TiffFieldTypes.IsUnsignedInteger(field.Type) || field.Type is TiffFieldType.Byte or TiffFieldType.Short)
            throw DngFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The DNG tag {name} uses {TiffFieldTypes.GetName(field.Type)}; offsets (LONG or IFD) are expected."));

        if (field.Count > (ulong)maxCount)
            throw DngFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The DNG tag {name} declares {field.Count} directories; at most {maxCount} are read."));

        return directory.ReadUnsignedArray(field, (long)field.Count);
    }

    /// <summary>Reads a big-endian unsigned 32-bit integer of an opcode list.</summary>
    public static uint ReadUInt32BigEndian(ReadOnlySpan<byte> data, int offset) => BinaryPrimitives.ReadUInt32BigEndian(data[offset..]);

    private static bool IsNumeric(TiffFieldType type) => type is TiffFieldType.Byte or TiffFieldType.SByte or TiffFieldType.Short or TiffFieldType.SShort or TiffFieldType.Long or TiffFieldType.SLong
        or TiffFieldType.Rational or TiffFieldType.SRational or TiffFieldType.Float or TiffFieldType.Double or TiffFieldType.Long8 or TiffFieldType.Ifd;

    private static double ReadRational(double numerator, double denominator, string name)
    {
        if (denominator == 0)
            throw DngFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The DNG tag {name} stores a rational value with a zero denominator."));

        return numerator / denominator;
    }
}
