namespace Meziantou.Framework.Imaging;

/// <summary>Selects the interpolation kernel used to resample pixels.</summary>
/// <remarks>When downsampling, kernel support is widened proportionally to the scale factor and coefficients are normalized.</remarks>
public enum ResamplingFilter
{
    /// <summary>Nearest-neighbor sampling (pixel centers, no blending).</summary>
    NearestNeighbor = 0,

    /// <summary>Bilinear (triangle) interpolation.</summary>
    Bilinear = 1,

    /// <summary>Bicubic Catmull-Rom interpolation. This is the default of <see cref="ResizeOptions.Filter"/>.</summary>
    Bicubic = 2,

    /// <summary>Three-lobe Lanczos windowed sinc interpolation.</summary>
    Lanczos3 = 3,
}
