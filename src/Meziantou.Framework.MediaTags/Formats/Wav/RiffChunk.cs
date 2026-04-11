using System.Buffers.Binary;
using System.Text;

namespace Meziantou.Framework.MediaTags.Formats.Wav;

internal sealed class RiffChunk
{
    public string Id { get; set; } = "";
    public int Size { get; set; }
    public long DataPosition { get; set; }
    public byte[]? Data { get; set; }
    public List<RiffChunk> SubChunks { get; } = [];

    public static List<RiffChunk> ReadChunks(Stream stream, long endPosition)
    {
        var chunks = new List<RiffChunk>();
        Span<byte> header = stackalloc byte[8];
        Span<byte> listType = stackalloc byte[4];

        while (stream.Position + 8 <= endPosition)
        {
            if (stream.ReadAtLeast(header, 8, throwOnEndOfStream: false) < 8)
                break;

            var id = Encoding.ASCII.GetString(header[..4]);
            var size = BinaryPrimitives.ReadInt32LittleEndian(header[4..]);

            var chunk = new RiffChunk
            {
                Id = id,
                Size = size,
                DataPosition = stream.Position,
            };

            if (id == "LIST")
            {
                // LIST chunk has a 4-byte type followed by sub-chunks
                if (size >= 4)
                {
                    stream.ReadAtLeast(listType, 4, throwOnEndOfStream: false);
                    chunk.Id = "LIST-" + Encoding.ASCII.GetString(listType);
                    chunk.SubChunks.AddRange(ReadChunks(stream, chunk.DataPosition + size));
                }
            }
            else if (size > 0 && size <= 10 * 1024 * 1024)
            {
                chunk.Data = new byte[size];
                stream.ReadAtLeast(chunk.Data, size, throwOnEndOfStream: false);
            }
            else
            {
                stream.Seek(size, SeekOrigin.Current);
            }

            // Chunks are padded to even byte boundaries
            if (size % 2 != 0 && stream.Position < endPosition)
                stream.Seek(1, SeekOrigin.Current);

            chunks.Add(chunk);
        }

        return chunks;
    }
}
