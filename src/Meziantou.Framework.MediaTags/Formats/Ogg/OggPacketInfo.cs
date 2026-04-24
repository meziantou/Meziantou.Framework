namespace Meziantou.Framework.MediaTags.Formats.Ogg;

internal sealed class OggPacketInfo
{
    public required byte[] Data { get; init; }
    public required int StartPageIndex { get; init; }
    public required int EndPageIndex { get; init; }
    public required bool StartsAtPageStart { get; init; }
    public required bool EndsAtPageEnd { get; init; }
    public required long FinalPageGranulePosition { get; init; }
}
