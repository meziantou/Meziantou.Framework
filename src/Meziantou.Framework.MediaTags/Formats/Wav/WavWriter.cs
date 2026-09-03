using System.Buffers.Binary;
using Meziantou.Framework.MediaTags.Internals;

namespace Meziantou.Framework.MediaTags.Formats.Wav;

internal sealed class WavWriter : IMediaTagWriter
{
    public MediaTagResult WriteTags(Stream inputStream, Stream outputStream, MediaTagInfo tags, MediaTagWriteOptions options)
    {
        try
        {
            inputStream.Position = 0;

            // Read RIFF header
            Span<byte> riffHeader = stackalloc byte[12];
            if (inputStream.ReadAtLeast(riffHeader, 12, throwOnEndOfStream: false) < 12)
                return MediaTagResult.Failure(MediaTagError.CorruptFile, "File too small for WAV.");

            // The container must be verified before anything is written: the output below synthesizes a fresh
            // RIFF/WAVE header, so writing it over a file that is not a WAV replaces all of its content.
            if (riffHeader[0] != 'R' || riffHeader[1] != 'I' || riffHeader[2] != 'F' || riffHeader[3] != 'F')
                return MediaTagResult.Failure(MediaTagError.UnsupportedFormat, "Not a RIFF file.");

            if (riffHeader[8] != 'W' || riffHeader[9] != 'A' || riffHeader[10] != 'V' || riffHeader[11] != 'E')
                return MediaTagResult.Failure(MediaTagError.UnsupportedFormat, "Not a WAV file.");

            // Read all chunks
            var chunks = RiffChunk.ReadChunks(inputStream, inputStream.Length, out var complete);

            // Rebuilding the file from a partial parse silently drops every chunk after the bad one, the audio
            // included, and this output is about to replace the caller's file.
            if (!complete)
                return MediaTagResult.Failure(MediaTagError.CorruptFile, "The WAV chunks do not cover the whole file.");

            // Build new ID3v2 tag
            if (!Id3v2.Id3v2Writer.TryBuildTag(tags, options.Id3v2PaddingSize, out var id3v2Tag, out var buildError))
                return MediaTagResult.Failure(MediaTagError.InvalidTagData, buildError);

            var preservedChunks = new List<RiffChunk>();
            foreach (var chunk in chunks)
            {
                if (chunk.Id is "LIST-INFO" or "id3 " or "ID3 " or "ID32")
                    continue; // Skip existing tag chunks

                preservedChunks.Add(chunk);
            }

            // The RIFF size field precedes the body, so the total is computed rather than produced by staging
            // the whole file in memory.
            var bodySize = 4L; // "WAVE"
            foreach (var chunk in preservedChunks)
            {
                bodySize += 8 + chunk.Size + (chunk.Size % 2);
            }

            if (id3v2Tag.Length > 0)
                bodySize += 8 + id3v2Tag.Length + (id3v2Tag.Length % 2);

            if (bodySize > uint.MaxValue)
                return MediaTagResult.Failure(MediaTagError.InvalidTagData, "The tagged file would be too large for the RIFF container.");

            Span<byte> outputHeader = stackalloc byte[12];
            outputHeader[0] = (byte)'R';
            outputHeader[1] = (byte)'I';
            outputHeader[2] = (byte)'F';
            outputHeader[3] = (byte)'F';
            BinaryPrimitives.WriteUInt32LittleEndian(outputHeader[4..], (uint)bodySize);
            outputHeader[8] = (byte)'W';
            outputHeader[9] = (byte)'A';
            outputHeader[10] = (byte)'V';
            outputHeader[11] = (byte)'E';
            outputStream.Write(outputHeader);

            foreach (var chunk in preservedChunks)
            {
                WriteChunkHeader(outputStream, chunk.ContainerId, chunk.Size);
                if (!StreamHelpers.CopyExactlyFrom(inputStream, outputStream, chunk.DataPosition, chunk.Size))
                    return MediaTagResult.Failure(MediaTagError.UnexpectedEndOfStream, "The file ended inside a WAV chunk.");

                WritePadding(outputStream, chunk.Size);
            }

            // Write ID3v2 tag as "id3 " chunk
            if (id3v2Tag.Length > 0)
            {
                WriteChunkHeader(outputStream, "id3 ", id3v2Tag.Length);
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
        BinaryPrimitives.WriteInt32LittleEndian(header[4..], size);
        output.Write(header);
    }

    private static void WritePadding(Stream output, int size)
    {
        // Chunks are padded to even byte boundaries
        if (size % 2 != 0)
            output.WriteByte(0);
    }
}
