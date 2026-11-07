using System.Runtime.InteropServices;

namespace Meziantou.Framework.MediaTags.Formats.Flac;

/// <summary>
/// The location of a metadata block in the input file. Blocks are described rather than buffered: a picture
/// block reaches 16 MB, and the writer only ever copies it straight through to the output.
/// </summary>
[StructLayout(LayoutKind.Auto)]
internal readonly struct FlacMetadataBlock
{
    public byte BlockType { get; init; }
    public long Position { get; init; }
    public int Size { get; init; }

    /// <summary>The largest size a FLAC metadata block header can express.</summary>
    public const int MaxSize = 0xFFFFFF;

    /// <summary>The maximum number of metadata blocks read from one file.</summary>
    /// <remarks>Real files hold a handful; this bounds the memory a malformed file can make the parser retain.</remarks>
    public const int MaxCount = 4096;

    // Block types
    public const byte StreamInfo = 0;
    public const byte Padding = 1;
    public const byte Application = 2;
    public const byte SeekTable = 3;
    public const byte VorbisCommentType = 4;
    public const byte CueSheet = 5;
    public const byte Picture = 6;
}
