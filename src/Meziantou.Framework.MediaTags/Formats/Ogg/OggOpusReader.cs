namespace Meziantou.Framework.MediaTags.Formats.Ogg;

internal sealed class OggOpusReader : IMediaTagReader
{
    public MediaTagResult<MediaTagInfo> ReadTags(Stream stream)
    {
        try
        {
            stream.Position = 0;
            var tags = new MediaTagInfo();

            // Read pages until we find the OpusTags header
            // Page 0: OpusHead identification header
            // Page 1: OpusTags comment header
            var pageIndex = 0;
            while (true)
            {
                var page = OggPage.Read(stream);
                if (page is null)
                    break;

                if (pageIndex == 1 || IsOpusTagsPage(page))
                {
                    // Extract comment data: skip "OpusTags" (8 bytes) prefix
                    var data = page.Data;
                    if (data.Length > 8
                        && data[0] == 'O' && data[1] == 'p' && data[2] == 'u' && data[3] == 's'
                        && data[4] == 'T' && data[5] == 'a' && data[6] == 'g' && data[7] == 's')
                    {
                        VorbisComment.VorbisCommentReader.TryParse(data.AsSpan(8), tags);
                    }
                    break;
                }

                pageIndex++;
                if (pageIndex > 10)
                    break;
            }

            return MediaTagResult<MediaTagInfo>.Success(tags);
        }
        catch (Exception ex)
        {
            return MediaTagResult<MediaTagInfo>.Failure(MediaTagError.CorruptFile, ex.Message);
        }
    }

    private static bool IsOpusTagsPage(OggPage page)
    {
        return page.Data.Length > 8
            && page.Data[0] == 'O' && page.Data[1] == 'p' && page.Data[2] == 'u' && page.Data[3] == 's'
            && page.Data[4] == 'T' && page.Data[5] == 'a' && page.Data[6] == 'g' && page.Data[7] == 's';
    }
}
