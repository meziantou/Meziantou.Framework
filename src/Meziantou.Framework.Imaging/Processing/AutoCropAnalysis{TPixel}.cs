namespace Meziantou.Framework.Imaging;

/// <summary>
/// The result of <see cref="ImageProcessingExtensions.AnalyzeAutoCrop{TPixel}(Image{TPixel}, AutoCropOptions?, CancellationToken)"/>:
/// the background, as a pixel of the image, and the content box detected in an <see cref="Image{TPixel}"/>.
/// </summary>
/// <typeparam name="TPixel">The pixel type of the analyzed image.</typeparam>
/// <remarks>See <see cref="AutoCropAnalysis"/> for the other members and the lifetime of an analysis.</remarks>
public sealed class AutoCropAnalysis<TPixel> : AutoCropAnalysis
    where TPixel : unmanaged
{
    internal AutoCropAnalysis(bool success, Size canvasSize, Rectangle bounds, Rgba64 backgroundColor, TPixel background, double weightX, double weightY)
        : base(success, canvasSize, bounds, backgroundColor, weightX, weightY)
    {
        BackgroundColor = background;
    }

    /// <summary>
    /// Gets the detected background: the first border pixel of the most frequent border color, as stored in the image. A
    /// fully transparent background is transparent black. The <see cref="AutoCropAnalysis.BackgroundColor"/> of the base
    /// type is the same color widened exactly to 16 bits.
    /// </summary>
    public new TPixel BackgroundColor { get; }
}
