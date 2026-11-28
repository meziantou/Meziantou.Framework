using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.Compression.Internals.Zip;

/// <summary>
/// The data descriptor that follows the content of an entry whose local header does not know the sizes (general purpose
/// flag bit 3): an optional signature, the CRC-32, and the compressed and uncompressed sizes, on 4 bytes, or on 8 bytes
/// for a zip64 entry.
/// </summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct ZipDataDescriptor(uint Crc, long CompressedSize, long UncompressedSize)
{
    /// <summary>The longest descriptor (a signature and 64-bit sizes) and the signature that follows it.</summary>
    public const int MaxLookahead = 4 + 4 + 16 + 4;

    /// <summary>
    /// Matches a data descriptor at the start of <paramref name="data"/>, for a content of <paramref name="compressedSize"/>
    /// bytes. The 4 forms are tried, the 64-bit ones only for a zip64 entry (an entry whose local header has a zip64 extra
    /// field) and first otherwise. With <paramref name="requireNextSignature"/>, a descriptor must be followed by the
    /// signature of a header (<c>PK</c>), which makes a descriptor that is not one very unlikely.
    /// </summary>
    /// <param name="data">The bytes at the position of the descriptor.</param>
    /// <param name="compressedSize">The number of bytes of the content.</param>
    /// <param name="crc">The CRC-32 the descriptor must have, or <see langword="null"/> when it is not known.</param>
    /// <param name="stored">Whether the uncompressed size must be the compressed size.</param>
    /// <param name="zip64">Whether the entry is a zip64 entry.</param>
    /// <param name="requireNextSignature">Whether the descriptor must be followed by a signature.</param>
    /// <param name="descriptor">The descriptor.</param>
    /// <param name="length">The length of the descriptor.</param>
    /// <returns><see langword="true"/> when a descriptor matches.</returns>
    public static bool TryMatch(ReadOnlySpan<byte> data, long compressedSize, uint? crc, bool stored, bool zip64, bool requireNextSignature, out ZipDataDescriptor descriptor, out int length)
    {
        if (zip64)
        {
            return TryMatch(data, compressedSize, crc, stored, signature: true, wide: true, requireNextSignature, out descriptor, out length)
                || TryMatch(data, compressedSize, crc, stored, signature: false, wide: true, requireNextSignature, out descriptor, out length);
        }

        return TryMatch(data, compressedSize, crc, stored, signature: true, wide: false, requireNextSignature, out descriptor, out length)
            || TryMatch(data, compressedSize, crc, stored, signature: false, wide: false, requireNextSignature, out descriptor, out length)
            || TryMatch(data, compressedSize, crc, stored, signature: true, wide: true, requireNextSignature, out descriptor, out length)
            || TryMatch(data, compressedSize, crc, stored, signature: false, wide: true, requireNextSignature, out descriptor, out length);
    }

    /// <summary>Gets a value indicating whether a descriptor of a content of <paramref name="compressedSize"/> bytes may start at <paramref name="data"/> (a fast filter of <see cref="TryMatch(ReadOnlySpan{byte}, long, uint?, bool, bool, bool, out ZipDataDescriptor, out int)"/>).</summary>
    public static bool MayStartAt(ReadOnlySpan<byte> data, long compressedSize)
    {
        if (data.Length < 8)
            return false;

        return BinaryPrimitives.ReadUInt32LittleEndian(data) == ZipEntryInfo.DataDescriptorSignature
            || BinaryPrimitives.ReadUInt32LittleEndian(data[4..]) == (uint)compressedSize;
    }

    private static bool TryMatch(ReadOnlySpan<byte> data, long compressedSize, uint? crc, bool stored, bool signature, bool wide, bool requireNextSignature, out ZipDataDescriptor descriptor, out int length)
    {
        descriptor = default;
        var offset = signature ? 4 : 0;
        length = offset + 4 + (wide ? 16 : 8);
        if (data.Length < length + (requireNextSignature ? 4 : 0))
            return false;

        if (signature && BinaryPrimitives.ReadUInt32LittleEndian(data) != ZipEntryInfo.DataDescriptorSignature)
            return false;

        var descriptorCrc = BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]);
        var sizes = data[(offset + 4)..];
        var (csize, usize) = wide
            ? (BinaryPrimitives.ReadUInt64LittleEndian(sizes), BinaryPrimitives.ReadUInt64LittleEndian(sizes[8..]))
            : (BinaryPrimitives.ReadUInt32LittleEndian(sizes), (ulong)BinaryPrimitives.ReadUInt32LittleEndian(sizes[4..]));

        if (csize != (ulong)compressedSize || usize > long.MaxValue)
            return false;

        if (crc is { } expectedCrc && descriptorCrc != expectedCrc)
            return false;

        if (stored && usize != csize)
            return false;

        // A header follows the descriptor, not another descriptor: a stored content of 12 zero bytes is the descriptor of an
        // empty content (CRC-32 and sizes of 0) followed by the signature of its own descriptor
        if (requireNextSignature && (data[length] != 'P' || data[length + 1] != 'K' || BinaryPrimitives.ReadUInt32LittleEndian(data[length..]) == ZipEntryInfo.DataDescriptorSignature))
            return false;

        descriptor = new ZipDataDescriptor(descriptorCrc, compressedSize, (long)usize);
        return true;
    }
}
