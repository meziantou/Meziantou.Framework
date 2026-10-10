namespace Meziantou.Framework.Imaging.Formats;

/// <summary>Selects between static PNG and animated PNG (APNG) output.</summary>
public enum PngAnimationMode
{
    /// <summary>Writes an APNG when the image is animated (<see cref="Image.IsAnimated"/>), and a static PNG otherwise.</summary>
    Auto = 0,

    /// <summary>Writes a static PNG. Animated images (including single-frame images with animation settings or a poster frame) are rejected with an <see cref="UnsupportedImageFeatureException"/>; frames are never silently dropped.</summary>
    Static = 1,

    /// <summary>Always writes an APNG, even for a single-frame still image.</summary>
    Animated = 2,
}
