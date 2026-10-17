using System.IO.Compression;

namespace Meziantou.Framework.WebPlatform.Css.Fonts;

/// <summary>
/// Decodes a WOFF 1.0 file to its sfnt data (https://www.w3.org/TR/WOFF/): the tables are compressed with zlib. A file that
/// does not conform to the requirements of the format for user agents (https://www.w3.org/TR/WOFF/#conform-invalid-mustreject)
/// is rejected.
/// </summary>
internal static class WoffDecoder
{
    private const int HeaderLength = 44;
    private const int TableEntryLength = 20;

    /// <summary>Returns the sfnt data of the WOFF file, or null when the file is invalid.</summary>
    public static byte[]? Decode(ReadOnlySpan<byte> data)
    {
        // https://www.w3.org/TR/WOFF/#WOFFHeader
        if (data.Length < HeaderLength || FontBinary.U32(data, 0) != OpenTypeTag.Woff || FontBinary.U32(data, 8) != data.Length)
            return null;

        var flavor = FontBinary.U32(data, 4);
        var tableCount = FontBinary.U16(data, 12);
        if (tableCount == 0 || FontBinary.U16(data, 14) != 0)
            return null;

        var directoryEnd = HeaderLength + (tableCount * TableEntryLength);
        if (directoryEnd > data.Length)
            return null;

        // The metadata and the private data blocks are inside of the file, after the tables, and aligned on 4 bytes
        if (!IsValidBlock(data, FontBinary.U32(data, 24), FontBinary.U32(data, 28), directoryEnd) || !IsValidBlock(data, FontBinary.U32(data, 36), FontBinary.U32(data, 40), directoryEnd))
            return null;

        // https://www.w3.org/TR/WOFF/#TableDirectory: the tables are sorted by tag, and do not overlap
        var ranges = new (long Start, long End)[tableCount];
        var tables = new List<(uint Tag, ReadOnlyMemory<byte> Data)>(tableCount);
        var totalLength = 0L;
        var previousTag = 0u;
        for (var i = 0; i < tableCount; i++)
        {
            var entry = HeaderLength + (i * TableEntryLength);
            var tag = FontBinary.U32(data, entry);
            var offset = FontBinary.U32(data, entry + 4);
            var compressedLength = FontBinary.U32(data, entry + 8);
            var originalLength = FontBinary.U32(data, entry + 12);
            if ((i > 0 && tag <= previousTag) || offset % 4 != 0 || offset < directoryEnd || !FontBinary.IsInRange(data, offset, compressedLength) || compressedLength > originalLength)
                return null;

            totalLength += originalLength;
            if (totalLength > FontFile.MaxDecodedLength)
                return null;

            previousTag = tag;
            ranges[i] = (offset, offset + compressedLength);
            var compressed = data.Slice((int)offset, (int)compressedLength);
            if (compressedLength == originalLength)
            {
                tables.Add((tag, compressed.ToArray()));
            }
            else if (Decompress(compressed, (int)originalLength) is { } table)
            {
                tables.Add((tag, table));
            }
            else
            {
                return null;
            }
        }

        Array.Sort(ranges);
        for (var i = 1; i < ranges.Length; i++)
        {
            if (ranges[i].Start < ranges[i - 1].End)
                return null;
        }

        return SfntWriter.WriteFont(flavor, tables);
    }

    private static bool IsValidBlock(ReadOnlySpan<byte> data, uint offset, uint length, int directoryEnd)
    {
        if (offset == 0)
            return length == 0;

        return offset % 4 == 0 && offset >= directoryEnd && FontBinary.IsInRange(data, offset, length);
    }

    /// <summary>Decompresses zlib data whose decompressed length must be the given length.</summary>
    private static byte[]? Decompress(ReadOnlySpan<byte> compressed, int length)
    {
        try
        {
            using var input = new MemoryStream(compressed.ToArray(), writable: false);
            using var zlib = new ZLibStream(input, CompressionMode.Decompress);

            // The output grows with the decompressed data, so that a small file cannot allocate the length it declares
            var output = new byte[Math.Min(length, Math.Max(compressed.Length * 4, 4096))];
            var written = 0;
            while (true)
            {
                if (written == output.Length)
                {
                    if (output.Length == length)
                        return zlib.ReadByte() == -1 ? output : null;

                    Array.Resize(ref output, (int)Math.Min(length, output.Length * 2L));
                }

                var read = zlib.Read(output, written, output.Length - written);
                if (read == 0)
                    return written == length ? output : null;

                written += read;
            }
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException)
        {
            // The compressed data is invalid (ZLibException is an IOException)
            return null;
        }
    }
}
