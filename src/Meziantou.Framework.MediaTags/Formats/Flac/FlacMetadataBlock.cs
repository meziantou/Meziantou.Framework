namespace Meziantou.Framework.MediaTags.Formats.Flac;

internal readonly struct FlacMetadataBlock
{
    public bool IsLast { get; init; }
    public byte BlockType { get; init; }
    public byte[] Data { get; init; }

    // Block types
    public const byte StreamInfo = 0;
    public const byte Padding = 1;
    public const byte Application = 2;
    public const byte SeekTable = 3;
    public const byte VorbisCommentType = 4;
    public const byte CueSheet = 5;
    public const byte Picture = 6;
}
