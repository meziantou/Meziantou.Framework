namespace Meziantou.Framework.Imaging;

// The content-aware crop suggestion
public static partial class ImageProcessingExtensions
{
    /// <summary>
    /// Suggests where to crop the image to an aspect ratio while keeping its content: the rectangle of the ratio, as large
    /// as the image allows (or scaled by <see cref="CropSuggestionOptions.Scale"/>), that holds the most detail or the most
    /// salient colors of every displayed frame and the poster frame. Nothing is changed: pass the rectangle to
    /// <see cref="Crop(Image, Rectangle, CancellationToken)"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every pixel gets an energy (<see cref="CropSuggestionOptions.Energy"/>): the differences of its luma and alpha with
    /// its right and lower neighbors, or how much its color stands out from the mean color of the frame (the frequency-tuned
    /// saliency of Achanta et al., in Oklab). The energies of the frames are added, and the rectangle of the requested size
    /// with the largest total wins. The energies are integers (from 16-bit samples, or saliencies rounded to 1/65,536), so
    /// the totals are exact and the suggestion is deterministic: among equal totals, the rectangle nearest to the center of
    /// the image wins, then the topmost, then the leftmost; an image without detail gets the centered crop.
    /// </para>
    /// <para>
    /// The size of the crop is the largest rectangle of the ratio inside the image, its other side rounded to the nearest
    /// whole pixel: a 4000×3000 image gets 3000×3000 for 1:1 and 4000×2250 for 16:9. Colors are read premultiplied by
    /// alpha, so transparent areas have no energy; floating-point samples are clamped to [0, 1] and must be finite. The
    /// time is linear in the number of pixels. A summed-area table of 8 bytes per pixel is charged to the image, with row
    /// buffers. The frames are read on the calling thread.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../../tests/Meziantou.Framework.Imaging.Processing.Tests/AutoCropTests.cs" ref:region="SuggestCrop_KeepTheSubject" ref:dedent="true">
    /// [Fact]
    /// public void SuggestCrop_KeepTheSubject()
    /// {
    ///     // A wide photo: a flat sky, with a detailed subject on the right
    ///     using var image = new Image&lt;Rgb24&gt;(160, 90, new Rgb24(120, 170, 230));
    ///     for (var y = 30; y &lt; 80; y++)
    ///     {
    ///         for (var x = 110; x &lt; 150; x++)
    ///         {
    ///             image.Frames[0][x, y] = (x + y) % 3 == 0 ? new Rgb24(40, 30, 20) : new Rgb24(200, 180, 90);
    ///         }
    ///     }
    ///
    ///     // The square crop keeps the subject instead of the middle of the sky
    ///     var crop = image.SuggestCrop(1, 1);
    ///     Assert.Equal(new Size(90, 90), crop.Size);
    ///     Assert.True(crop.X &lt;= 110 &amp;&amp; crop.Right &gt;= 150, $"The crop {crop} cuts the subject");
    ///
    ///     image.Crop(crop);
    ///     Assert.Equal(new Size(90, 90), image.Size);
    /// }
    /// </code>
    /// </example>
    /// <param name="image">The image.</param>
    /// <param name="aspectWidth">The width of the aspect ratio, such as 16 for 16:9. Must be positive.</param>
    /// <param name="aspectHeight">The height of the aspect ratio, such as 9 for 16:9. Must be positive.</param>
    /// <param name="options">The energy and the scale of the crop, or <see langword="null"/> for the largest crop that keeps the most detail.</param>
    /// <param name="cancellationToken">The token checked between rows.</param>
    /// <returns>The suggested rectangle, inside the image.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="image"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="aspectWidth"/> or <paramref name="aspectHeight"/> is not positive.</exception>
    /// <exception cref="ImageResourceLimitException">The table or the buffers exceed a limit.</exception>
    /// <exception cref="InvalidOperationException">A pixel lease is active.</exception>
    /// <exception cref="ObjectDisposedException">The image is disposed.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    /// <exception cref="UnsupportedImageFeatureException">A floating-point sample is not finite.</exception>
    public static Rectangle SuggestCrop(this Image image, int aspectWidth, int aspectHeight, CropSuggestionOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(aspectWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(aspectHeight);
        return image.SuggestCropFrames(frames: null, aspectWidth, aspectHeight, options ?? new CropSuggestionOptions(), cancellationToken);
    }

    /// <summary>
    /// Suggests where to crop one frame to an aspect ratio while keeping its content. See
    /// <see cref="SuggestCrop(Image, int, int, CropSuggestionOptions?, CancellationToken)"/>.
    /// </summary>
    /// <param name="frame">The frame.</param>
    /// <param name="aspectWidth">The width of the aspect ratio, such as 16 for 16:9. Must be positive.</param>
    /// <param name="aspectHeight">The height of the aspect ratio, such as 9 for 16:9. Must be positive.</param>
    /// <param name="options">The energy and the scale of the crop, or <see langword="null"/> for the largest crop that keeps the most detail.</param>
    /// <param name="cancellationToken">The token checked between rows.</param>
    /// <returns>The suggested rectangle, inside the frame.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="frame"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="aspectWidth"/> or <paramref name="aspectHeight"/> is not positive.</exception>
    /// <exception cref="ImageResourceLimitException">The table or the buffers exceed a limit.</exception>
    /// <exception cref="InvalidOperationException">A pixel lease is active.</exception>
    /// <exception cref="ObjectDisposedException">The frame is no longer attached to a live image.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    /// <exception cref="UnsupportedImageFeatureException">A floating-point sample is not finite.</exception>
    public static Rectangle SuggestCrop(this ImageFrame frame, int aspectWidth, int aspectHeight, CropSuggestionOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(aspectWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(aspectHeight);
        _ = frame.GetStorage();
        return frame.OwnerImage.SuggestCropFrames([frame], aspectWidth, aspectHeight, options ?? new CropSuggestionOptions(), cancellationToken);
    }
}
