using System.Buffers.Binary;
using System.Text;

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

            // Parse chunks
            Span<byte> chunkHeader = stackalloc byte[8];
            while (stream.Position + 8 <= stream.Length)
            {
                if (stream.ReadAtLeast(chunkHeader, 8, throwOnEndOfStream: false) < 8)
                    break;

                var chunkId = Encoding.ASCII.GetString(chunkHeader[..4]);
                var chunkSize = BinaryPrimitives.ReadInt32BigEndian(chunkHeader[4..]);
                var chunkDataStart = stream.Position;

                if (chunkId is "ID3 " or "id3 ")
                {
                    var chunkData = new byte[chunkSize];
                    if (stream.ReadAtLeast(chunkData, chunkSize, throwOnEndOfStream: false) >= chunkSize)
                    {
                        using var id3Stream = new MemoryStream(chunkData);
                        Id3v2.Id3v2Reader.TryReadTag(id3Stream, tags);
                    }
                }
                else if (chunkId == "NAME")
                {
                    var data = new byte[chunkSize];
                    if (stream.ReadAtLeast(data, chunkSize, throwOnEndOfStream: false) >= chunkSize)
                        tags.Title ??= ReadAiffString(data);
                }
                else if (chunkId == "AUTH")
                {
                    var data = new byte[chunkSize];
                    if (stream.ReadAtLeast(data, chunkSize, throwOnEndOfStream: false) >= chunkSize)
                        tags.Artist ??= ReadAiffString(data);
                }
                else if (chunkId == "ANNO")
                {
                    var data = new byte[chunkSize];
                    if (stream.ReadAtLeast(data, chunkSize, throwOnEndOfStream: false) >= chunkSize)
                        tags.Comment ??= ReadAiffString(data);
                }
                else if (chunkId == "(c) ")
                {
                    var data = new byte[chunkSize];
                    if (stream.ReadAtLeast(data, chunkSize, throwOnEndOfStream: false) >= chunkSize)
                        tags.Copyright ??= ReadAiffString(data);
                }
                else if (chunkId == "ISRC")
                {
                    var data = new byte[chunkSize];
                    if (stream.ReadAtLeast(data, chunkSize, throwOnEndOfStream: false) >= chunkSize)
                        tags.Isrc ??= ReadAiffString(data);
                }

                // Skip to next chunk (big-endian sizes, pad to even boundary)
                var nextPos = chunkDataStart + chunkSize;
                if (chunkSize % 2 != 0)
                    nextPos++;
                stream.Position = nextPos;
            }

            return MediaTagResult<MediaTagInfo>.Success(tags);
        }
        catch (Exception ex)
        {
            return MediaTagResult<MediaTagInfo>.Failure(MediaTagError.CorruptFile, ex.Message);
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
