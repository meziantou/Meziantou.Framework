using System.Buffers.Binary;
using Meziantou.Framework.MediaTags.Internals;

namespace Meziantou.Framework.MediaTags.Formats.Aiff;

internal sealed class AiffWriter : IMediaTagWriter
{
    public MediaTagResult WriteTags(Stream inputStream, Stream outputStream, MediaTagInfo tags, MediaTagWriteOptions options)
    {
        try
        {
            inputStream.Position = 0;

            // Read FORM header
            Span<byte> formHeader = stackalloc byte[12];
            if (inputStream.ReadAtLeast(formHeader, 12, throwOnEndOfStream: false) < 12)
                return MediaTagResult.Failure(MediaTagError.CorruptFile, "File too small for AIFF.");

            // The container must be verified before anything is written: the output below synthesizes a fresh
            // FORM header, so writing it over a file that is not an AIFF replaces all of its content.
            if (formHeader[0] != 'F' || formHeader[1] != 'O' || formHeader[2] != 'R' || formHeader[3] != 'M')
                return MediaTagResult.Failure(MediaTagError.UnsupportedFormat, "Not an IFF file.");

            var formType = Encoding.ASCII.GetString(formHeader[8..12]);
            if (formType is not ("AIFF" or "AIFC"))
                return MediaTagResult.Failure(MediaTagError.UnsupportedFormat, "Not an AIFF file.");

            var chunks = AiffChunk.ReadChunks(inputStream, inputStream.Length, out var complete);

            // Rebuilding the file from a partial parse silently drops every chunk after the bad one, the audio
            // included, and this output is about to replace the caller's file.
            if (!complete)
                return MediaTagResult.Failure(MediaTagError.CorruptFile, "The AIFF chunks do not cover the whole file.");

            // Build new ID3v2 tag
            if (!Id3v2.Id3v2Writer.TryBuildTag(tags, options.Id3v2PaddingSize, out var id3v2Tag, out var buildError))
                return MediaTagResult.Failure(MediaTagError.InvalidTagData, buildError);

            var preservedChunks = new List<AiffChunk>();
            foreach (var chunk in chunks)
            {
                // Drop every chunk AiffReader can source a tag from, otherwise the stale chunk is read
                // back in preference to the ID3 tag written below. Keep in sync with AiffChunk.TagChunkIds.
                if (AiffChunk.IsTagChunk(chunk.Id))
                    continue;

                preservedChunks.Add(chunk);
            }

            // The FORM size field precedes the body, so the total is computed rather than produced by staging
            // the whole file in memory.
            var bodySize = 4L; // formType
            foreach (var chunk in preservedChunks)
            {
                bodySize += 8 + chunk.Size + (chunk.Size % 2);
            }

            if (id3v2Tag.Length > 0)
                bodySize += 8 + id3v2Tag.Length + (id3v2Tag.Length % 2);

            if (bodySize > int.MaxValue)
                return MediaTagResult.Failure(MediaTagError.InvalidTagData, "The tagged file would be too large for the AIFF container.");

            Span<byte> outputHeader = stackalloc byte[12];
            outputHeader[0] = (byte)'F';
            outputHeader[1] = (byte)'O';
            outputHeader[2] = (byte)'R';
            outputHeader[3] = (byte)'M';
            BinaryPrimitives.WriteInt32BigEndian(outputHeader[4..], (int)bodySize);
            Encoding.ASCII.GetBytes(formType, outputHeader[8..12]);
            outputStream.Write(outputHeader);

            foreach (var chunk in preservedChunks)
            {
                WriteChunkHeader(outputStream, chunk.Id, chunk.Size);
                if (!StreamHelpers.CopyExactlyFrom(inputStream, outputStream, chunk.DataPosition, chunk.Size))
                    return MediaTagResult.Failure(MediaTagError.UnexpectedEndOfStream, "The file ended inside an AIFF chunk.");

                WritePadding(outputStream, chunk.Size);
            }

            // Write ID3 chunk
            if (id3v2Tag.Length > 0)
            {
                WriteChunkHeader(outputStream, "ID3 ", id3v2Tag.Length);
                outputStream.Write(id3v2Tag);
                WritePadding(outputStream, id3v2Tag.Length);
            }

            return MediaTagResult.Success();
        }
        catch (Exception ex) when (MediaTagErrors.TryMap(ex, out var error))
        {
            return MediaTagResult.Failure(error, ex.Message);
        }
    }

    private static void WriteChunkHeader(Stream output, string id, int size)
    {
        Span<byte> header = stackalloc byte[8];
        Encoding.ASCII.GetBytes(id, header[..4]);
        BinaryPrimitives.WriteInt32BigEndian(header[4..], size);
        output.Write(header);
    }

    private static void WritePadding(Stream output, int size)
    {
        // Chunks are padded to even byte boundaries
        if (size % 2 != 0)
            output.WriteByte(0);
    }
}
