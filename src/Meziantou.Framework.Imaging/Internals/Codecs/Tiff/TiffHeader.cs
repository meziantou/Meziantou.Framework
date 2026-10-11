using System.Buffers.Binary;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The parsed TIFF file header: the byte order, the classic/BigTIFF variant and the offset of the first image file
/// directory. Everything that follows is addressed through file offsets, which is why TIFF is decoded over a
/// <see cref="RandomAccessSource"/> and not through the forward-only parser pipeline.
/// </summary>
/// <param name="IsBigEndian"><see langword="true"/> for the <c>MM</c> byte order.</param>
/// <param name="IsBigTiff"><see langword="true"/> for a BigTIFF (magic 43, 64-bit offsets).</param>
/// <param name="FirstDirectoryOffset">The offset of the first IFD.</param>
internal readonly record struct TiffHeader(bool IsBigEndian, bool IsBigTiff, ulong FirstDirectoryOffset)
{
    /// <summary>Gets the size of an offset and of the inline value field of a directory entry: 4 bytes, or 8 in a BigTIFF.</summary>
    public int OffsetSize => IsBigTiff ? 8 : 4;

    /// <summary>Gets the length of one directory entry.</summary>
    public int EntryLength => IsBigTiff ? TiffFormat.BigTiffEntryLength : TiffFormat.ClassicEntryLength;

    /// <summary>Determines whether a prefix starts with a TIFF or BigTIFF header.</summary>
    /// <param name="prefix">At least 8 bytes.</param>
    /// <returns><see langword="true"/> if the byte order mark and the magic number match.</returns>
    public static bool MatchesSignature(ReadOnlySpan<byte> prefix)
    {
        if (prefix.Length < TiffFormat.ClassicHeaderLength)
            return false;

        bool bigEndian;
        if (prefix.StartsWith(TiffFormat.LittleEndianMark))
        {
            bigEndian = false;
        }
        else if (prefix.StartsWith(TiffFormat.BigEndianMark))
        {
            bigEndian = true;
        }
        else
        {
            return false;
        }

        var magic = bigEndian ? BinaryPrimitives.ReadUInt16BigEndian(prefix[2..]) : BinaryPrimitives.ReadUInt16LittleEndian(prefix[2..]);
        if (magic == TiffFormat.ClassicMagic)
            return true;

        if (magic != TiffFormat.BigTiffMagic)
            return false;

        // A BigTIFF declares an 8-byte offset size and a zero reserved field
        var offsetSize = bigEndian ? BinaryPrimitives.ReadUInt16BigEndian(prefix[4..]) : BinaryPrimitives.ReadUInt16LittleEndian(prefix[4..]);
        var reserved = bigEndian ? BinaryPrimitives.ReadUInt16BigEndian(prefix[6..]) : BinaryPrimitives.ReadUInt16LittleEndian(prefix[6..]);
        return offsetSize == 8 && reserved == 0;
    }

    /// <summary>Reads and validates the header at the start of a source.</summary>
    /// <param name="source">The input.</param>
    /// <returns>The header.</returns>
    /// <exception cref="InvalidImageContentException">The signature, the magic number or the BigTIFF offset size is invalid, or the first IFD offset is outside the input.</exception>
    public static TiffHeader Read(RandomAccessSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        Span<byte> buffer = stackalloc byte[TiffFormat.BigTiffHeaderLength];
        if (source.Length < TiffFormat.ClassicHeaderLength)
            throw TiffFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The TIFF data has {source.Length} bytes; the 8-byte header does not fit."));

        source.Read(0, buffer[..TiffFormat.ClassicHeaderLength]);
        bool bigEndian;
        if (buffer.StartsWith(TiffFormat.LittleEndianMark))
        {
            bigEndian = false;
        }
        else if (buffer.StartsWith(TiffFormat.BigEndianMark))
        {
            bigEndian = true;
        }
        else
        {
            throw TiffFormat.Invalid("The TIFF byte-order mark is neither 'II' (little endian) nor 'MM' (big endian).");
        }

        var magic = ReadUInt16(buffer[2..], bigEndian);
        ulong firstDirectory;
        bool isBigTiff;
        if (magic == TiffFormat.ClassicMagic)
        {
            isBigTiff = false;
            firstDirectory = ReadUInt32(buffer[4..], bigEndian);
        }
        else if (magic == TiffFormat.BigTiffMagic)
        {
            isBigTiff = true;
            if (source.Length < TiffFormat.BigTiffHeaderLength)
                throw TiffFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The BigTIFF data has {source.Length} bytes; the 16-byte header does not fit."));

            source.Read(0, buffer);
            var offsetSize = ReadUInt16(buffer[4..], bigEndian);
            if (offsetSize != 8)
                throw TiffFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The BigTIFF declares an offset size of {offsetSize} bytes; 8 is expected."));

            var reserved = ReadUInt16(buffer[6..], bigEndian);
            if (reserved != 0)
                throw TiffFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The BigTIFF reserved header field is {reserved}; 0 is expected."));

            firstDirectory = ReadUInt64(buffer[8..], bigEndian);
        }
        else
        {
            throw TiffFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The TIFF magic number is {magic}; 42 (TIFF) or 43 (BigTIFF) is expected."));
        }

        if (firstDirectory == 0)
            throw TiffFormat.Invalid("The TIFF header declares no image file directory (the first IFD offset is 0).");

        return new TiffHeader(bigEndian, isBigTiff, firstDirectory);
    }

    /// <summary>Reads a 16-bit value in the byte order of the file.</summary>
    public ushort ReadUInt16(ReadOnlySpan<byte> source) => ReadUInt16(source, IsBigEndian);

    /// <summary>Reads a 32-bit value in the byte order of the file.</summary>
    public uint ReadUInt32(ReadOnlySpan<byte> source) => ReadUInt32(source, IsBigEndian);

    /// <summary>Reads a 64-bit value in the byte order of the file.</summary>
    public ulong ReadUInt64(ReadOnlySpan<byte> source) => ReadUInt64(source, IsBigEndian);

    /// <summary>Reads an offset in the byte order and the offset size of the file.</summary>
    public ulong ReadOffset(ReadOnlySpan<byte> source) => IsBigTiff ? ReadUInt64(source) : ReadUInt32(source);

    private static ushort ReadUInt16(ReadOnlySpan<byte> source, bool bigEndian)
        => bigEndian ? BinaryPrimitives.ReadUInt16BigEndian(source) : BinaryPrimitives.ReadUInt16LittleEndian(source);

    private static uint ReadUInt32(ReadOnlySpan<byte> source, bool bigEndian)
        => bigEndian ? BinaryPrimitives.ReadUInt32BigEndian(source) : BinaryPrimitives.ReadUInt32LittleEndian(source);

    private static ulong ReadUInt64(ReadOnlySpan<byte> source, bool bigEndian)
        => bigEndian ? BinaryPrimitives.ReadUInt64BigEndian(source) : BinaryPrimitives.ReadUInt64LittleEndian(source);
}
