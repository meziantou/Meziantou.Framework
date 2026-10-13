using System.Buffers.Binary;
using System.IO.Compression;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>Expands a WOFF 1.0 file to the font it wraps.</summary>
/// <remarks>
/// <para>
/// W3C Recommendation "WOFF File Format 1.0". A WOFF file is the table directory of a font whose tables are each stored
/// as they are or as a zlib stream. The font is rebuilt with its tables in the order of the file, each at a multiple of
/// four bytes.
/// </para>
/// <para>
/// The size of the font is the sum of the lengths the directory declares: it is checked against
/// <see cref="ImageResourceLimits.MaxFontBytes"/> before anything is allocated, and a table that does not decompress to
/// exactly its declared length is an error, so a small file cannot expand to more than the limit. The metadata and the
/// private data of the file are not read.
/// </para>
/// </remarks>
internal static class WoffReader
{
    private const int HeaderLength = 44;
    private const int EntryLength = 20;

    public static byte[] Decode(byte[] file, ImageResourceLimits limits)
    {
        ReadOnlySpan<byte> data = file;
        var header = FontBinary.Slice(data, 0, HeaderLength);
        var flavor = FontBinary.UInt32(header, 4);
        if (flavor == FontTags.Collection)
            throw FontErrors.Invalid("a WOFF file cannot contain a font collection.");

        var tableCount = FontBinary.UInt16(header, 12);
        var entries = FontBinary.Slice(data, HeaderLength, (long)tableCount * EntryLength);

        // The size of the font, before any allocation.
        var total = SfntWriter.GetDirectoryLength(tableCount);
        for (var i = 0; i < tableCount; i++)
        {
            var entry = entries.Slice(i * EntryLength, EntryLength);
            var tag = FontBinary.UInt32(entry, 0);
            var offset = FontBinary.UInt32(entry, 4);
            var compressedLength = FontBinary.UInt32(entry, 8);
            var originalLength = FontBinary.UInt32(entry, 12);
            if ((long)offset + compressedLength > data.Length)
                throw FontErrors.InvalidTable(tag, "lies outside the file.");

            if (compressedLength > originalLength)
                throw FontErrors.InvalidTable(tag, "is declared longer compressed than expanded.");

            total += SfntWriter.Pad(originalLength);
        }

        if (total > limits.MaxFontBytes)
            throw new ImageResourceLimitException(ImageResourceLimitKind.FontBytes, limits.MaxFontBytes, total);

        if (total > CheckedSizes.MaxBufferLength)
            throw new ImageResourceLimitException(ImageResourceLimitKind.FontBytes, CheckedSizes.MaxBufferLength, total);

        var font = new byte[total];
        SfntWriter.WriteHeader(font, flavor, tableCount);
        var position = (int)SfntWriter.GetDirectoryLength(tableCount);
        for (var i = 0; i < tableCount; i++)
        {
            var entry = entries.Slice(i * EntryLength, EntryLength);
            var tag = BinaryPrimitives.ReadUInt32BigEndian(entry);
            var offset = (int)BinaryPrimitives.ReadUInt32BigEndian(entry[4..]);
            var compressedLength = (int)BinaryPrimitives.ReadUInt32BigEndian(entry[8..]);
            var originalLength = (int)BinaryPrimitives.ReadUInt32BigEndian(entry[12..]);
            var destination = font.AsSpan(position, originalLength);
            if (compressedLength == originalLength)
            {
                data.Slice(offset, originalLength).CopyTo(destination);
            }
            else
            {
                Inflate(file, offset, compressedLength, destination, tag);
            }

            SfntWriter.WriteRecord(font.AsSpan(SfntWriter.HeaderLength + (i * SfntWriter.RecordLength)), tag, BinaryPrimitives.ReadUInt32BigEndian(entry[16..]), position, originalLength);
            position += (int)SfntWriter.Pad(originalLength);
        }

        return font;
    }

    /// <summary>Expands a zlib stream whose expanded length is known, and checks that it is exactly that long.</summary>
    private static void Inflate(byte[] file, int offset, int length, Span<byte> destination, uint tag)
    {
        try
        {
            using var input = new MemoryStream(file, offset, length, writable: false);
            using var zlib = new ZLibStream(input, CompressionMode.Decompress);
            var written = 0;
            while (written < destination.Length)
            {
                var read = zlib.Read(destination[written..]);
                if (read <= 0)
                    throw FontErrors.InvalidTable(tag, "has compressed data that ends before the declared length of the table.");

                written += read;
            }

            Span<byte> extra = stackalloc byte[1];
            if (zlib.Read(extra) != 0)
                throw FontErrors.InvalidTable(tag, "has compressed data that is longer than the declared length of the table.");
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException)
        {
            // The stream reads from memory: an I/O error is an error of the compressed data that the decompressor
            // reports that way, such as a stream that asks for a preset dictionary.
            throw FontErrors.InvalidTable(tag, "has malformed compressed data.", exception);
        }

        // ZLibStream reports a stream cut before its checksum as a clean end: verify the checksum, the last four bytes
        // of a zlib stream (RFC 1950).
        if (length < 6 || BoundedInflater.ComputeAdler32(destination) != BinaryPrimitives.ReadUInt32BigEndian(file.AsSpan(offset + length - 4, 4)))
            throw FontErrors.InvalidTable(tag, "has compressed data that is truncated or corrupted.");
    }
}
