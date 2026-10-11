namespace Meziantou.Framework.Imaging.Internals;

/// <summary>What <see cref="JpegXlCodestreamWalker.Feed"/> reached.</summary>
internal enum JpegXlCodestreamEvent
{
    /// <summary>More bytes are needed.</summary>
    None,

    /// <summary>The image header, and the ICC profile when there is one, are read.</summary>
    ImageHeader,

    /// <summary>The header and the table of contents of a frame are read; the caller chooses whether its sections are buffered.</summary>
    FrameHeader,

    /// <summary>The sections of the frame were all received (or skipped).</summary>
    FrameComplete,
}
