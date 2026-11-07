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
/// trailing Adler-32 checksum of the datastream matches the decompressed bytes. A raw deflate datastream (Apple CgBI
/// files) has no header and no checksum: the zlib inflater is given a header before it and the checksum of what it
/// decompressed after it, so that bytes after its final block do not match the checksum, then one more byte, which only
/// an inflater that did not reach the end of the datastream reads. A malformed or truncated stream yields
/// <see langword="null"/>: the ancillary payload is not adopted.
/// </remarks>
internal static class BoundedInflater
{
    private const int ChunkSize = 4096;

    /// <summary>Gets the zlib header given before a raw deflate datastream: deflate, 32 KiB window, no dictionary.</summary>
    internal static ReadOnlySpan<byte> SyntheticZlibHeader => [0x78, 0x01];

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

    /// <summary>Decompresses a raw deflate datastream (no zlib header, no Adler-32 trailer).</summary>
    /// <param name="compressed">The complete deflate datastream.</param>
    /// <param name="tracker">The per-input tracker charged with the decompressed size.</param>
    /// <returns>The decompressed bytes, or <see langword="null"/> when the datastream is malformed, truncated or followed by other bytes.</returns>
    /// <exception cref="ImageResourceLimitException">The decompressed size exceeds the metadata limit.</exception>
    public static byte[]? TryInflateRawDeflate(ReadOnlySpan<byte> compressed, InputResourceTracker tracker)
    {
        ArgumentNullException.ThrowIfNull(tracker);
        if (compressed.IsEmpty)
            return null;

        var input = ArrayPool<byte>.Shared.Rent(compressed.Length + SyntheticZlibHeader.Length);
        var chunk = ArrayPool<byte>.Shared.Rent(ChunkSize);
        try
        {
            using var source = new InflateFeedStream(input);
            source.Push(SyntheticZlibHeader, compressed);
            using var zlib = new ZLibStream(source, CompressionMode.Decompress);
            var output = new ArrayBufferWriter<byte>(Math.Min(compressed.Length * 2, 64 * 1024));
            int read;
            while ((read = ReadChunk(zlib, chunk)) > 0)
            {
                tracker.ChargeMetadataBytes(read);
                output.Write(chunk.AsSpan(0, read));
            }

            // A finished inflater stops reading its source: unread bytes follow the final block (more than its read-ahead)
            if (source.Available != 0)
                return null;

            // The checksum ends a complete datastream; bytes after its final block do not match it
            Span<byte> checksum = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(checksum, Adler32.Compute(output.WrittenSpan));
            source.Push(checksum);
            if (ReadChunk(zlib, chunk) != 0 || source.Available != 0)
                return null;

            // A finished inflater does not read what follows; one that did not reach the end of the datastream does
            source.Push([0]);
            if (ReadChunk(zlib, chunk) != 0 || source.Available == 0)
                return null;

            return output.WrittenSpan.ToArray();
        }
        catch (InvalidDataException)
        {
            return null;
        }
        catch (IOException)
        {
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
