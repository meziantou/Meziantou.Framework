using System.Buffers;
using System.Buffers.Binary;
using System.IO.Compression;
using Meziantou.Framework.Hashing;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Decompresses a complete in-memory zlib datastream (PNG <c>iCCP</c>, <c>zTXt</c>, compressed <c>iTXt</c>), charging every
/// decompressed byte to <see cref="ImageResourceLimits.MaxMetadataBytes"/> <em>before</em> retaining it, so a decompression
/// bomb fails with <see cref="ImageResourceLimitException"/> instead of being buffered.
/// </summary>
/// <remarks>
/// The BCL <see cref="ZLibStream"/> reports a truncated stream as a clean end, so the result is accepted only if the
/// trailing Adler-32 checksum of the datastream matches the decompressed bytes. A malformed or truncated stream yields
/// <see langword="null"/>: the ancillary payload is not adopted.
/// </remarks>
internal static class BoundedInflater
{
    private const int ChunkSize = 4096;

    /// <summary>Decompresses a zlib datastream.</summary>
    /// <param name="compressed">The complete zlib datastream (header, deflate data, Adler-32).</param>
    /// <param name="tracker">The per-input tracker charged with the decompressed size.</param>
    /// <returns>The decompressed bytes, or <see langword="null"/> when the datastream is malformed or truncated.</returns>
    /// <exception cref="ImageResourceLimitException">The decompressed size exceeds the metadata limit.</exception>
    public static byte[]? TryInflateZlib(ReadOnlySpan<byte> compressed, InputResourceTracker tracker)
    {
        ArgumentNullException.ThrowIfNull(tracker);
        if (compressed.Length < 6)
            return null;

        var input = ArrayPool<byte>.Shared.Rent(compressed.Length);
        var chunk = ArrayPool<byte>.Shared.Rent(ChunkSize);
        try
        {
            compressed.CopyTo(input);
            using var source = new MemoryStream(input, 0, compressed.Length, writable: false);
            using var zlib = new ZLibStream(source, CompressionMode.Decompress);
            var output = new ArrayBufferWriter<byte>(Math.Min(compressed.Length * 2, 64 * 1024));
            int read;
            while ((read = ReadChunk(zlib, chunk)) > 0)
            {
                tracker.ChargeMetadataBytes(read);
                output.Write(chunk.AsSpan(0, read));
            }

            var expectedAdler = BinaryPrimitives.ReadUInt32BigEndian(compressed[^4..]);
            if (Adler32.Compute(output.WrittenSpan) != expectedAdler)
                return null;

            return output.WrittenSpan.ToArray();
        }
        catch (InvalidDataException)
        {
            return null;
        }
        catch (IOException)
        {
            // The source is in memory: an IOException is the BCL's (internal) ZLibException for zlib error codes that are not
            // data errors, for example a preset dictionary (FDICT), which PNG forbids
            return null;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(chunk);
            ArrayPool<byte>.Shared.Return(input);
        }
    }

    private static int ReadChunk(ZLibStream zlib, byte[] chunk) => zlib.Read(chunk, 0, ChunkSize);
}
