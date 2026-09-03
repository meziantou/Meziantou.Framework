using Meziantou.Framework.MediaTags.Internals;

namespace Meziantou.Framework.MediaTags.Formats.Ogg;

internal sealed class OggOpusWriter : IMediaTagWriter
{
    private static readonly byte[] OpusTagsPrefix = "OpusTags"u8.ToArray();

    public MediaTagResult WriteTags(Stream inputStream, Stream outputStream, MediaTagInfo tags, MediaTagWriteOptions options)
    {
        try
        {
            inputStream.Position = 0;

            var pages = OggPacketUtilities.ReadAllPages(inputStream);
            if (pages.Count < 2)
                return MediaTagResult.Failure(MediaTagError.CorruptFile, "OGG file has fewer than 2 pages.");

            if (OggPacketUtilities.GetSingleStreamSerialNumber(pages) is null)
                return MediaTagResult.Failure(MediaTagError.UnsupportedFormat, "Multiplexed or chained OGG streams are not supported.");

            // Build new comment data. An OpusTags packet has no framing bit, unlike a Vorbis comment header.
            var commentData = VorbisComment.VorbisCommentWriter.Build(tags);

            var newCommentPacket = new byte[OpusTagsPrefix.Length + commentData.Length];
            OpusTagsPrefix.CopyTo(newCommentPacket, 0);
            commentData.CopyTo(newCommentPacket, OpusTagsPrefix.Length);

            var outputPages = OggPacketUtilities.ReplacePacket(pages, OpusTagsPrefix, newCommentPacket);
            for (var i = 0; i < outputPages.Count; i++)
            {
                outputPages[i].Write(outputStream);
            }

            return MediaTagResult.Success();
        }
        catch (Exception ex) when (MediaTagErrors.TryMap(ex, out var error))
        {
            return MediaTagResult.Failure(error, ex.Message);
        }
    }
}
