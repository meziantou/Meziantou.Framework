namespace Meziantou.Framework.MediaTags.Formats.Ogg;

internal sealed class OggVorbisReader : IMediaTagReader
{
    private static readonly byte[] VorbisCommentPrefix = [0x03, (byte)'v', (byte)'o', (byte)'r', (byte)'b', (byte)'i', (byte)'s'];

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
                if (packet.Data.AsSpan().StartsWith(VorbisCommentPrefix))
                {
                    VorbisComment.VorbisCommentReader.TryParse(packet.Data.AsSpan(VorbisCommentPrefix.Length), tags);
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
