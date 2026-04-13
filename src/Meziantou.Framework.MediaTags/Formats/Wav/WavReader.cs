using System.Text;

namespace Meziantou.Framework.MediaTags.Formats.Wav;

internal sealed class WavReader : IMediaTagReader
{
    public MediaTagResult<MediaTagInfo> ReadTags(Stream stream)
    {
        try
        {
            stream.Position = 0;
            var tags = new MediaTagInfo();

            // Read RIFF header
            Span<byte> riffHeader = stackalloc byte[12];
            if (stream.ReadAtLeast(riffHeader, 12, throwOnEndOfStream: false) < 12)
                return MediaTagResult<MediaTagInfo>.Failure(MediaTagError.CorruptFile, "File too small for WAV.");

            if (riffHeader[0] != 'R' || riffHeader[1] != 'I' || riffHeader[2] != 'F' || riffHeader[3] != 'F')
                return MediaTagResult<MediaTagInfo>.Failure(MediaTagError.UnsupportedFormat, "Not a RIFF file.");

            if (riffHeader[8] != 'W' || riffHeader[9] != 'A' || riffHeader[10] != 'V' || riffHeader[11] != 'E')
                return MediaTagResult<MediaTagInfo>.Failure(MediaTagError.UnsupportedFormat, "Not a WAV file.");

            var chunks = RiffChunk.ReadChunks(stream, stream.Length);

            // Look for LIST-INFO chunk
            foreach (var chunk in chunks)
            {
                if (chunk.Id == "LIST-INFO")
                {
                    ReadInfoChunks(chunk.SubChunks, tags);
                }
                else if (chunk.Id is "id3 " or "ID3 " or "ID32")
                {
                    // ID3v2 tag embedded in WAV
                    if (chunk.Data is not null)
                    {
                        using var id3Stream = new MemoryStream(chunk.Data);
                        Id3v2.Id3v2Reader.TryReadTag(id3Stream, tags);
                    }
                }
            }

            return MediaTagResult<MediaTagInfo>.Success(tags);
        }
        catch (Exception ex)
        {
            return MediaTagResult<MediaTagInfo>.Failure(MediaTagError.CorruptFile, ex.Message);
        }
    }

    private static void ReadInfoChunks(List<RiffChunk> chunks, MediaTagInfo tags)
    {
        foreach (var chunk in chunks)
        {
            if (chunk.Data is null)
                continue;

            var value = ReadInfoString(chunk.Data);
            if (string.IsNullOrEmpty(value))
                continue;

            switch (chunk.Id)
            {
                case "INAM": tags.Title ??= value; break;
                case "IART": tags.Artist ??= value; break;
                case "IPRD": tags.Album ??= value; break;
                case "IGNR": tags.Genre ??= value; break;
                case "ICRD": // Creation date
                    if (tags.Year is null && value.Length >= 4 && int.TryParse(value.AsSpan(0, 4), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var year))
                        tags.Year = year;
                    break;
                case "ITRK":
                    if (tags.TrackNumber is null && int.TryParse(value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var track))
                        tags.TrackNumber = track;
                    break;
                case "ICMT": tags.Comment ??= value; break;
                case "ILYR": tags.Lyrics ??= value; break;
                case "ISRC": tags.Isrc ??= value; break;
                case "ICOP": tags.Copyright ??= value; break;
                case "IENG": tags.Composer ??= value; break;
                default:
                    tags.CustomFields.TryAdd(chunk.Id, value);
                    break;
            }
        }
    }

    private static string ReadInfoString(byte[] data)
    {
        // INFO strings are null-terminated ASCII/Latin-1
        var length = data.Length;
        while (length > 0 && data[length - 1] == 0)
            length--;
        return Encoding.Latin1.GetString(data, 0, length);
    }
}
