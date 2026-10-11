namespace Meziantou.Framework.Imaging.TestHarness.Convolution;

/// <summary>The parameters of a reference convolution (independent of the library types).</summary>
/// <param name="KernelWidth">The number of columns of the matrix (odd).</param>
/// <param name="KernelHeight">The number of rows of the matrix (odd).</param>
/// <param name="Weights">The weights, row by row from the top-left one.</param>
/// <param name="EdgeMode">The pixels read outside the image.</param>
/// <param name="PreserveAlpha">Whether colors are filtered straight and alpha is kept, instead of filtering premultiplied colors and alpha.</param>
/// <param name="Linear">Whether colors are filtered in linear light (sRGB transfer function of IEC 61966-2-1).</param>
public sealed record ReferenceConvolutionOptions(
    int KernelWidth,
    int KernelHeight,
    IReadOnlyList<decimal> Weights,
    ReferenceEdgeMode EdgeMode = ReferenceEdgeMode.Clamp,
    bool PreserveAlpha = false,
    bool Linear = false)
{
    /// <inheritdoc/>
    public override string ToString()
        => string.Create(CultureInfo.InvariantCulture, $"{KernelWidth}x{KernelHeight} kernel, {EdgeMode} edges{(PreserveAlpha ? ", alpha preserved" : "")}{(Linear ? ", linear" : "")}");
}
