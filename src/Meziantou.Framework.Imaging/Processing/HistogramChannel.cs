namespace Meziantou.Framework.Imaging;

/// <summary>The sample a histogram counts (<see cref="ImageProcessingExtensions.ComputeHistogram(Image, HistogramChannel, CancellationToken)"/>).</summary>
public enum HistogramChannel
{
    /// <summary>
    /// The Rec. 709 luma of the encoded samples, at the storage precision: <c>Y = (2126 R + 7152 G + 722 B + 5000) / 10000</c>,
    /// the value <see cref="ImageProcessingExtensions.Grayscale(Image, CancellationToken)"/> stores. The luma of a gray pixel is its value.
    /// </summary>
    Luminance = 0,

    /// <summary>The red sample. A gray pixel has its value as red, green and blue samples.</summary>
    Red = 1,

    /// <summary>The green sample. A gray pixel has its value as red, green and blue samples.</summary>
    Green = 2,

    /// <summary>The blue sample. A gray pixel has its value as red, green and blue samples.</summary>
    Blue = 3,

    /// <summary>The alpha sample. A pixel format without alpha is opaque: every pixel is counted in the last bin.</summary>
    Alpha = 4,
}
