using Meziantou.Framework.MediaTags.Internals;

namespace Meziantou.Framework.MediaTags.Formats.Ogg;

internal sealed class OggVorbisWriter : IMediaTagWriter
{
    private static readonly byte[] VorbisCommentPrefix = [0x03, (byte)'v', (byte)'o', (byte)'r', (byte)'b', (byte)'i', (byte)'s'];

    /// <summary>
    /// The framing bit that terminates a Vorbis comment header.
    /// </summary>
    /// <remarks>
    /// libvorbis rejects the whole header when this byte is missing, so a file written without it is refused by
    /// every libvorbis-based player even though this library reads it back happily. Opus must not have one:
    /// an OpusTags packet ends at its last comment.
    /// </remarks>
    private const byte VorbisFramingBit = 0x01;

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

            // Build new comment data
            var commentData = VorbisComment.VorbisCommentWriter.Build(tags);

            // "\x03vorbis" header + comments + framing bit
            var newCommentPacket = new byte[VorbisCommentPrefix.Length + commentData.Length + 1];
            VorbisCommentPrefix.CopyTo(newCommentPacket, 0);
            commentData.CopyTo(newCommentPacket, VorbisCommentPrefix.Length);
            newCommentPacket[^1] = VorbisFramingBit;

            var outputPages = OggPacketUtilities.ReplacePacket(pages, VorbisCommentPrefix, newCommentPacket);
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
