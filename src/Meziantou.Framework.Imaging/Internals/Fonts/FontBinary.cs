using System.Buffers.Binary;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Reads the big-endian values of the font tables. A font is untrusted input: every read is checked against the bounds
/// of the table, and a read outside them is an <see cref="InvalidImageContentException"/>.
/// </summary>
internal static class FontBinary
{
    public static int UInt8(ReadOnlySpan<byte> data, int offset)
    {
        if ((uint)offset >= (uint)data.Length)
            throw FontErrors.Truncated();

        return data[offset];
    }

    public static int Int8(ReadOnlySpan<byte> data, int offset) => (sbyte)UInt8(data, offset);

    public static int UInt16(ReadOnlySpan<byte> data, int offset)
    {
        if (offset < 0 || offset > data.Length - 2)
            throw FontErrors.Truncated();

        return BinaryPrimitives.ReadUInt16BigEndian(data[offset..]);
    }

    public static int Int16(ReadOnlySpan<byte> data, int offset)
    {
        if (offset < 0 || offset > data.Length - 2)
            throw FontErrors.Truncated();

        return BinaryPrimitives.ReadInt16BigEndian(data[offset..]);
    }

    public static int UInt24(ReadOnlySpan<byte> data, int offset)
    {
        if (offset < 0 || offset > data.Length - 3)
            throw FontErrors.Truncated();

        return (data[offset] << 16) | (data[offset + 1] << 8) | data[offset + 2];
    }

    public static uint UInt32(ReadOnlySpan<byte> data, int offset)
    {
        if (offset < 0 || offset > data.Length - 4)
            throw FontErrors.Truncated();

        return BinaryPrimitives.ReadUInt32BigEndian(data[offset..]);
    }

    public static int Int32(ReadOnlySpan<byte> data, int offset) => (int)UInt32(data, offset);

    /// <summary>Reads an unsigned 32-bit offset or count that must fit in the non-negative range of an <see cref="int"/>.</summary>
    public static int Offset32(ReadOnlySpan<byte> data, int offset)
    {
        var value = UInt32(data, offset);
        if (value > int.MaxValue)
            throw FontErrors.Truncated();

        return (int)value;
    }

    /// <summary>Adds a 32-bit offset to a position of a table: a sum that is not a position of any table is an error of the font.</summary>
    public static int AddOffset(int position, int offset)
    {
        var sum = (long)position + offset;
        if (sum > int.MaxValue)
            throw FontErrors.Truncated();

        return (int)sum;
    }

    /// <summary>Gets a part of a table, checked with 64-bit arithmetic.</summary>
    public static ReadOnlySpan<byte> Slice(ReadOnlySpan<byte> data, long offset, long length)
    {
        if (offset < 0 || length < 0 || offset + length > data.Length)
            throw FontErrors.Truncated();

        return data.Slice((int)offset, (int)length);
    }

    /// <summary>Checks that a range lies inside a table, with 64-bit arithmetic.</summary>
    public static void EnsureRange(ReadOnlySpan<byte> data, long offset, long length)
    {
        if (offset < 0 || length < 0 || offset + length > data.Length)
            throw FontErrors.Truncated();
    }
}
