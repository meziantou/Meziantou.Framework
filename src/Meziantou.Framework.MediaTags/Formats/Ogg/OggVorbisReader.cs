namespace Meziantou.Framework.MediaTags.Formats.Ogg;

internal sealed class OggVorbisReader : IMediaTagReader
{
    public MediaTagResult<MediaTagInfo> ReadTags(Stream stream)
    {
        try
        {
            stream.Position = 0;
            var tags = new MediaTagInfo();

            // Read pages until we find the Vorbis comment header
            // Page 0: identification header (\x01vorbis)
            // Page 1: comment header (\x03vorbis)
            var pageIndex = 0;
            while (true)
            {
                var page = OggPage.Read(stream);
                if (page is null)
                    break;

                if (pageIndex == 1 || IsVorbisCommentPage(page))
                {
                    // Extract comment data: skip "\x03vorbis" (7 bytes) prefix
                    var data = page.Data;
                    if (data.Length > 7 && data[0] == 0x03
                        && data[1] == 'v' && data[2] == 'o' && data[3] == 'r'
                        && data[4] == 'b' && data[5] == 'i' && data[6] == 's')
                    {
                        VorbisComment.VorbisCommentReader.TryParse(data.AsSpan(7), tags);
                    }
                    break;
                }

                pageIndex++;
                if (pageIndex > 10)
                    break; // Safety limit
            }

            return MediaTagResult<MediaTagInfo>.Success(tags);
        }
        catch (Exception ex)
        {
            return MediaTagResult<MediaTagInfo>.Failure(MediaTagError.CorruptFile, ex.Message);
        }
    }

    private static bool IsVorbisCommentPage(OggPage page)
    {
        return page.Data.Length > 7
            && page.Data[0] == 0x03
            && page.Data[1] == 'v' && page.Data[2] == 'o' && page.Data[3] == 'r'
            && page.Data[4] == 'b' && page.Data[5] == 'i' && page.Data[6] == 's';
    }
}
