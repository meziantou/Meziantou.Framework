using System.IO.Compression;

namespace Meziantou.Framework.Pdf.Filters;

/// <summary>The <c>FlateDecode</c> filter (ISO 32000-2, 7.4.4): zlib (RFC 1950) and deflate (RFC 1951) data.</summary>
/// <remarks>
/// Readers decode what they can of a damaged stream: a stream that ends early, has a wrong checksum or no zlib header
/// gives the bytes decoded before the damage, as other readers do.
/// </remarks>
internal static class FlateFilter
{
    public static byte[] Decode(ReadOnlySpan<byte> input, long limit)
    {
        var output = new BoundedOutput(limit, Math.Max(256, input.Length * 4));
        var data = input.ToArray();

        // A zlib header (RFC 1950, 2.2): compression method 8 and a check value; some producers write raw deflate data
        var hasZlibHeader = data.Length >= 2 && (data[0] & 0x0F) == 8 && ((data[0] << 8) | data[1]) % 31 == 0;
        var start = hasZlibHeader ? 2 : 0;
        using var source = new MemoryStream(data, start, data.Length - start, writable: false);
        using var deflate = new DeflateStream(source, CompressionMode.Decompress);
        var chunk = new byte[16384];
        try
        {
            int read;
            while ((read = deflate.Read(chunk)) > 0)
            {
                output.Write(chunk.AsSpan(0, read));
            }
        }
        catch (InvalidDataException) when (output.Length > 0 || data.Length == 0)
        {
            // Damaged data: keep what was decoded
        }
        catch (InvalidDataException ex)
        {
            throw new InvalidPdfContentException("The FlateDecode data is not valid", ex);
        }

        return output.ToArray();
    }

    public static byte[] Encode(ReadOnlySpan<byte> input, CompressionLevel level = CompressionLevel.Optimal)
    {
        using var result = new MemoryStream(Math.Max(64, input.Length / 2));
        using (var zlib = new ZLibStream(result, level, leaveOpen: true))
        {
            zlib.Write(input);
        }

        return result.ToArray();
    }
}
