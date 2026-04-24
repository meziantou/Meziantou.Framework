namespace Meziantou.Framework.MediaTags.Formats.Ogg;

internal sealed class OggOpusWriter : IMediaTagWriter
{
    private static readonly byte[] OpusTagsPrefix = "OpusTags"u8.ToArray();

    public MediaTagResult WriteTags(Stream inputStream, Stream outputStream, MediaTagInfo tags)
    {
        try
        {
            inputStream.Position = 0;

            var pages = OggPacketUtilities.ReadAllPages(inputStream);
            if (pages.Count < 2)
                return MediaTagResult.Failure(MediaTagError.CorruptFile, "OGG file has fewer than 2 pages.");

            // Build new comment data
            var commentData = VorbisComment.VorbisCommentWriter.Build(tags);

            // Prepend "OpusTags" header
            var newCommentPacket = new byte[8 + commentData.Length];
            newCommentPacket[0] = (byte)'O';
            newCommentPacket[1] = (byte)'p';
            newCommentPacket[2] = (byte)'u';
            newCommentPacket[3] = (byte)'s';
            newCommentPacket[4] = (byte)'T';
            newCommentPacket[5] = (byte)'a';
            newCommentPacket[6] = (byte)'g';
            newCommentPacket[7] = (byte)'s';
            commentData.CopyTo(newCommentPacket, 8);

            var outputPages = OggPacketUtilities.ReplacePacket(pages, OpusTagsPrefix, newCommentPacket);
            for (var i = 0; i < outputPages.Count; i++)
            {
                outputPages[i].Write(outputStream);
            }

            return MediaTagResult.Success();
        }
        catch (Exception ex)
        {
            return MediaTagResult.Failure(MediaTagError.IoError, ex.Message);
        }
    }
}
