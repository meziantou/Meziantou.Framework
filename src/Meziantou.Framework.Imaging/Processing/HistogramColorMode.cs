namespace Meziantou.Framework.Imaging;

/// <summary>
/// The samples a histogram-based tone operation (<see cref="ImageProcessingExtensions.Equalize(Image, HistogramEqualizationOptions?, CancellationToken)"/>,
/// <see cref="ImageProcessingExtensions.AutoLevels(Image, AutoLevelsOptions?, CancellationToken)"/>) remaps.
/// </summary>
/// <remarks>Gray pixel formats have one sample per pixel: both modes give the same result.</remarks>
public enum HistogramColorMode
{
    /// <summary>
    /// The operation remaps the Rec. 709 luma of the pixels (<see cref="HistogramChannel.Luminance"/>) and adds the change of
    /// luma, <c>Y' - Y</c>, to the red, green and blue samples, clamped to the sample range. The chroma of the pixels (the
    /// differences <c>B - Y</c> and <c>R - Y</c>) is kept, so the hues do not shift, except where a sample is clamped.
    /// </summary>
    Luminance = 0,

    /// <summary>
    /// The operation remaps the red, green and blue samples independently, each from its own histogram. The balance of the
    /// channels changes, and the hues with it: an auto-levels in this mode also neutralizes a color cast.
    /// </summary>
    PerChannel = 1,
}
