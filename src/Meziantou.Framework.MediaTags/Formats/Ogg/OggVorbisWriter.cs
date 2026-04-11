namespace Meziantou.Framework.MediaTags.Formats.Ogg;

internal sealed class OggVorbisWriter : IMediaTagWriter
{
    public MediaTagResult WriteTags(Stream inputStream, Stream outputStream, MediaTagInfo tags)
    {
        try
        {
            inputStream.Position = 0;

            // Read all pages
            var pages = new List<OggPage>();
            while (true)
            {
                var page = OggPage.Read(inputStream);
                if (page is null)
                    break;
                pages.Add(page);
            }

            if (pages.Count < 2)
                return MediaTagResult.Failure(MediaTagError.CorruptFile, "OGG file has fewer than 2 pages.");

            // Build new comment data
            var commentData = VorbisComment.VorbisCommentWriter.Build(tags);

            // Prepend "\x03vorbis" header
            var newCommentPacket = new byte[7 + commentData.Length];
            newCommentPacket[0] = 0x03;
            newCommentPacket[1] = (byte)'v';
            newCommentPacket[2] = (byte)'o';
            newCommentPacket[3] = (byte)'r';
            newCommentPacket[4] = (byte)'b';
            newCommentPacket[5] = (byte)'i';
            newCommentPacket[6] = (byte)'s';
            commentData.CopyTo(newCommentPacket, 7);

            // Write page 0 (identification header) unchanged
            pages[0].Write(outputStream);

            // Write page 1 with new comment data
            var commentPage = new OggPage
            {
                Version = pages[1].Version,
                HeaderType = pages[1].HeaderType,
                GranulePosition = pages[1].GranulePosition,
                SerialNumber = pages[1].SerialNumber,
                PageSequenceNumber = pages[1].PageSequenceNumber,
                SegmentTable = OggPage.BuildSegmentTable(newCommentPacket.Length),
                Data = newCommentPacket,
            };
            commentPage.Write(outputStream);

            // Write remaining pages with updated sequence numbers
            var seqDelta = commentPage.PageSequenceNumber + 1 - (pages.Count > 2 ? pages[2].PageSequenceNumber : 0);
            for (var i = 2; i < pages.Count; i++)
            {
                pages[i].PageSequenceNumber = (uint)(pages[i].PageSequenceNumber + seqDelta);
                pages[i].Write(outputStream);
            }

            return MediaTagResult.Success();
        }
        catch (Exception ex)
        {
            return MediaTagResult.Failure(MediaTagError.IoError, ex.Message);
        }
    }
}
