namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Classifies the library-controlled bytes charged to an <see cref="AllocationScope"/>. The kind
/// only feeds diagnostics; every kind counts toward the same <see cref="ImageResourceLimits.MaxLiveAllocationBytes"/> limit.
/// </summary>
internal enum AllocationKind
{
    /// <summary>Pixel storage of image frames and poster frames.</summary>
    ImagePixels = 0,

    /// <summary>Decoder working state (scan lines, coefficient buffers, decompression windows, LZW tables...).</summary>
    DecoderState = 1,

    /// <summary>Animation compositor canvas.</summary>
    CompositorState = 2,

    /// <summary>Saved region restored by a "restore to previous" disposal instruction.</summary>
    RestorePreviousState = 3,

    /// <summary>Temporary storage of one operation (resampling windows, encoder scratch...).</summary>
    Temporary = 4,

    /// <summary>Decompressed or retained metadata (profiles, text, EXIF, XMP).</summary>
    Metadata = 5,
}
