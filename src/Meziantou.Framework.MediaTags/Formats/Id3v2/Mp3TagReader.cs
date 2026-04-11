namespace Meziantou.Framework.MediaTags.Formats.Id3v2;

internal sealed class Mp3TagReader : IMediaTagReader
{
    public MediaTagResult<MediaTagInfo> ReadTags(Stream stream)
    {
        try
        {
            var tags = new MediaTagInfo();

            // Try ID3v2 first (at start of file)
            stream.Position = 0;
            Id3v2Reader.TryReadTag(stream, tags);

            // Then try ID3v1 (at end of file) — ID3v2 values take priority (already set via ??=)
            Id3v1.Id3v1Reader.TryReadTag(stream, tags);

            return MediaTagResult<MediaTagInfo>.Success(tags);
        }
        catch (Exception ex)
        {
            return MediaTagResult<MediaTagInfo>.Failure(MediaTagError.CorruptFile, ex.Message);
        }
    }
}
