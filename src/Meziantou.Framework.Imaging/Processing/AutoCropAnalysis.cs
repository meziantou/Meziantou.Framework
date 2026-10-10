namespace Meziantou.Framework.Imaging;

/// <summary>
/// The result of <see cref="ImageProcessingExtensions.AnalyzeAutoCrop(Image, AutoCropOptions?, CancellationToken)"/>: the
/// background and the content box detected in an image.
/// </summary>
/// <remarks>
/// An analysis is an immutable snapshot: it does not follow later changes of the image. It can be applied to any image of
/// the same canvas size with <see cref="ImageProcessingExtensions.AutoCrop(Image, AutoCropAnalysis, AutoCropOptions?, CancellationToken)"/>.
/// </remarks>
public sealed class AutoCropAnalysis
{
    internal AutoCropAnalysis(bool success, Size canvasSize, Rectangle bounds, Rgba64 backgroundColor, double weightX, double weightY)
    {
        Success = success;
        CanvasSize = canvasSize;
        Bounds = bounds;
        BackgroundColor = backgroundColor;
        WeightX = weightX;
        WeightY = weightY;
    }

    /// <summary>
    /// Gets a value indicating whether a border was found around a content box of at least 3x3 pixels. When
    /// <see langword="false"/>, an auto-crop leaves the image unchanged.
    /// </summary>
    public bool Success { get; }

    /// <summary>Gets the canvas size of the analyzed image.</summary>
    public Size CanvasSize { get; }

    /// <summary>
    /// Gets the bounding box of the content in every frame and in the poster, in stored-pixel coordinates, without padding.
    /// It is the whole canvas when <see cref="Success"/> is <see langword="false"/>.
    /// </summary>
    public Rectangle Bounds { get; }

    /// <summary>
    /// Gets the detected background: the first border pixel of the most frequent border color, widened exactly to 16 bits.
    /// A fully transparent background is transparent black.
    /// </summary>
    public Rgba64 BackgroundColor { get; }

    /// <summary>
    /// Gets the horizontal weight of the image, from -1 (left) to 1 (right): the mean over all pixels of the horizontal
    /// position of the pixel center relative to the canvas center, multiplied by the difference of the pixel from the
    /// background (0 to 1, scaled by alpha). It is 0 unless <see cref="AutoCropOptions.AnalyzeWeights"/> is set.
    /// </summary>
    public double WeightX { get; }

    /// <summary>Gets the vertical weight of the image, from -1 (top) to 1 (bottom). See <see cref="WeightX"/>.</summary>
    public double WeightY { get; }
}
