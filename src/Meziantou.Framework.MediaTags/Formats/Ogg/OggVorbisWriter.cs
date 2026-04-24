namespace Meziantou.Framework.MediaTags.Formats.Ogg;

internal sealed class OggVorbisWriter : IMediaTagWriter
{
    private static readonly byte[] VorbisCommentPrefix = [0x03, (byte)'v', (byte)'o', (byte)'r', (byte)'b', (byte)'i', (byte)'s'];

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

            var outputPages = OggPacketUtilities.ReplacePacket(pages, VorbisCommentPrefix, newCommentPacket);
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
