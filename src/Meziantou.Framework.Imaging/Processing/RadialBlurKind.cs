namespace Meziantou.Framework.Imaging;

/// <summary>The direction in which <see cref="ImageProcessingExtensions.RadialBlur(Image, RadialBlurOptions, CancellationToken)"/> blurs every pixel.</summary>
public enum RadialBlurKind
{
    /// <summary>Along the line through the center: a zoom during the exposure, sharp at the center and streaked toward the edges.</summary>
    Zoom = 0,

    /// <summary>Along the circle around the center: a rotation during the exposure, sharp at the center and swirled toward the edges.</summary>
    Spin = 1,
}
