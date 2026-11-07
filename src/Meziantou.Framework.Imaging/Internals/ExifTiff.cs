using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Minimal, bounds-checked reader and patcher for TIFF-structured EXIF data (starting at the TIFF header, without the JPEG
/// <c>Exif\0\0</c> prefix). It implements the metadata contract of the library:
/// <list type="bullet">
/// <item>decoders read the orientation tag into the typed <see cref="ImageMetadata.Orientation"/> (never applied to pixels);</item>
/// <item>serialization rewrites the orientation tag from the authoritative typed value, reconciles existing pixel-dimension
/// tags with the image, and synthesizes a minimal block when an orientation must be stored and no EXIF exists;</item>
/// <item>geometry edits remove the thumbnail (IFD1), zeroing its bytes so stale content is never re-emitted.</item>
/// </list>
/// </summary>
/// <remarks>
/// Patching is done in place (or by appending a rebuilt IFD0) so that every other offset in the data — including offsets
/// private to maker notes — stays valid. Only IFD0, the IFD chain (IFD1...), the Exif, GPS and Interoperability IFDs are
/// traversed; every offset and count is bounds-checked and IFD cycles are rejected.
/// </remarks>
internal static class ExifTiff
{
    public const ushort ImageWidthTag = 0x0100;
    public const ushort ImageLengthTag = 0x0101;
    public const ushort StripOffsetsTag = 0x0111;
    public const ushort OrientationTag = 0x0112;
    public const ushort StripByteCountsTag = 0x0117;
    public const ushort JpegInterchangeFormatTag = 0x0201;
    public const ushort JpegInterchangeFormatLengthTag = 0x0202;
    public const ushort ExifIfdPointerTag = 0x8769;
    public const ushort GpsIfdPointerTag = 0x8825;
    public const ushort PixelXDimensionTag = 0xA002;
    public const ushort PixelYDimensionTag = 0xA003;
    public const ushort InteroperabilityIfdPointerTag = 0xA005;

    public const ushort TypeShort = 3;
    public const ushort TypeLong = 4;

    private const int HeaderSize = 8;
    private const int EntrySize = 12;
    private const int MaxChainedIfds = 16;

    /// <summary>Determines whether the data is structurally valid TIFF-structured EXIF.</summary>
    /// <param name="data">The data.</param>
    /// <returns><see langword="true"/> if the data can be parsed.</returns>
    public static bool IsValid(ReadOnlySpan<byte> data) => ExifStructure.TryParse(data, out _, out _);

    /// <summary>Reads the orientation tag of IFD0.</summary>
    /// <param name="data">The EXIF data.</param>
    /// <returns>
    /// The orientation, or <see langword="null"/> when the data is malformed, the tag is absent, or its value is not 1 to 8
    /// (decoders then keep <see cref="ExifOrientation.TopLeft"/>).
    /// </returns>
    public static ExifOrientation? ReadOrientation(ReadOnlySpan<byte> data)
    {
        if (!ExifStructure.TryParse(data, out var structure, out _))
            return null;

        var entry = ExifStructure.Find(structure.Ifd0, OrientationTag);
        if (entry is null || entry.Value.Count != 1 || !structure.TryReadUnsigned(data, entry.Value, out var value))
            return null;

        return value is >= 1 and <= 8 ? (ExifOrientation)value : null;
    }

    /// <summary>Determines whether IFD0 has an orientation tag, whatever its value.</summary>
    /// <param name="data">The EXIF data.</param>
    /// <returns><see langword="true"/> if the data is valid and IFD0 contains tag 0x0112.</returns>
    public static bool HasOrientationTag(ReadOnlySpan<byte> data)
        => ExifStructure.TryParse(data, out var structure, out _) && ExifStructure.Find(structure.Ifd0, OrientationTag) is not null;

    /// <summary>Reads the <c>PixelXDimension</c>/<c>PixelYDimension</c> tags of the Exif IFD.</summary>
    /// <param name="data">The EXIF data.</param>
    /// <returns>The dimensions; each is <see langword="null"/> when absent or unreadable.</returns>
    public static (uint? Width, uint? Height) ReadPixelDimensions(ReadOnlySpan<byte> data)
    {
        if (!ExifStructure.TryParse(data, out var structure, out _) || structure.ExifIfd is null)
            return (null, null);

        return (Read(structure, data, PixelXDimensionTag), Read(structure, data, PixelYDimensionTag));

        static uint? Read(ExifStructure structure, ReadOnlySpan<byte> data, ushort tag)
        {
            var entry = ExifStructure.Find(structure.ExifIfd!, tag);
            return entry is { Count: 1 } && structure.TryReadUnsigned(data, entry.Value, out var value) ? value : null;
        }
    }

    /// <summary>Determines whether the data contains a thumbnail directory (IFD1).</summary>
    /// <param name="data">The EXIF data.</param>
    /// <returns><see langword="true"/> if IFD0 links to another IFD.</returns>
    public static bool HasThumbnail(ReadOnlySpan<byte> data) => ExifStructure.TryParse(data, out var structure, out _) && structure.Ifd1 is not null;

    /// <summary>Creates a minimal little-endian EXIF block containing only the orientation tag.</summary>
    /// <param name="orientation">The orientation.</param>
    /// <returns>The TIFF-structured data (26 bytes).</returns>
    public static byte[] CreateMinimal(ExifOrientation orientation)
    {
        var result = new byte[HeaderSize + 2 + EntrySize + 4];
        "II*\0"u8.CopyTo(result);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4), HeaderSize);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(8), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(10), OrientationTag);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(12), TypeShort);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(14), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(18), (ushort)orientation);

        // Next IFD offset (bytes 22-25) stays 0
        return result;
    }

    /// <summary>Rewrites EXIF data for serialization or after a geometry change.</summary>
    /// <param name="data">The source EXIF data. It is not modified.</param>
    /// <param name="orientation">The authoritative orientation to store, or <see langword="null"/> to keep the stored tag unchanged.</param>
    /// <param name="pixelSize">The image dimensions to store in existing dimension tags, or <see langword="null"/> to keep them.</param>
    /// <param name="removeThumbnail"><see langword="true"/> to unlink IFD1 and zero its thumbnail bytes.</param>
    /// <param name="format">The format reported if the data is malformed.</param>
    /// <returns>The rewritten data (a new array).</returns>
    /// <exception cref="InvalidImageContentException">The data is not valid TIFF-structured EXIF.</exception>
    public static byte[] Rewrite(ReadOnlySpan<byte> data, ExifOrientation? orientation, Size? pixelSize, bool removeThumbnail, ImageFormat format = ImageFormat.Unknown)
    {
        if (!ExifStructure.TryParse(data, out var structure, out var error))
            throw new InvalidImageContentException("The EXIF profile is malformed: " + error, format);

        var result = data.ToArray();
        var length = result.Length;
        var span = result.AsSpan();

        if (pixelSize is { } size)
        {
            PatchDimension(span, structure, structure.Ifd0, ImageWidthTag, size.Width);
            PatchDimension(span, structure, structure.Ifd0, ImageLengthTag, size.Height);
            if (structure.ExifIfd is not null)
            {
                PatchDimension(span, structure, structure.ExifIfd, PixelXDimensionTag, size.Width);
                PatchDimension(span, structure, structure.ExifIfd, PixelYDimensionTag, size.Height);
            }
        }

        if (removeThumbnail && structure.Ifd1 is not null)
        {
            length = RemoveThumbnail(span, structure, length);
        }

        var orientationEntry = ExifStructure.Find(structure.Ifd0, OrientationTag);
        if (orientation is { } value)
        {
            if (orientationEntry is { } entry)
            {
                // The entry is 12 bytes whatever its original type: store SHORT, count 1, value inline
                structure.WriteUInt16(span, entry.EntryOffset + 2, TypeShort);
                structure.WriteUInt32(span, entry.EntryOffset + 4, 1);
                span.Slice(entry.EntryOffset + 8, 4).Clear();
                structure.WriteUInt16(span, entry.EntryOffset + 8, (ushort)value);
            }
            else
            {
                return AppendIfd0WithOrientation(result.AsSpan(0, length), structure, value);
            }
        }

        return length == result.Length ? result : result.AsSpan(0, length).ToArray();
    }

    private static void PatchDimension(Span<byte> data, ExifStructure structure, IfdInfo ifd, ushort tag, int value)
    {
        var found = ExifStructure.Find(ifd, tag);
        if (found is not { } entry)
            return;

        var type = value <= ushort.MaxValue && entry.Type == TypeShort ? TypeShort : TypeLong;
        structure.WriteUInt16(data, entry.EntryOffset + 2, type);
        structure.WriteUInt32(data, entry.EntryOffset + 4, 1);
        data.Slice(entry.EntryOffset + 8, 4).Clear();
        if (type == TypeShort)
        {
            structure.WriteUInt16(data, entry.EntryOffset + 8, (ushort)value);
        }
        else
        {
            structure.WriteUInt32(data, entry.EntryOffset + 8, (uint)value);
        }
    }

    private static int RemoveThumbnail(Span<byte> data, ExifStructure structure, int length)
    {
        var ifd1 = structure.Ifd1!;
        var ranges = new List<(int Start, int End)>();

        // Thumbnail payload: JPEG interchange format, or strips of an uncompressed thumbnail
        var jpegOffset = ExifStructure.Find(ifd1, JpegInterchangeFormatTag);
        var jpegLength = ExifStructure.Find(ifd1, JpegInterchangeFormatLengthTag);
        if (jpegOffset is { Count: 1 } offsetEntry && jpegLength is { Count: 1 } lengthEntry
            && structure.TryReadUnsigned(data, offsetEntry, out var start) && structure.TryReadUnsigned(data, lengthEntry, out var size))
        {
            AddRange(ranges, start, size, length);
        }

        var stripOffsets = ExifStructure.Find(ifd1, StripOffsetsTag);
        var stripCounts = ExifStructure.Find(ifd1, StripByteCountsTag);
        if (stripOffsets is { } offsetsEntry && stripCounts is { } countsEntry && offsetsEntry.Count == countsEntry.Count && offsetsEntry.Count <= 4096)
        {
            for (var i = 0; i < offsetsEntry.Count; i++)
            {
                if (structure.TryReadUnsigned(data, offsetsEntry, out var stripStart, i) && structure.TryReadUnsigned(data, countsEntry, out var stripSize, i))
                {
                    AddRange(ranges, stripStart, stripSize, length);
                }
            }
        }

        // Out-of-line values of IFD1, then the IFD1 structure itself (and any IFD chained after it)
        for (var current = ifd1; current is not null; current = current.Next)
        {
            foreach (var entry in current.Entries)
            {
                if (entry.ValueByteCount > 4)
                {
                    AddRange(ranges, (uint)entry.ValueOffset, (uint)entry.ValueByteCount, length);
                }
            }

            AddRange(ranges, (uint)current.Offset, (uint)(2 + (current.Entries.Count * EntrySize) + 4), length);
        }

        foreach (var (rangeStart, rangeEnd) in ranges)
        {
            data[rangeStart..rangeEnd].Clear();
        }

        // Unlink IFD1
        structure.WriteUInt32(data, structure.Ifd0.Offset + 2 + (structure.Ifd0.Entries.Count * EntrySize), 0);

        // Drop zeroed bytes at the end of the data (the usual layout stores the thumbnail last)
        ranges.Sort((x, y) => x.Start.CompareTo(y.Start));
        for (var i = ranges.Count - 1; i >= 0; i--)
        {
            if (ranges[i].End >= length && ranges[i].Start < length)
            {
                length = Math.Max(ranges[i].Start, HeaderSize);
            }
        }

        // Never cut into live structures: IFD0 and the Exif/GPS/Interop directories must remain inside the data
        return Math.Max(length, structure.LiveEnd);
    }

    private static void AddRange(List<(int Start, int End)> ranges, uint start, uint size, int length)
    {
        if (start < HeaderSize || start >= length || size == 0)
            return;

        var end = Math.Min((ulong)start + size, (ulong)length);
        ranges.Add(((int)start, (int)end));
    }

    private static byte[] AppendIfd0WithOrientation(ReadOnlySpan<byte> data, ExifStructure structure, ExifOrientation orientation)
    {
        var ifd0 = structure.Ifd0;
        var newOffset = (data.Length + 1) & ~1; // IFD offsets are word-aligned
        var entryCount = ifd0.Entries.Count + 1;
        var result = new byte[newOffset + 2 + (entryCount * EntrySize) + 4];
        data.CopyTo(result);
        var span = result.AsSpan();

        structure.WriteUInt32(span, 4, (uint)newOffset);
        structure.WriteUInt16(span, newOffset, (ushort)entryCount);
        var position = newOffset + 2;
        var inserted = false;
        foreach (var entry in ifd0.Entries)
        {
            if (!inserted && entry.Tag > OrientationTag)
            {
                WriteOrientationEntry(span, structure, position, orientation);
                position += EntrySize;
                inserted = true;
            }

            // Values stored out of line keep their offsets; inline values are copied with the entry
            data.Slice(entry.EntryOffset, EntrySize).CopyTo(span[position..]);
            position += EntrySize;
        }

        if (!inserted)
        {
            WriteOrientationEntry(span, structure, position, orientation);
            position += EntrySize;
        }

        // Keep the link to IFD1 as stored in the (possibly patched) original IFD0
        var nextOffsetPosition = ifd0.Offset + 2 + (ifd0.Entries.Count * EntrySize);
        data.Slice(nextOffsetPosition, 4).CopyTo(span[position..]);
        return result;

        static void WriteOrientationEntry(Span<byte> span, ExifStructure structure, int position, ExifOrientation orientation)
        {
            structure.WriteUInt16(span, position, OrientationTag);
            structure.WriteUInt16(span, position + 2, TypeShort);
            structure.WriteUInt32(span, position + 4, 1);
            structure.WriteUInt16(span, position + 8, (ushort)orientation);
        }
    }

    internal static int GetTypeSize(ushort type) => type switch
    {
        1 or 2 or 6 or 7 => 1, // BYTE, ASCII, SBYTE, UNDEFINED
        3 or 8 => 2, // SHORT, SSHORT
        4 or 9 or 11 or 13 => 4, // LONG, SLONG, FLOAT, IFD
        5 or 10 or 12 => 8, // RATIONAL, SRATIONAL, DOUBLE
        _ => 0, // Unknown types are skipped by readers (TIFF 6.0)
    };

    [StructLayout(LayoutKind.Auto)]
    internal readonly record struct IfdEntry(ushort Tag, ushort Type, uint Count, int EntryOffset, int ValueOffset, long ValueByteCount);

    internal sealed class IfdInfo(int offset, List<IfdEntry> entries)
    {
        public int Offset { get; } = offset;

        public List<IfdEntry> Entries { get; } = entries;

        public IfdInfo? Next { get; set; }

        public int End => Offset + 2 + (Entries.Count * EntrySize) + 4;
    }

    internal sealed class ExifStructure
    {
        private ExifStructure(bool bigEndian, IfdInfo ifd0)
        {
            BigEndian = bigEndian;
            Ifd0 = ifd0;
        }

        public bool BigEndian { get; }

        public IfdInfo Ifd0 { get; }

        public IfdInfo? Ifd1 => Ifd0.Next;

        public IfdInfo? ExifIfd { get; private set; }

        public IfdInfo? GpsIfd { get; private set; }

        public IfdInfo? InteroperabilityIfd { get; private set; }

        /// <summary>Gets the end of the last byte used by IFD0 and the Exif/GPS/Interop directories, including their out-of-line values.</summary>
        public int LiveEnd { get; private set; }

        public static bool TryParse(ReadOnlySpan<byte> data, [NotNullWhen(true)] out ExifStructure? structure, [NotNullWhen(false)] out string? error)
        {
            structure = null;
            if (data.Length < HeaderSize)
            {
                error = "the data is shorter than a TIFF header.";
                return false;
            }

            bool bigEndian;
            if (data[0] == 'I' && data[1] == 'I' && data[2] == 0x2A && data[3] == 0)
            {
                bigEndian = false;
            }
            else if (data[0] == 'M' && data[1] == 'M' && data[2] == 0 && data[3] == 0x2A)
            {
                bigEndian = true;
            }
            else
            {
                error = "the TIFF header is missing (expected 'II*\\0' or 'MM\\0*'; EXIF data must not include the 'Exif\\0\\0' prefix).";
                return false;
            }

            var visited = new HashSet<int>();
            var ifd0Offset = ReadUInt32(data, 4, bigEndian);
            if (!TryReadIfd(data, ifd0Offset, bigEndian, visited, out var ifd0, out error))
                return false;

            var result = new ExifStructure(bigEndian, ifd0);

            // IFD chain (IFD1 is the thumbnail directory)
            var current = ifd0;
            for (var i = 0; i < MaxChainedIfds; i++)
            {
                var next = ReadUInt32(data, current.Offset + 2 + (current.Entries.Count * EntrySize), bigEndian);
                if (next == 0)
                    break;

                if (!TryReadIfd(data, next, bigEndian, visited, out var nextIfd, out error))
                    return false;

                current.Next = nextIfd;
                current = nextIfd;
                if (i == MaxChainedIfds - 1)
                {
                    error = "the IFD chain is too long.";
                    return false;
                }
            }

            if (!result.TryReadSubIfd(data, ifd0, ExifIfdPointerTag, visited, out var exifIfd, out error))
                return false;

            result.ExifIfd = exifIfd;
            if (!result.TryReadSubIfd(data, ifd0, GpsIfdPointerTag, visited, out var gpsIfd, out error))
                return false;

            result.GpsIfd = gpsIfd;
            if (exifIfd is not null)
            {
                if (!result.TryReadSubIfd(data, exifIfd, InteroperabilityIfdPointerTag, visited, out var interopIfd, out error))
                    return false;

                result.InteroperabilityIfd = interopIfd;
            }

            foreach (var live in (ReadOnlySpan<IfdInfo?>)[ifd0, result.ExifIfd, result.GpsIfd, result.InteroperabilityIfd])
            {
                if (live is null)
                    continue;

                result.LiveEnd = Math.Max(result.LiveEnd, live.End);
                foreach (var entry in live.Entries)
                {
                    if (entry.ValueByteCount > 4)
                    {
                        result.LiveEnd = Math.Max(result.LiveEnd, (int)(entry.ValueOffset + entry.ValueByteCount));
                    }
                }
            }

            structure = result;
            error = null;
            return true;
        }

        public static IfdEntry? Find(IfdInfo ifd, ushort tag)
        {
            foreach (var entry in ifd.Entries)
            {
                if (entry.Tag == tag)
                    return entry;
            }

            return null;
        }

        /// <summary>Reads element <paramref name="index"/> of a BYTE, SHORT or LONG entry.</summary>
        public bool TryReadUnsigned(ReadOnlySpan<byte> data, IfdEntry entry, out uint value, int index = 0)
        {
            value = 0;
            if (index < 0 || index >= entry.Count)
                return false;

            var size = GetTypeSize(entry.Type);
            var position = (long)entry.ValueOffset + ((long)index * size);
            switch (entry.Type)
            {
                case 1:
                    value = data[(int)position];
                    return true;
                case TypeShort:
                    value = BigEndian ? BinaryPrimitives.ReadUInt16BigEndian(data[(int)position..]) : BinaryPrimitives.ReadUInt16LittleEndian(data[(int)position..]);
                    return true;
                case TypeLong:
                    value = ReadUInt32(data, (int)position, BigEndian);
                    return true;
                default:
                    return false;
            }
        }

        public void WriteUInt16(Span<byte> data, int offset, ushort value)
        {
            if (BigEndian)
            {
                BinaryPrimitives.WriteUInt16BigEndian(data[offset..], value);
            }
            else
            {
                BinaryPrimitives.WriteUInt16LittleEndian(data[offset..], value);
            }
        }

        public void WriteUInt32(Span<byte> data, int offset, uint value)
        {
            if (BigEndian)
            {
                BinaryPrimitives.WriteUInt32BigEndian(data[offset..], value);
            }
            else
            {
                BinaryPrimitives.WriteUInt32LittleEndian(data[offset..], value);
            }
        }

        private bool TryReadSubIfd(ReadOnlySpan<byte> data, IfdInfo parent, ushort pointerTag, HashSet<int> visited, out IfdInfo? ifd, [NotNullWhen(false)] out string? error)
        {
            ifd = null;
            error = null;
            if (Find(parent, pointerTag) is not { } pointer)
                return true;

            if (pointer.Count != 1 || (pointer.Type is not (TypeLong or 13)) || !TryReadUnsigned(data, pointer with { Type = TypeLong }, out var offset))
            {
                error = string.Create(CultureInfo.InvariantCulture, $"the IFD pointer tag 0x{pointerTag:X4} is invalid.");
                return false;
            }

            if (!TryReadIfd(data, offset, BigEndian, visited, out var result, out error))
                return false;

            ifd = result;
            return true;
        }

        private static bool TryReadIfd(ReadOnlySpan<byte> data, uint offset, bool bigEndian, HashSet<int> visited, [NotNullWhen(true)] out IfdInfo? ifd, [NotNullWhen(false)] out string? error)
        {
            ifd = null;
            if (offset < HeaderSize || offset > (uint)(data.Length - 2))
            {
                error = string.Create(CultureInfo.InvariantCulture, $"the IFD offset {offset} is outside the data.");
                return false;
            }

            var start = (int)offset;
            if (!visited.Add(start))
            {
                error = string.Create(CultureInfo.InvariantCulture, $"the IFD at offset {offset} is referenced more than once (cycle).");
                return false;
            }

            var count = bigEndian ? BinaryPrimitives.ReadUInt16BigEndian(data[start..]) : BinaryPrimitives.ReadUInt16LittleEndian(data[start..]);
            var end = (long)start + 2 + ((long)count * EntrySize) + 4;
            if (end > data.Length)
            {
                error = string.Create(CultureInfo.InvariantCulture, $"the IFD at offset {offset} ({count} entries) exceeds the data.");
                return false;
            }

            var entries = new List<IfdEntry>(count);
            for (var i = 0; i < count; i++)
            {
                var entryOffset = start + 2 + (i * EntrySize);
                var tag = bigEndian ? BinaryPrimitives.ReadUInt16BigEndian(data[entryOffset..]) : BinaryPrimitives.ReadUInt16LittleEndian(data[entryOffset..]);
                var type = bigEndian ? BinaryPrimitives.ReadUInt16BigEndian(data[(entryOffset + 2)..]) : BinaryPrimitives.ReadUInt16LittleEndian(data[(entryOffset + 2)..]);
                var valueCount = ReadUInt32(data, entryOffset + 4, bigEndian);
                var byteCount = (long)GetTypeSize(type) * valueCount;
                var valueOffset = entryOffset + 8;
                if (byteCount > 4)
                {
                    var outOfLine = ReadUInt32(data, entryOffset + 8, bigEndian);
                    if (outOfLine < HeaderSize || outOfLine + byteCount > data.Length)
                    {
                        error = string.Create(CultureInfo.InvariantCulture, $"the value of tag 0x{tag:X4} ({byteCount} bytes at offset {outOfLine}) is outside the data.");
                        return false;
                    }

                    valueOffset = (int)outOfLine;
                }

                entries.Add(new IfdEntry(tag, type, valueCount, entryOffset, valueOffset, byteCount));
            }

            ifd = new IfdInfo(start, entries);
            error = null;
            return true;
        }

        private static uint ReadUInt32(ReadOnlySpan<byte> data, int offset, bool bigEndian)
            => bigEndian ? BinaryPrimitives.ReadUInt32BigEndian(data[offset..]) : BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]);
    }
}
