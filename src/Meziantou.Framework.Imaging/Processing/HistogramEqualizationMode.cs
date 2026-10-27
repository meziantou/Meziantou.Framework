namespace Meziantou.Framework.Imaging;

/// <summary>The algorithm of a histogram equalization (<see cref="HistogramEqualizationOptions.Mode"/>).</summary>
public enum HistogramEqualizationMode
{
    /// <summary>
    /// One mapping for the whole frame, from its cumulative histogram: a level <c>v</c> becomes
    /// <c>round((cdf(v) - cdf_min) * max / (N - cdf_min))</c>, where <c>cdf(v)</c> is the number of counted samples at most
    /// <c>v</c>, <c>cdf_min</c> the number at the lowest level present, <c>N</c> the number of counted samples and <c>max</c>
    /// the largest sample value (255 or 65,535).
    /// </summary>
    Global = 0,

    /// <summary>
    /// Contrast-limited adaptive histogram equalization (CLAHE): the frame is divided into a grid of tiles
    /// (<see cref="HistogramEqualizationOptions.TileCountX"/>, <see cref="HistogramEqualizationOptions.TileCountY"/>), each
    /// tile gets the mapping of its own histogram, clipped at <see cref="HistogramEqualizationOptions.ClipLimit"/>, and every
    /// pixel gets the bilinear interpolation of the mappings of the four tiles whose centers surround it.
    /// </summary>
    Adaptive = 1,
}
