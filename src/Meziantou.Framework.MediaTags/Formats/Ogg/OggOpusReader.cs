using System.Buffers.Binary;
using Meziantou.Framework.MediaTags.Internals;

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

            // Both header packets are at the front of the stream, so only the leading pages are read.
            stream.Position = 0;
            OggPacketUtilities.TryFindHeaderPacket(stream, OpusHeadPrefix, out var opusHead);

            stream.Position = 0;
            if (OggPacketUtilities.TryFindHeaderPacket(stream, OpusTagsPrefix, out var opusTags))
            {
                VorbisComment.VorbisCommentReader.TryParse(opusTags.AsSpan(OpusTagsPrefix.Length), tags);
            }

            if (opusHead is not null)
            {
                tags.Duration ??= TryReadDuration(stream, opusHead);
            }

            return MediaTagResult<MediaTagInfo>.Success(tags);
        }
        catch (Exception ex) when (MediaTagErrors.TryMap(ex, out var error))
        {
            return MediaTagResult<MediaTagInfo>.Failure(error, ex.Message);
        }
    }

    /// <summary>
    /// Derives the duration from the granule position of the last page, which is found by searching backwards
    /// from the end of the file rather than by reading every page.
    /// </summary>
    private static TimeSpan? TryReadDuration(Stream stream, byte[] opusHead)
    {
        if (opusHead.Length < OpusHeadPreSkipOffset + 2)
            return null;

        var preSkip = BinaryPrimitives.ReadUInt16LittleEndian(opusHead.AsSpan(OpusHeadPreSkipOffset));

        stream.Position = 0;
        var firstPage = OggPage.Read(stream);
        if (firstPage is null)
            return null;

        if (!OggPacketUtilities.TryGetLastGranulePosition(stream, firstPage.SerialNumber, out var lastGranulePosition))
            return null;

        if (lastGranulePosition <= preSkip)
            return null;

        return TimeSpan.FromSeconds((lastGranulePosition - preSkip) / (double)OpusSampleRate);
    }
}
