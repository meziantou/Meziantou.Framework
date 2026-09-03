using Meziantou.Framework.MediaTags.Internals;

namespace Meziantou.Framework.MediaTags.Formats.Aiff;

internal sealed class AiffReader : IMediaTagReader
{
    public MediaTagResult<MediaTagInfo> ReadTags(Stream stream)
    {
        try
        {
            stream.Position = 0;
            var tags = new MediaTagInfo();

            // Read FORM header
            Span<byte> header = stackalloc byte[12];
            if (stream.ReadAtLeast(header, 12, throwOnEndOfStream: false) < 12)
                return MediaTagResult<MediaTagInfo>.Failure(MediaTagError.CorruptFile, "File too small for AIFF.");

            if (header[0] != 'F' || header[1] != 'O' || header[2] != 'R' || header[3] != 'M')
                return MediaTagResult<MediaTagInfo>.Failure(MediaTagError.UnsupportedFormat, "Not an IFF file.");

            var formType = Encoding.ASCII.GetString(header[8..12]);
            if (formType is not ("AIFF" or "AIFC"))
                return MediaTagResult<MediaTagInfo>.Failure(MediaTagError.UnsupportedFormat, "Not an AIFF file.");

            // Reading is best effort: whatever chunks were parsed before a malformed one still carry usable
            // tags, and unlike the writer nothing is destroyed by using them.
            var chunks = AiffChunk.ReadChunks(stream, stream.Length, out _);
            foreach (var chunk in chunks)
            {
                ProcessChunk(chunk, tags);
            }

            return MediaTagResult<MediaTagInfo>.Success(tags);
        }
        catch (Exception ex) when (MediaTagErrors.TryMap(ex, out var error))
        {
            return MediaTagResult<MediaTagInfo>.Failure(error, ex.Message);
        }
    }

    private static void ProcessChunk(AiffChunk chunk, MediaTagInfo tags)
    {
        if (chunk.Data is not { } data)
            return;

        switch (chunk.Id)
        {
            case "ID3 " or "id3 ":
                using (var id3Stream = new MemoryStream(data))
                {
                    Id3v2.Id3v2Reader.TryReadTag(id3Stream, tags);
                }

                break;

            case "NAME":
                tags.Title ??= ReadAiffString(data);
                break;

            case "AUTH":
                tags.Artist ??= ReadAiffString(data);
                break;

            case "ANNO":
                tags.Comment ??= ReadAiffString(data);
                break;

            case "(c) ":
                tags.Copyright ??= ReadAiffString(data);
                break;

            case "ISRC":
                tags.Isrc ??= ReadAiffString(data);
                break;
        }
    }

    private static string ReadAiffString(byte[] data)
    {
        var length = data.Length;
        while (length > 0 && data[length - 1] == 0)
            length--;

        return Encoding.Latin1.GetString(data, 0, length);
    }
}
