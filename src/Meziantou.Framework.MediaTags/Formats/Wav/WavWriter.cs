using System.Buffers.Binary;
using System.Text;

namespace Meziantou.Framework.MediaTags.Formats.Wav;

internal sealed class WavWriter : IMediaTagWriter
{
    public MediaTagResult WriteTags(Stream inputStream, Stream outputStream, MediaTagInfo tags)
    {
        try
        {
            inputStream.Position = 0;

            // Read RIFF header
            Span<byte> riffHeader = stackalloc byte[12];
            if (inputStream.ReadAtLeast(riffHeader, 12, throwOnEndOfStream: false) < 12)
                return MediaTagResult.Failure(MediaTagError.CorruptFile, "File too small for WAV.");

            // Read all chunks
            var chunks = RiffChunk.ReadChunks(inputStream, inputStream.Length);

            // Build new ID3v2 tag
            var id3v2Tag = Id3v2.Id3v2Writer.BuildTag(tags);

            // Rebuild the file
            using var bodyStream = new MemoryStream();

            // Copy existing non-tag chunks
            foreach (var chunk in chunks)
            {
                if (chunk.Id is "LIST-INFO" or "id3 " or "ID3 " or "ID32")
                    continue; // Skip existing tag chunks

                if (chunk.Id.StartsWith("LIST-", StringComparison.Ordinal))
                {
                    // Preserve non-INFO LIST chunks
                    WriteChunkFromSource(inputStream, bodyStream, chunk);
                }
                else if (chunk.Data is not null)
                {
                    WriteChunk(bodyStream, chunk.Id, chunk.Data);
                }
                else
                {
                    WriteChunkFromSource(inputStream, bodyStream, chunk);
                }
            }

            // Write ID3v2 tag as "id3 " chunk
            WriteChunk(bodyStream, "id3 ", id3v2Tag);

            // Write RIFF header + body
            var totalSize = 4 + (int)bodyStream.Length; // "WAVE" + chunks
            Span<byte> outputHeader = stackalloc byte[12];
            outputHeader[0] = (byte)'R';
            outputHeader[1] = (byte)'I';
            outputHeader[2] = (byte)'F';
            outputHeader[3] = (byte)'F';
            BinaryPrimitives.WriteInt32LittleEndian(outputHeader[4..], totalSize);
            outputHeader[8] = (byte)'W';
            outputHeader[9] = (byte)'A';
            outputHeader[10] = (byte)'V';
            outputHeader[11] = (byte)'E';

            outputStream.Write(outputHeader);
            bodyStream.Position = 0;
            bodyStream.CopyTo(outputStream);

            return MediaTagResult.Success();
        }
        catch (Exception ex)
        {
            return MediaTagResult.Failure(MediaTagError.IoError, ex.Message);
        }
    }

    private static void WriteChunk(Stream output, string id, byte[] data)
    {
        Span<byte> header = stackalloc byte[8];
        Encoding.ASCII.GetBytes(id, header[..4]);
        BinaryPrimitives.WriteInt32LittleEndian(header[4..], data.Length);
        output.Write(header);
        output.Write(data);

        // Pad to even boundary
        if (data.Length % 2 != 0)
            output.WriteByte(0);
    }

    private static void WriteChunkFromSource(Stream source, Stream output, RiffChunk chunk)
    {
        Span<byte> header = stackalloc byte[8];
        Encoding.ASCII.GetBytes(chunk.Id.StartsWith("LIST-", StringComparison.Ordinal) ? "LIST" : chunk.Id, header[..4]);
        BinaryPrimitives.WriteInt32LittleEndian(header[4..], chunk.Size);
        output.Write(header);

        if (chunk.Id.StartsWith("LIST-", StringComparison.Ordinal))
        {
            // Write list type
            var listType = chunk.Id[5..];
            Span<byte> listTypeBytes = stackalloc byte[4];
            Encoding.ASCII.GetBytes(listType, listTypeBytes);
            output.Write(listTypeBytes);
        }

        // Copy data from source
        source.Position = chunk.DataPosition;
        if (!chunk.Id.StartsWith("LIST-", StringComparison.Ordinal))
        {
            var buffer = new byte[Math.Min(chunk.Size, 8192)];
            var remaining = chunk.Size;
            while (remaining > 0)
            {
                var toRead = Math.Min(remaining, buffer.Length);
                var read = source.Read(buffer, 0, toRead);
                if (read == 0) break;
                output.Write(buffer, 0, read);
                remaining -= read;
            }
        }
        else
        {
            // For LIST chunks, copy sub-chunk data (skip the 4-byte list type we already wrote)
            source.Position = chunk.DataPosition + 4;
            var remaining = chunk.Size - 4;
            var buffer = new byte[Math.Min(remaining, 8192)];
            while (remaining > 0)
            {
                var toRead = Math.Min(remaining, buffer.Length);
                var read = source.Read(buffer, 0, toRead);
                if (read == 0) break;
                output.Write(buffer, 0, read);
                remaining -= read;
            }
        }

        // Pad to even boundary
        if (chunk.Size % 2 != 0)
            output.WriteByte(0);
    }
}
