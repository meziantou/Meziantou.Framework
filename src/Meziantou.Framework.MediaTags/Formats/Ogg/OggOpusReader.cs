namespace Meziantou.Framework.MediaTags.Formats.Ogg;

internal sealed class OggOpusReader : IMediaTagReader
{
    private static readonly byte[] OpusTagsPrefix = "OpusTags"u8.ToArray();

    public MediaTagResult<MediaTagInfo> ReadTags(Stream stream)
    {
        try
        {
            stream.Position = 0;
            var tags = new MediaTagInfo();

            var pages = OggPacketUtilities.ReadAllPages(stream);
            var packets = OggPacketUtilities.ReadPackets(pages);
            foreach (var packet in packets)
            {
                if (packet.Data.AsSpan().StartsWith(OpusTagsPrefix))
                {
                    VorbisComment.VorbisCommentReader.TryParse(packet.Data.AsSpan(OpusTagsPrefix.Length), tags);
                    break;
                }
            }

            return MediaTagResult<MediaTagInfo>.Success(tags);
        }
        catch (Exception ex)
        {
            return MediaTagResult<MediaTagInfo>.Failure(MediaTagError.CorruptFile, ex.Message);
        }
    }
}
