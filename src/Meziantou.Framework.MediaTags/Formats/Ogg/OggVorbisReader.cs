using Meziantou.Framework.MediaTags.Internals;

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

            // The comment packet is one of the first packets of the stream, so only the leading pages are read.
            if (OggPacketUtilities.TryFindHeaderPacket(stream, VorbisCommentPrefix, out var commentPacket))
            {
                VorbisComment.VorbisCommentReader.TryParse(commentPacket.AsSpan(VorbisCommentPrefix.Length), tags);
            }

            return MediaTagResult<MediaTagInfo>.Success(tags);
        }
        catch (Exception ex) when (MediaTagErrors.TryMap(ex, out var error))
        {
            return MediaTagResult<MediaTagInfo>.Failure(error, ex.Message);
        }
    }
}
