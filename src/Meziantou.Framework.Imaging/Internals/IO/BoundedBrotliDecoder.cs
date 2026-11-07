using System.Buffers;
using System.IO.Compression;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Decompresses a complete in-memory Brotli stream (the <c>brob</c> boxes of JPEG XL), charging every decompressed byte to
/// <see cref="ImageResourceLimits.MaxMetadataBytes"/> <em>before</em> retaining it, so a decompression bomb fails with
/// <see cref="ImageResourceLimitException"/> instead of being buffered.
/// </summary>
/// <remarks>A malformed or truncated stream yields <see langword="null"/>: the ancillary payload is not adopted.</remarks>
internal static class BoundedBrotliDecoder
{
    private const int ChunkSize = 4096;

    /// <summary>Decompresses a Brotli stream.</summary>
    /// <param name="compressed">The complete stream.</param>
    /// <param name="tracker">The per-input tracker charged with the decompressed size.</param>
    /// <returns>The decompressed bytes, or <see langword="null"/> when the stream is malformed or truncated.</returns>
    /// <exception cref="ImageResourceLimitException">The decompressed size exceeds the metadata limit.</exception>
    public static byte[]? TryDecompress(ReadOnlySpan<byte> compressed, InputResourceTracker tracker)
    {
        ArgumentNullException.ThrowIfNull(tracker);
        var chunk = ArrayPool<byte>.Shared.Rent(ChunkSize);
        try
        {
            using var decoder = new BrotliDecoder();
            var output = new ArrayBufferWriter<byte>(Math.Min(Math.Max(compressed.Length, 1) * 2, 64 * 1024));
            while (true)
            {
                var status = decoder.Decompress(compressed, chunk, out var consumed, out var written);
                if (written > 0)
                {
                    tracker.ChargeMetadataBytes(written);
                    output.Write(chunk.AsSpan(0, written));
                }

                compressed = compressed[consumed..];
                switch (status)
                {
                    case OperationStatus.Done:
                        // Bytes after the end of the stream are not part of it: the box is malformed
                        return compressed.IsEmpty ? output.WrittenSpan.ToArray() : null;

                    case OperationStatus.DestinationTooSmall:
                        continue;

                    default:
                        // The stream is truncated or invalid
                        return null;
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(chunk);
        }
    }
}
