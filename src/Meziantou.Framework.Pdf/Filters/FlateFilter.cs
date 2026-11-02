using System.IO.Compression;

namespace Meziantou.Framework.Pdf.Filters;

/// <summary>The <c>FlateDecode</c> filter (ISO 32000-2, 7.4.4): zlib (RFC 1950) and deflate (RFC 1951) data.</summary>
/// <remarks>
/// Readers decode what they can of a damaged stream: a stream that ends early, has a wrong checksum or no zlib header
/// gives the bytes decoded before the damage, as other readers do.
/// </remarks>
internal static class FlateFilter
{
    public static byte[] Decode(ReadOnlyMemory<byte> input, long limit)
    {
        var output = new BoundedOutput(limit, (int)Math.Min(Math.Max(256L, input.Length * 4L), int.MaxValue));

        // A zlib header (RFC 1950, 2.2): compression method 8 and a check value; some producers write raw deflate data
        var span = input.Span;
        var hasZlibHeader = span.Length >= 2 && (span[0] & 0x0F) == 8 && ((span[0] << 8) | span[1]) % 31 == 0;
        var data = hasZlibHeader ? input[2..] : input;

        // The data is read where it is: the arrays of the parser and of the previous filters
        using var source = new ReadOnlyMemoryStream(data);
        using var deflate = new DeflateStream(source, CompressionMode.Decompress);
        Span<byte> probe = stackalloc byte[1];
        try
        {
            while (true)
            {
                var free = output.GetFreeSpan();
                if (free.IsEmpty)
                {
                    // The data has reached the limit: it is exceeded if there is more
                    if (deflate.Read(probe) == 0)
                        break;

                    output.Write(probe[0]); // Throws the limit error
                }

                var read = deflate.Read(free);
                if (read == 0)
                    break;

                output.Advance(read);
            }
        }
        catch (InvalidDataException) when (output.Length > 0 || input.Length == 0)
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
