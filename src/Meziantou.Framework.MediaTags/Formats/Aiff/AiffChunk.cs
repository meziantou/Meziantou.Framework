using System.Buffers.Binary;
using Meziantou.Framework.MediaTags.Internals;

namespace Meziantou.Framework.MediaTags.Formats.Aiff;

internal sealed class AiffChunk
{
    /// <summary>The maximum number of chunks read from one file.</summary>
    /// <remarks>
    /// An empty chunk costs 8 bytes in the file but a retained object here, so an unbounded count lets a small
    /// file force a disproportionate allocation. Real files hold a handful of chunks.
    /// </remarks>
    public const int MaxCount = 8192;

    /// <summary>The chunks a tag can be read from, and therefore the only ones whose content is buffered.</summary>
    public static readonly string[] TagChunkIds = ["ID3 ", "id3 ", "NAME", "AUTH", "ANNO", "(c) ", "ISRC"];

    public string Id { get; set; } = "";
    public int Size { get; set; }
    public long DataPosition { get; set; }
    public byte[]? Data { get; set; }

    /// <summary>
    /// Reads the chunks between the current position and <paramref name="endPosition"/>.
    /// </summary>
    /// <param name="complete">
    /// <see langword="true"/> when every byte of the container was accounted for. A writer must not rebuild a
    /// file from an incomplete parse: the chunks that were not reached, <c>SSND</c> included, would be dropped.
    /// </param>
    public static List<AiffChunk> ReadChunks(Stream stream, long endPosition, out bool complete)
    {
        var chunks = new List<AiffChunk>();
        Span<byte> header = stackalloc byte[8];
        complete = false;

        while (stream.Position + 8 <= endPosition)
        {
            if (chunks.Count >= MaxCount)
                throw new InvalidDataException($"The file declares more than {MaxCount} AIFF chunks.");

            if (stream.ReadAtLeast(header, 8, throwOnEndOfStream: false) < 8)
                return chunks;

            var chunkId = Encoding.ASCII.GetString(header[..4]);
            var chunkSize = BinaryPrimitives.ReadInt32BigEndian(header[4..]);
            var dataPosition = stream.Position;

            // A chunk size is stored in a signed field, so a malformed file can declare a negative size (which
            // would move the cursor backwards and loop forever) or a size far beyond the end of the file
            // (which would allocate a buffer that can never be filled).
            if (chunkSize < 0 || chunkSize > endPosition - dataPosition)
                return chunks;

            var chunk = new AiffChunk
            {
                Id = chunkId,
                Size = chunkSize,
                DataPosition = dataPosition,
            };

            if (IsTagChunk(chunkId) && chunkSize > 0 && chunkSize <= StreamHelpers.MaxRecordDataSize)
            {
                chunk.Data = new byte[chunkSize];
                if (stream.ReadAtLeast(chunk.Data, chunkSize, throwOnEndOfStream: false) < chunkSize)
                    return chunks;
            }

            chunks.Add(chunk);

            // Skip to next chunk (big-endian sizes, pad to even boundary)
            var nextPos = dataPosition + chunkSize;
            if (chunkSize % 2 != 0)
                nextPos++;

            stream.Position = nextPos;
        }

        complete = true;
        return chunks;
    }

    public static bool IsTagChunk(string chunkId) => Array.IndexOf(TagChunkIds, chunkId) >= 0;
}
