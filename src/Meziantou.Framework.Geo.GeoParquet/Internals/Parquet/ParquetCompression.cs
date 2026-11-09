using System.Buffers.Binary;
using System.IO.Compression;

namespace Meziantou.Framework.Geo.Internals.Parquet;

/// <summary>
/// Decompresses the pages of a Parquet file. Snappy and LZ4 are decoded here; GZIP and Brotli by .NET, and Zstandard by
/// .NET from .NET 11, else by the decoder of Meziantou.Framework.Imaging. LZO is not supported.
/// </summary>
internal static class ParquetCompression
{
    public const int Uncompressed = 0;
    public const int Snappy = 1;
    public const int Gzip = 2;
    public const int Lzo = 3;
    public const int Brotli = 4;
    public const int Lz4Hadoop = 5;
    public const int Zstd = 6;
    public const int Lz4Raw = 7;

    // The longest output of a byte of input: a 3-byte copy of Snappy writes 64 bytes, a 255 byte of LZ4 adds 255 to a length
    private const int MaxSnappyRatio = 22;
    private const int MaxLz4Ratio = 256;

    private const int StreamChunkLength = 64 * 1024;

#if !NET11_0_OR_GREATER
    // An RLE block of Zstandard (a 3-byte header and 1 byte) writes up to 128 KB
    private const int MaxZstdRatio = 32 * 1024;

    // The decoder holds about 700 KB of tables; it resets them at every frame, so a thread reuses it from page to page
    [ThreadStatic]
    private static ZstdDecoder? s_zstdDecoder;
#endif

    /// <summary>
    /// Decompresses data whose decompressed length is known, as the length of a page is. The length comes from the file,
    /// so the memory is allocated as the data is decompressed, or checked against the ratio of the codec.
    /// </summary>
    public static byte[] Decompress(int codec, byte[] buffer, int offset, int length, int decompressedLength)
    {
        var input = buffer.AsSpan(offset, length);
        switch (codec)
        {
            case Uncompressed:
                if (input.Length != decompressedLength)
                    throw new FormatException("The size of an uncompressed page of the Parquet file does not match its header.");

                return input.ToArray();

            case Snappy:
                CheckRatio(length, decompressedLength, MaxSnappyRatio);
                var snappy = new byte[decompressedLength];
                DecompressSnappy(input, snappy);
                return snappy;

            case Gzip:
                return DecompressStream(new GZipStream(new MemoryStream(buffer, offset, length, writable: false), CompressionMode.Decompress), decompressedLength);

            case Brotli:
                return DecompressStream(new BrotliStream(new MemoryStream(buffer, offset, length, writable: false), CompressionMode.Decompress), decompressedLength);

            case Zstd:
#if NET11_0_OR_GREATER
                return DecompressStream(new ZstandardStream(new MemoryStream(buffer, offset, length, writable: false), CompressionMode.Decompress), decompressedLength);
#else
                CheckRatio(length, decompressedLength, MaxZstdRatio);
                var zstd = new byte[decompressedLength];
                try
                {
                    (s_zstdDecoder ??= new ZstdDecoder()).Decompress(input, zstd);
                }
                catch (InvalidDataException ex)
                {
                    throw new FormatException("A compressed page of the Parquet file is not valid: " + ex.Message, ex);
                }

                return zstd;
#endif

            case Lz4Raw:
                CheckRatio(length, decompressedLength, MaxLz4Ratio);
                var lz4 = new byte[decompressedLength];
                if (DecompressLz4Block(input, lz4) != lz4.Length)
                    throw new FormatException("A page of the Parquet file is shorter than its header declares.");

                return lz4;

            case Lz4Hadoop:
                // The Hadoop framing (big-endian decompressed and compressed lengths before each block), else a raw block, as Arrow reads it
                CheckRatio(length, decompressedLength, MaxLz4Ratio);
                var hadoop = new byte[decompressedLength];
                if (!TryDecompressLz4Hadoop(input, hadoop) && DecompressLz4Block(input, hadoop) != hadoop.Length)
                    throw new FormatException("A page of the Parquet file is shorter than its header declares.");

                return hadoop;

            case Lzo:
                throw new NotSupportedException("The Parquet file is compressed with LZO, which is not supported.");

            default:
                throw new FormatException($"The compression codec {codec} of the Parquet file is not valid.");
        }
    }

    private static void CheckRatio(int length, int decompressedLength, int ratio)
    {
        if (decompressedLength > ((long)length * ratio) + 64)
            throw new FormatException("The size of a decompressed page of the Parquet file is larger than its compressed data can hold.");
    }

    private static byte[] DecompressStream(Stream stream, int decompressedLength)
    {
        try
        {
            return DecompressStreamCore(stream, decompressedLength);
        }
        catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException or IOException)
        {
            // GZipStream reports invalid data with an InvalidDataException, BrotliStream with an InvalidOperationException
            throw new FormatException("A compressed page of the Parquet file is not valid: " + ex.Message, ex);
        }
    }

    private static byte[] DecompressStreamCore(Stream stream, int decompressedLength)
    {
        using (stream)
        {
            var result = new byte[Math.Min(decompressedLength, StreamChunkLength)];
            var written = 0;
            while (true)
            {
                var read = stream.Read(result, written, result.Length - written);
                if (read is 0)
                    break;

                written += read;
                if (written == result.Length)
                {
                    if (written == decompressedLength)
                    {
                        if (stream.ReadByte() >= 0)
                            throw new FormatException("A decompressed page of the Parquet file is longer than its header declares.");

                        break;
                    }

                    Array.Resize(ref result, (int)Math.Min(decompressedLength, result.Length * 2L));
                }
            }

            if (written != decompressedLength)
                throw new FormatException("A decompressed page of the Parquet file is shorter than its header declares.");

            return result;
        }
    }

    // https://github.com/google/snappy/blob/main/format_description.txt
    private static void DecompressSnappy(ReadOnlySpan<byte> input, Span<byte> output)
    {
        var position = 0;
        ulong length = 0;
        for (var shift = 0; ; shift += 7)
        {
            if (position >= input.Length || shift > 28)
                throw InvalidData();

            var b = input[position++];
            length |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) is 0)
                break;
        }

        if (length != (ulong)output.Length)
            throw new FormatException("The size of a decompressed page of the Parquet file does not match its header.");

        var written = 0;
        while (position < input.Length)
        {
            var tag = input[position++];
            int copyLength;
            int copyOffset;
            switch (tag & 3)
            {
                case 0: // Literal
                    var literalLength = tag >> 2;
                    if (literalLength >= 60)
                    {
                        var byteCount = literalLength - 59;
                        if (position + byteCount > input.Length)
                            throw InvalidData();

                        literalLength = 0;
                        for (var i = 0; i < byteCount; i++)
                        {
                            literalLength |= input[position + i] << (8 * i);
                        }

                        position += byteCount;
                        if (literalLength < 0)
                            throw InvalidData();
                    }

                    literalLength++;
                    if (literalLength > input.Length - position || literalLength > output.Length - written)
                        throw InvalidData();

                    input.Slice(position, literalLength).CopyTo(output[written..]);
                    position += literalLength;
                    written += literalLength;
                    continue;

                case 1: // Copy with a 1-byte offset
                    if (position >= input.Length)
                        throw InvalidData();

                    copyLength = 4 + ((tag >> 2) & 7);
                    copyOffset = ((tag >> 5) << 8) | input[position++];
                    break;

                case 2: // Copy with a 2-byte offset
                    if (position + 2 > input.Length)
                        throw InvalidData();

                    copyLength = (tag >> 2) + 1;
                    copyOffset = BinaryPrimitives.ReadUInt16LittleEndian(input[position..]);
                    position += 2;
                    break;

                default: // Copy with a 4-byte offset
                    if (position + 4 > input.Length)
                        throw InvalidData();

                    copyLength = (tag >> 2) + 1;
                    copyOffset = BinaryPrimitives.ReadInt32LittleEndian(input[position..]);
                    position += 4;
                    break;
            }

            Copy(output, ref written, copyOffset, copyLength);
        }

        if (written != output.Length)
            throw InvalidData();
    }

    private static bool TryDecompressLz4Hadoop(ReadOnlySpan<byte> input, Span<byte> output)
    {
        var position = 0;
        var written = 0;
        while (position < input.Length)
        {
            if (input.Length - position < 8)
                return false;

            var decompressedLength = BinaryPrimitives.ReadUInt32BigEndian(input[position..]);
            var compressedLength = BinaryPrimitives.ReadUInt32BigEndian(input[(position + 4)..]);
            position += 8;
            if (compressedLength > (uint)(input.Length - position) || decompressedLength > (uint)(output.Length - written))
                return false;

            try
            {
                if (DecompressLz4Block(input.Slice(position, (int)compressedLength), output.Slice(written, (int)decompressedLength)) != decompressedLength)
                    return false;
            }
            catch (FormatException)
            {
                return false;
            }

            position += (int)compressedLength;
            written += (int)decompressedLength;
        }

        return written == output.Length;
    }

    // https://github.com/lz4/lz4/blob/dev/doc/lz4_Block_format.md
    private static int DecompressLz4Block(ReadOnlySpan<byte> input, Span<byte> output)
    {
        var position = 0;
        var written = 0;
        while (position < input.Length)
        {
            var token = input[position++];
            var literalLength = ReadLz4Length(input, ref position, token >> 4);
            if (literalLength > input.Length - position || literalLength > output.Length - written)
                throw InvalidData();

            input.Slice(position, literalLength).CopyTo(output[written..]);
            position += literalLength;
            written += literalLength;

            // The last sequence has literals only
            if (position == input.Length)
                break;

            if (position + 2 > input.Length)
                throw InvalidData();

            var offset = BinaryPrimitives.ReadUInt16LittleEndian(input[position..]);
            position += 2;
            var matchLength = ReadLz4Length(input, ref position, token & 0x0F) + 4;
            Copy(output, ref written, offset, matchLength);
        }

        return written;
    }

    private static int ReadLz4Length(ReadOnlySpan<byte> input, ref int position, int length)
    {
        if (length is not 15)
            return length;

        while (true)
        {
            if (position >= input.Length)
                throw InvalidData();

            var b = input[position++];
            length += b;
            if (length < 0)
                throw InvalidData();

            if (b is not 255)
                return length;
        }
    }

    // Copies from earlier in the output; the source and the destination may overlap, which repeats a pattern
    private static void Copy(Span<byte> output, ref int written, int offset, int length)
    {
        if (offset <= 0 || offset > written || length > output.Length - written)
            throw InvalidData();

        var source = written - offset;
        if (offset >= length)
        {
            output.Slice(source, length).CopyTo(output[written..]);
        }
        else
        {
            for (var i = 0; i < length; i++)
            {
                output[written + i] = output[source + i];
            }
        }

        written += length;
    }

    private static FormatException InvalidData() => new("A compressed page of the Parquet file is not valid.");
}
