namespace Meziantou.Framework.Imaging;

// Inpainting
public static partial class ImageProcessingExtensions
{
    /// <summary>
    /// Fills the pixels of a mask of every frame of the image from the pixels around them: removes a scratch, a stain, a
    /// caption, a watermark or a small object, the front of known pixels moving inward from the boundary of the mask.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The method is the fast marching method of Telea (2004). The pixels of the mask are filled in the order of their
    /// distance to its boundary (the solution of the eikonal equation on the pixel grid), each one from the pixels already
    /// known or filled within <see cref="InpaintingOptions.Radius"/>: the weighted mean of their first-order extrapolations
    /// (their value plus their gradient times the offset), favoring the pixels that are close, in the direction the front
    /// moves, and at the same distance to the boundary. It continues the colors and the edges that reach the mask; it
    /// suits thin or small regions, and blurs the texture of large ones, which it does not reconstruct.
    /// </para>
    /// <para>
    /// The colors are filled premultiplied by alpha, in double precision, each channel clamped to the range of the pixels
    /// it is estimated from; the pixels outside the mask keep their stored values. The order of the pixels is total, so the
    /// result does not depend on the platform. Every displayed frame and the poster are filled with the same mask. The
    /// working memory (about 17 bytes per pixel, 32 bytes per pixel for a working copy of a frame, and the new frames) is
    /// charged to the image, and the operation is transactional across every frame and the poster. An empty mask leaves
    /// the image unchanged.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../../tests/Meziantou.Framework.Imaging.Processing.Tests/SmoothingTests.cs" ref:region="Inpaint_RemoveAScratch" ref:dedent="true">
    /// [Fact]
    /// public void Inpaint_RemoveAScratch()
    /// {
    ///     // A smooth gradient with a red scratch across it
    ///     using var photo = new Image&lt;Rgb24&gt;(40, 30);
    ///     for (var y = 0; y &lt; 30; y++)
    ///     {
    ///         for (var x = 0; x &lt; 40; x++)
    ///         {
    ///             photo.Frames[0][x, y] = new Rgb24((byte)(50 + (4 * x)), (byte)(40 + (5 * y)), 120);
    ///         }
    ///     }
    ///
    ///     var scratch = new PixelMask(photo.Size);
    ///     for (var x = 5; x &lt; 35; x++)
    ///     {
    ///         photo.Frames[0][x, 14] = new Rgb24(255, 0, 0);
    ///         photo.Frames[0][x, 15] = new Rgb24(255, 0, 0);
    ///         scratch[x, 14] = true;
    ///         scratch[x, 15] = true;
    ///     }
    ///
    ///     // The scratch is filled from the pixels around it: the gradient continues through it
    ///     photo.Inpaint(scratch);
    ///
    ///     for (var x = 5; x &lt; 35; x++)
    ///     {
    ///         Assert.InRange(photo.Frames[0][x, 14].R, 50 + (4 * x) - 1, 50 + (4 * x) + 1);
    ///         Assert.InRange(photo.Frames[0][x, 15].G, 40 + (5 * 15) - 1, 40 + (5 * 15) + 1);
    ///     }
    /// }
    /// </code>
    /// </example>
    /// <param name="image">The image to modify.</param>
    /// <param name="mask">The pixels to fill. It must have the size of the image, and leave at least one pixel out.</param>
    /// <param name="options">The radius, or <see langword="null"/> for <see cref="InpaintingOptions.Default"/>.</param>
    /// <param name="cancellationToken">The token checked during the filling of each frame and once more before the new frames replace the old ones.</param>
    /// <exception cref="ArgumentNullException"><paramref name="image"/> or <paramref name="mask"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="mask"/> does not have the size of the image, or holds every pixel.</exception>
    /// <exception cref="UnsupportedImageFeatureException">The pixels are CMYK inks, or a sample of a floating-point image is not a number. The image is unchanged.</exception>
    /// <exception cref="ImageResourceLimitException">The working memory and the new frames exceed the allocation limit. The image is unchanged.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled. The image is unchanged.</exception>
    /// <exception cref="InvalidOperationException">A pixel lease is active.</exception>
    /// <exception cref="ObjectDisposedException">The image is disposed.</exception>
    public static void Inpaint(this Image image, PixelMask mask, InpaintingOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(mask);
        ThrowIfCmyk(image.PixelFormat, nameof(Inpaint));
        ValidateMask(mask, image.Size, nameof(mask));
        image.InpaintCore(mask, options ?? InpaintingOptions.Default, cancellationToken);
    }
}
