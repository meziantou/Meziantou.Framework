using System.Buffers.Binary;

namespace Meziantou.Framework.MediaTags.Formats.Ogg;

internal sealed class OggOpusReader : IMediaTagReader
{
    private const int OpusSampleRate = 48_000;
    private const int OpusHeadPreSkipOffset = 10;
    private static readonly byte[] OpusTagsPrefix = "OpusTags"u8.ToArray();
    private static readonly byte[] OpusHeadPrefix = "OpusHead"u8.ToArray();

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

            tags.Duration ??= TryReadDuration(pages, packets);

            return MediaTagResult<MediaTagInfo>.Success(tags);
        }
        catch (Exception ex)
        {
            return MediaTagResult<MediaTagInfo>.Failure(MediaTagError.CorruptFile, ex.Message);
        }
    }

    private static TimeSpan? TryReadDuration(List<OggPage> pages, List<OggPacketInfo> packets)
    {
        OggPacketInfo? opusHeadPacket = null;
        foreach (var packet in packets)
        {
            if (packet.Data.AsSpan().StartsWith(OpusHeadPrefix))
            {
                opusHeadPacket = packet;
                break;
            }
        }

        if (opusHeadPacket is null || opusHeadPacket.Data.Length < OpusHeadPreSkipOffset + 2)
            return null;

        var preSkip = BinaryPrimitives.ReadUInt16LittleEndian(opusHeadPacket.Data.AsSpan(OpusHeadPreSkipOffset));
        if (opusHeadPacket.StartPageIndex < 0 || opusHeadPacket.StartPageIndex >= pages.Count)
            return null;

        var streamSerialNumber = pages[opusHeadPacket.StartPageIndex].SerialNumber;
        long? lastGranulePosition = null;
        for (var i = pages.Count - 1; i >= 0; i--)
        {
            var page = pages[i];
            if (page.SerialNumber != streamSerialNumber || page.GranulePosition < 0)
                continue;

            lastGranulePosition = page.GranulePosition;
            break;
        }

        if (lastGranulePosition is null || lastGranulePosition <= preSkip)
            return null;

        return TimeSpan.FromSeconds((lastGranulePosition.Value - preSkip) / (double)OpusSampleRate);
    }
}
