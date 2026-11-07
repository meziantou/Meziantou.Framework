using System.Buffers.Binary;
using Meziantou.Framework.MediaTags.Internals;

namespace Meziantou.Framework.MediaTags.Formats.Wav;

internal sealed class RiffChunk
{
    /// <summary>The maximum number of nested LIST chunks a file may contain.</summary>
    /// <remarks>
    /// Reading is recursive, so an unbounded nesting depth would overflow the stack, which cannot be caught.
    /// Real files nest a level or two; this limit is far above any legitimate use.
    /// </remarks>
    public const int MaxDepth = 32;

    /// <summary>The maximum number of chunks read from one file.</summary>
    /// <remarks>
    /// An empty chunk costs 8 bytes in the file but a retained object here, so an unbounded count lets a small
    /// file force a disproportionate allocation. Real files hold a handful of chunks.
    /// </remarks>
    public const int MaxCount = 8192;

    public string Id { get; set; } = "";

    /// <summary>Gets or sets the size of the chunk content. RIFF sizes are unsigned: a data chunk reaches 4 GB.</summary>
    public long Size { get; set; }
    public long DataPosition { get; set; }
    public byte[]? Data { get; set; }
    public List<RiffChunk> SubChunks { get; } = [];

    /// <summary>Gets the four-character identifier to write back to the file.</summary>
    public string ContainerId => Id.StartsWith("LIST-", StringComparison.Ordinal) ? "LIST" : Id;

    /// <summary>
    /// Reads the chunks between the current position and <paramref name="endPosition"/>.
    /// </summary>
    /// <param name="complete">
    /// <see langword="true"/> when every byte of the container was accounted for. A writer must not rebuild a
    /// file from an incomplete parse: the chunks that were not reached, <c>data</c> included, would be dropped.
    /// </param>
    public static List<RiffChunk> ReadChunks(Stream stream, long endPosition, out bool complete)
    {
        var chunks = new List<RiffChunk>();
        var remainingCount = MaxCount;
        complete = ReadChunks(stream, endPosition, depth: 0, ref remainingCount, chunks);
        return chunks;
    }

    private static bool ReadChunks(Stream stream, long endPosition, int depth, ref int remainingCount, List<RiffChunk> chunks)
    {
        if (depth >= MaxDepth)
            throw new InvalidDataException($"RIFF chunks are nested too deeply. The maximum supported depth is {MaxDepth}.");

        Span<byte> header = stackalloc byte[8];
        Span<byte> listType = stackalloc byte[4];

        while (stream.Position + 8 <= endPosition)
        {
            if (remainingCount <= 0)
                throw new InvalidDataException($"The file declares more than {MaxCount} RIFF chunks.");

            if (stream.ReadAtLeast(header, 8, throwOnEndOfStream: false) < 8)
                return false;

            var id = Encoding.ASCII.GetString(header[..4]);
            var size = (long)BinaryPrimitives.ReadUInt32LittleEndian(header[4..]);
            var chunk = new RiffChunk
            {
                Id = id,
                Size = size,
                DataPosition = stream.Position,
            };

            if (chunk.DataPosition > endPosition - size)
                return false;

            remainingCount--;
            var chunkEnd = chunk.DataPosition + size;
            if (id == "LIST")
            {
                // LIST chunk has a 4-byte type followed by sub-chunks
                if (size >= 4)
                {
                    if (stream.ReadAtLeast(listType, 4, throwOnEndOfStream: false) < 4)
                        return false;

                    chunk.Id = "LIST-" + Encoding.ASCII.GetString(listType);
                    if (!ReadChunks(stream, chunkEnd, depth + 1, ref remainingCount, chunk.SubChunks))
                        return false;
                }
            }
            else if (size > 0 && size <= GetBufferLimit(id, depth))
            {
                chunk.Data = new byte[size];
                if (stream.ReadAtLeast(chunk.Data, (int)size, throwOnEndOfStream: false) < size)
                    return false;
            }

            stream.Position = chunkEnd;

            // Chunks are padded to even byte boundaries
            if (size % 2 != 0 && stream.Position < endPosition)
                stream.Seek(1, SeekOrigin.Current);

            chunks.Add(chunk);
        }

        return true;
    }

    /// <summary>
    /// Gets the size up to which the content of a chunk is read into memory, or 0 when it is not needed.
    /// </summary>
    /// <remarks>
    /// The <c>data</c> chunk holds the audio: buffering it costs a full read and a large object heap allocation
    /// per file for bytes nothing looks at. Only the chunks a tag is read from are buffered.
    /// </remarks>
    private static long GetBufferLimit(string id, int depth)
    {
        // An ID3v2 tag holding large artwork is legitimately bigger than any other record. Leaving it unread
        // would not just hide the tags: the writer drops the chunk and rebuilds the tag without them. The size
        // was already checked against the bytes actually in the file.
        if (IsId3Chunk(id))
            return Id3v2.Id3v2Header.MaxTotalSize;

        // Sub-chunks of a LIST are the INFO tag values themselves, and are small.
        if (depth > 0 || id is "fmt " or "fact")
            return StreamHelpers.MaxRecordDataSize;

        return 0;
    }

    public static bool IsId3Chunk(string id) => id is "id3 " or "ID3 " or "ID32";
}
