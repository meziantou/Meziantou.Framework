using System.Buffers;
using System.Buffers.Binary;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Writes PNG chunks (W3C PNG specification, section 5.3): a 4-byte big-endian length, the 4-byte type, the data and the
/// CRC-32 of the type and the data. A chunk can be written at once (<see cref="Write"/>) or in parts whose total length is
/// known up front (<see cref="Begin"/>, <c>Append</c>, <see cref="End"/>), so that no intermediate copy of a
/// multi-part payload (keyword, separators, text) is needed.
/// </summary>
internal static class PngChunkWriter
{
    /// <summary>The largest chunk data length (2^31 - 1, the PNG four-byte unsigned integer limit).</summary>
    public const int MaxDataLength = int.MaxValue;

    public const uint Ihdr = 0x49484452;
    public const uint Idat = 0x49444154;
    public const uint Iend = 0x49454E44;
    public const uint Phys = 0x70485973;
    public const uint Iccp = 0x69434350;
    public const uint Exif = 0x65584966;
    public const uint Text = 0x74455874;
    public const uint Ztxt = 0x7A545874;
    public const uint Itxt = 0x69545874;
    public const uint Actl = 0x6163544C;
    public const uint Fctl = 0x6663544C;
    public const uint Fdat = 0x66644154;

    /// <summary>Writes the 8-byte PNG signature.</summary>
    public static void WriteSignature(IBufferWriter<byte> output)
    {
        var signature = PngCodec.Signature;
        signature.CopyTo(output.GetSpan(signature.Length));
        output.Advance(signature.Length);
    }

    /// <summary>Writes a complete chunk.</summary>
    /// <param name="output">The output.</param>
    /// <param name="type">The chunk type (four ASCII letters, big-endian).</param>
    /// <param name="data">The chunk data.</param>
    public static void Write(IBufferWriter<byte> output, uint type, ReadOnlySpan<byte> data)
    {
        var crc = Begin(output, type, data.Length);
        Append(output, ref crc, data);
        End(output, crc);
    }

    /// <summary>Writes the length and type of a chunk.</summary>
    /// <param name="output">The output.</param>
    /// <param name="type">The chunk type.</param>
    /// <param name="length">The total length of the data that <c>Append</c> will write.</param>
    /// <returns>The running CRC, to pass to <c>Append</c> and <see cref="End"/>.</returns>
    public static uint Begin(IBufferWriter<byte> output, uint type, long length)
    {
        if (length is < 0 or > MaxDataLength)
            throw new UnsupportedImageFeatureException(string.Create(CultureInfo.InvariantCulture, $"A PNG chunk stores at most {MaxDataLength} bytes; the data is {length} bytes."), ImageFormat.Png, "PNG chunk size");

        var header = output.GetSpan(8);
        BinaryPrimitives.WriteUInt32BigEndian(header, (uint)length);
        BinaryPrimitives.WriteUInt32BigEndian(header[4..], type);
        var crc = Crc32.Update(Crc32.Initial, header.Slice(4, 4));
        output.Advance(8);
        return crc;
    }

    /// <summary>Writes a part of the chunk data.</summary>
    public static void Append(IBufferWriter<byte> output, ref uint crc, ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
            return;

        data.CopyTo(output.GetSpan(data.Length));
        output.Advance(data.Length);
        crc = Crc32.Update(crc, data);
    }

    /// <summary>Writes one byte of the chunk data.</summary>
    public static void Append(IBufferWriter<byte> output, ref uint crc, byte value) => Append(output, ref crc, [value]);

    /// <summary>Writes the CRC that ends the chunk.</summary>
    public static void End(IBufferWriter<byte> output, uint crc)
    {
        BinaryPrimitives.WriteUInt32BigEndian(output.GetSpan(4), Crc32.Finish(crc));
        output.Advance(4);
    }
}
