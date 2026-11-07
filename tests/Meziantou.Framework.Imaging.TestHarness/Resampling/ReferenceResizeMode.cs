namespace Meziantou.Framework.Imaging.TestHarness.Resampling;

/// <summary>How the reference resampler interprets the target size.</summary>
public enum ReferenceResizeMode
{
    /// <summary>Scale by <c>min(tw / sw, th / sh)</c> (at most 1 without upscaling) and round the other side to nearest, ties up, at least 1.</summary>
    Contain,

    /// <summary>Exactly the target size, each axis scaled independently.</summary>
    Stretch,

    /// <summary>Scale by <c>max(tw / sw, th / sh)</c>, exactly the target size, the overflow cropped at the anchor.</summary>
    Cover,
}
