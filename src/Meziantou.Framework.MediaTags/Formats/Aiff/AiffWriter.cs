using System.Buffers.Binary;
using System.Text;

namespace Meziantou.Framework.MediaTags.Formats.Aiff;

internal sealed class AiffWriter : IMediaTagWriter
{
    public MediaTagResult WriteTags(Stream inputStream, Stream outputStream, MediaTagInfo tags)
    {
        try
        {
            inputStream.Position = 0;

            // Read FORM header
            Span<byte> formHeader = stackalloc byte[12];
            if (inputStream.ReadAtLeast(formHeader, 12, throwOnEndOfStream: false) < 12)
                return MediaTagResult.Failure(MediaTagError.CorruptFile, "File too small for AIFF.");

            var formType = Encoding.ASCII.GetString(formHeader[8..12]);

            // Read all chunk positions
            var existingChunks = new List<(string Id, int Size, long DataPos)>();
            Span<byte> chunkHeader = stackalloc byte[8];
            while (inputStream.Position + 8 <= inputStream.Length)
            {
                if (inputStream.ReadAtLeast(chunkHeader, 8, throwOnEndOfStream: false) < 8)
                    break;

                var chunkId = Encoding.ASCII.GetString(chunkHeader[..4]);
                var chunkSize = BinaryPrimitives.ReadInt32BigEndian(chunkHeader[4..]);
                var dataPos = inputStream.Position;

                existingChunks.Add((chunkId, chunkSize, dataPos));

                var nextPos = dataPos + chunkSize;
                if (chunkSize % 2 != 0) nextPos++;
                inputStream.Position = nextPos;
            }

            // Build new ID3v2 tag
            var id3v2Tag = Id3v2.Id3v2Writer.BuildTag(tags);

            // Write FORM header (placeholder size, will update)
            using var bodyStream = new MemoryStream();

            // Copy existing chunks except ID3
            foreach (var (id, size, dataPos) in existingChunks)
            {
                if (id is "ID3 " or "id3 " or "NAME" or "AUTH" or "ANNO" or "(c) ")
                    continue;

                // Write chunk header
                Encoding.ASCII.GetBytes(id, chunkHeader[..4]);
                BinaryPrimitives.WriteInt32BigEndian(chunkHeader[4..], size);
                bodyStream.Write(chunkHeader);

                // Copy chunk data
                inputStream.Position = dataPos;
                var buffer = new byte[Math.Min(size, 8192)];
                var remaining = size;
                while (remaining > 0)
                {
                    var toRead = Math.Min(remaining, buffer.Length);
                    var read = inputStream.Read(buffer, 0, toRead);
                    if (read == 0) break;
                    bodyStream.Write(buffer, 0, read);
                    remaining -= read;
                }

                if (size % 2 != 0)
                    bodyStream.WriteByte(0);
            }

            // Write ID3 chunk
            Span<byte> id3Header = stackalloc byte[8];
            Encoding.ASCII.GetBytes("ID3 ", id3Header[..4]);
            BinaryPrimitives.WriteInt32BigEndian(id3Header[4..], id3v2Tag.Length);
            bodyStream.Write(id3Header);
            bodyStream.Write(id3v2Tag);
            if (id3v2Tag.Length % 2 != 0)
                bodyStream.WriteByte(0);

            // Write FORM header with correct size
            var totalFormSize = 4 + (int)bodyStream.Length; // formType + chunks
            Span<byte> outputHeader = stackalloc byte[12];
            outputHeader[0] = (byte)'F';
            outputHeader[1] = (byte)'O';
            outputHeader[2] = (byte)'R';
            outputHeader[3] = (byte)'M';
            BinaryPrimitives.WriteInt32BigEndian(outputHeader[4..], totalFormSize);
            Encoding.ASCII.GetBytes(formType, outputHeader[8..12]);

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
}
