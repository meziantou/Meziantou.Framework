using System.Buffers.Binary;

namespace Meziantou.Framework.MediaTags.Formats.Wav;

internal sealed class RiffChunk
{
    /// <summary>The maximum number of nested LIST chunks a file may contain.</summary>
    /// <remarks>
    /// Reading is recursive, so an unbounded nesting depth would overflow the stack, which cannot be caught.
    /// Real files nest a level or two; this limit is far above any legitimate use.
    /// </remarks>
    public const int MaxDepth = 32;

    public string Id { get; set; } = "";
    public int Size { get; set; }
    public long DataPosition { get; set; }
    public byte[]? Data { get; set; }
    public List<RiffChunk> SubChunks { get; } = [];

    public static List<RiffChunk> ReadChunks(Stream stream, long endPosition)
    {
        return ReadChunks(stream, endPosition, depth: 0);
    }

    private static List<RiffChunk> ReadChunks(Stream stream, long endPosition, int depth)
    {
        if (depth >= MaxDepth)
            throw new InvalidDataException($"RIFF chunks are nested too deeply. The maximum supported depth is {MaxDepth}.");

        var chunks = new List<RiffChunk>();
        Span<byte> header = stackalloc byte[8];
        Span<byte> listType = stackalloc byte[4];

        while (stream.Position + 8 <= endPosition)
        {
            if (stream.ReadAtLeast(header, 8, throwOnEndOfStream: false) < 8)
                break;

            var id = Encoding.ASCII.GetString(header[..4]);
            var size = BinaryPrimitives.ReadInt32LittleEndian(header[4..]);
            if (size < 0)
                break;

            var chunk = new RiffChunk
            {
                Id = id,
                Size = size,
                DataPosition = stream.Position,
            };

            if (chunk.DataPosition > endPosition - size)
                break;

            var chunkEnd = chunk.DataPosition + size;
            if (id == "LIST")
            {
                // LIST chunk has a 4-byte type followed by sub-chunks
                if (size >= 4)
                {
                    if (stream.ReadAtLeast(listType, 4, throwOnEndOfStream: false) < 4)
                        break;

                    chunk.Id = "LIST-" + Encoding.ASCII.GetString(listType);
                    chunk.SubChunks.AddRange(ReadChunks(stream, chunkEnd, depth + 1));
                }
            }
            else if (size > 0 && size <= 10 * 1024 * 1024)
            {
                chunk.Data = new byte[size];
                if (stream.ReadAtLeast(chunk.Data, size, throwOnEndOfStream: false) < size)
                    break;
            }

            stream.Position = chunkEnd;

            // Chunks are padded to even byte boundaries
            if (size % 2 != 0 && stream.Position < endPosition)
                stream.Seek(1, SeekOrigin.Current);

            chunks.Add(chunk);
        }

        return chunks;
    }
}
