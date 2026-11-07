namespace Meziantou.Framework.Imaging.Internals;

/// <summary>What happens to the frame region once the displayed frame was produced (APNG <c>dispose_op</c>).</summary>
internal enum AnimationDisposal
{
    /// <summary>The canvas is left as is (APNG <c>APNG_DISPOSE_OP_NONE</c>).</summary>
    None = 0,

    /// <summary>The region is cleared to transparent black (APNG <c>APNG_DISPOSE_OP_BACKGROUND</c>; never a GIF background color).</summary>
    ClearToTransparent = 1,

    /// <summary>The region is restored to its content before the frame (APNG <c>APNG_DISPOSE_OP_PREVIOUS</c>).</summary>
    RestorePrevious = 2,
}
