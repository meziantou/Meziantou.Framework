using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Fonts;

/// <summary>
/// Reads the big-endian values of the font tables. A font is untrusted input: every read is checked against the bounds
/// of the table, and a read outside them is an <see cref="InvalidFontException"/>.
/// </summary>
/// <remarks>
/// The readers of fixed-size values are on every path of the code that reads a font (every lookup of a coverage table,
/// every field of a subtable): they are inlined, and their exception is thrown by a method of its own so that they
/// stay small. Without the attribute, an optimized build without profile data (NativeAOT, or the first optimized
/// code of a method) calls them.
/// </remarks>
internal static class FontBinary
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int UInt8(ReadOnlySpan<byte> data, int offset)
    {
        if ((uint)offset >= (uint)data.Length)
            ThrowTruncated();

        return data[offset];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Int8(ReadOnlySpan<byte> data, int offset) => (sbyte)UInt8(data, offset);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int UInt16(ReadOnlySpan<byte> data, int offset)
    {
        if ((ulong)(uint)offset + 2 > (uint)data.Length)
            ThrowTruncated();

        return BinaryPrimitives.ReadUInt16BigEndian(data.Slice(offset, 2));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Int16(ReadOnlySpan<byte> data, int offset)
    {
        if ((ulong)(uint)offset + 2 > (uint)data.Length)
            ThrowTruncated();

        return BinaryPrimitives.ReadInt16BigEndian(data.Slice(offset, 2));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int UInt24(ReadOnlySpan<byte> data, int offset)
    {
        if ((ulong)(uint)offset + 3 > (uint)data.Length)
            ThrowTruncated();

        var bytes = data.Slice(offset, 3);
        return (bytes[0] << 16) | (bytes[1] << 8) | bytes[2];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint UInt32(ReadOnlySpan<byte> data, int offset)
    {
        if ((ulong)(uint)offset + 4 > (uint)data.Length)
            ThrowTruncated();

        return BinaryPrimitives.ReadUInt32BigEndian(data.Slice(offset, 4));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Int32(ReadOnlySpan<byte> data, int offset) => (int)UInt32(data, offset);

    /// <summary>Reads an unsigned 32-bit offset or count that must fit in the non-negative range of an <see cref="int"/>.</summary>
    public static int Offset32(ReadOnlySpan<byte> data, int offset)
    {
        var value = UInt32(data, offset);
        if (value > int.MaxValue)
            ThrowTruncated();

        return (int)value;
    }

    /// <summary>Adds a 32-bit offset to a position of a table: a sum that is not a position of any table is an error of the font.</summary>
    public static int AddOffset(int position, int offset)
    {
        var sum = (long)position + offset;
        if (sum > int.MaxValue)
            ThrowTruncated();

        return (int)sum;
    }

    /// <summary>Gets a part of a table, checked with 64-bit arithmetic.</summary>
    public static ReadOnlySpan<byte> Slice(ReadOnlySpan<byte> data, long offset, long length)
    {
        if (offset < 0 || length < 0 || offset + length > data.Length)
            ThrowTruncated();

        return data.Slice((int)offset, (int)length);
    }

    /// <summary>Checks that a range lies inside a table, with 64-bit arithmetic.</summary>
    public static void EnsureRange(ReadOnlySpan<byte> data, long offset, long length)
    {
        if (offset < 0 || length < 0 || offset + length > data.Length)
            ThrowTruncated();
    }

    [DoesNotReturn]
    private static void ThrowTruncated() => throw FontErrors.Truncated();
}
