// The "LZMA alone" format of the LZMA SDK (.lzma), which is in the public domain.
using System.Buffers.Binary;

namespace Meziantou.Framework.Compression.Internals.Lzma;

/// <summary>The .lzma format: a 13-byte header (properties, dictionary size, uncompressed size or -1), then the LZMA data.</summary>
internal static class LzmaAloneFormat
{
    public const int HeaderSize = 13;

    /// <summary>Reads the header and returns a decoder of the data that follows it in <paramref name="input"/>.</summary>
    public static LzmaDecoderStream CreateDecoder(LzmaInputBuffer input, long maxMemoryUsage)
    {
        Span<byte> header = stackalloc byte[HeaderSize];
        input.ReadExactly(header);
        if (!LzmaDecoder.TryDecodeProperties(header[0], out var lc, out var lp, out var pb))
            throw new InvalidDataException("The LZMA data is corrupt: the properties are invalid.");

        var dictionarySize = BinaryPrimitives.ReadUInt32LittleEndian(header[1..]);
        var size = BinaryPrimitives.ReadUInt64LittleEndian(header[5..]);
        long outputLength;
        if (size == ulong.MaxValue)
        {
            outputLength = -1;
        }
        else if (size > long.MaxValue)
        {
            throw new InvalidDataException("The LZMA data is corrupt: the uncompressed size is invalid.");
        }
        else
        {
            outputLength = (long)size;
        }

        return new LzmaDecoderStream(input, lc, lp, pb, dictionarySize, outputLength, maxMemoryUsage);
    }

    /// <summary>Writes the header (with an unknown size) and returns an encoder that ends the data with an end marker.</summary>
    public static LzmaEncoderStream CreateEncoder(Stream output, LzmaEncoderSettings settings)
    {
        var encoder = new LzmaEncoderStream(output, settings, writeEndMarker: true, leaveOpen: true);
        Span<byte> header = stackalloc byte[HeaderSize];
        encoder.Properties.CopyTo(header);
        BinaryPrimitives.WriteUInt64LittleEndian(header[5..], ulong.MaxValue);
        output.Write(header);
        return encoder;
    }
}
