namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The in-loop filters that the decoder applies to a picture (Rec. ITU-T H.265, clause 8.7 "In-loop filter process"). Every
/// picture is decoded with <see cref="All"/>; a subset is a diagnostic that isolates a stage of the decoding process.
/// </summary>
[Flags]
internal enum HevcInLoopFilters
{
    /// <summary>No filter: the picture constructed prior to the in-loop filter process.</summary>
    None = 0,

    /// <summary>The deblocking filter (clause 8.7.2).</summary>
    Deblocking = 1,

    /// <summary>The sample adaptive offset (clause 8.7.3).</summary>
    Sao = 2,

    /// <summary>Every filter of the decoding process.</summary>
    All = Deblocking | Sao,
}
