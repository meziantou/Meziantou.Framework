namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The post-processing filters that the decoder applies to a frame. Every frame is decoded with <see cref="All"/>; a subset is
/// a diagnostic that isolates a stage of the decoding process, which reference decoders can also output.
/// </summary>
[Flags]
internal enum Av1InLoopFilters
{
    /// <summary>No filter: the reconstructed frame, upscaled when the frame uses super-resolution.</summary>
    None = 0,

    /// <summary>The loop filter (section 7.14).</summary>
    Deblock = 1,

    /// <summary>The constrained directional enhancement filter (section 7.15).</summary>
    Cdef = 2,

    /// <summary>The loop restoration (section 7.17).</summary>
    Restoration = 4,

    /// <summary>Every filter of the decoding process.</summary>
    All = Deblock | Cdef | Restoration,
}
