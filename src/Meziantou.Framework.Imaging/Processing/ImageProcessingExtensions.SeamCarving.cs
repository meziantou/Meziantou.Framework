namespace Meziantou.Framework.Imaging;

// Content-aware resizing
public static partial class ImageProcessingExtensions
{
    /// <summary>
    /// Resizes every frame of the image to a new size while keeping its content: instead of scaling the image, seam carving
    /// removes (or duplicates) the connected paths of pixels across the image that hold the least detail, so that the
    /// subjects keep their proportions while the flat background shrinks or grows.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A vertical seam is a path of one pixel per row, each one at most one column away from the one above (a horizontal seam,
    /// one pixel per column). The width changes first, one vertical seam at a time, then the height, with horizontal seams.
    /// The seam removed is the one whose removal brings together the least different pixels (the forward energy of
    /// Rubinstein, Shamir and Avidan, 2008, on the Rec. 709 luma premultiplied by alpha and on the alpha, at the storage
    /// precision). To enlarge, the seams a reduction would remove are found first, then each one is duplicated: a pixel
    /// blended from the seam pixel and its neighbor is inserted next to it (Avidan and Shamir, 2007); an enlargement adds at
    /// most half of the current size at a time. Pixels that are only moved keep their stored values; blended pixels are
    /// computed in single precision, premultiplied by alpha, and rounded to the pixel format.
    /// </para>
    /// <para>
    /// The seams are computed once from the sum of the energies of every displayed frame and the poster, and removed from all
    /// of them, so that an animation stays aligned. <see cref="SeamCarvingOptions.ProtectedRegion"/> keeps pixels as long as
    /// possible and <see cref="SeamCarvingOptions.RemovedRegion"/> removes pixels first: a seam is compared by the number of
    /// protected pixels minus the number of pixels to remove that it crosses, then by its energy. The costs are integers and
    /// the ties are broken in a fixed order, so the result does not depend on the platform.
    /// </para>
    /// <para>
    /// The time is proportional to the number of seams times the number of pixels times the number of frames. The working
    /// memory (about 8 bytes per pixel and frame for the features, and 13 bytes per pixel for the search, plus two working
    /// copies of a frame) is charged to the image. The operation is transactional across every frame and the poster. A
    /// cursor hotspot follows its pixel; the operation fails when a seam removes it.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../../tests/Meziantou.Framework.Imaging.Processing.Tests/ResizeTests.cs" ref:region="SeamCarve_KeepTheSubjects" ref:dedent="true">
    /// [Fact]
    /// public void SeamCarve_KeepTheSubjects()
    /// {
    ///     // A flat sky with two detailed subjects, 8 pixels wide each
    ///     using var image = new Image&lt;Rgb24&gt;(40, 20, new Rgb24(120, 170, 230));
    ///     for (var y = 4; y &lt; 16; y++)
    ///     {
    ///         for (var x = 0; x &lt; 8; x++)
    ///         {
    ///             var level = (byte)(40 + (45 * (((7 * x) + (3 * y)) % 5)));
    ///             var color = new Rgb24(level, (byte)(level / 2), 20);
    ///             image.Frames[0][4 + x, y] = color;
    ///             image.Frames[0][28 + x, y] = color;
    ///         }
    ///     }
    ///
    ///     // 16 seams go through the sky: the subjects keep their size, they only get closer to each other
    ///     image.SeamCarve(24, 20);
    ///
    ///     Assert.Equal(new Size(24, 20), image.Size);
    ///     var subjectPixels = 0;
    ///     for (var y = 0; y &lt; 20; y++)
    ///     {
    ///         for (var x = 0; x &lt; 24; x++)
    ///         {
    ///             if (image.Frames[0][x, y] != new Rgb24(120, 170, 230))
    ///             {
    ///                 subjectPixels++;
    ///             }
    ///         }
    ///     }
    ///
    ///     Assert.Equal(2 * 8 * 12, subjectPixels);
    /// }
    /// </code>
    /// </example>
    /// <param name="image">The image to modify.</param>
    /// <param name="width">The new width. Must be positive.</param>
    /// <param name="height">The new height. Must be positive.</param>
    /// <param name="options">The regions to protect and to remove, or <see langword="null"/> for none.</param>
    /// <param name="cancellationToken">The token checked between seams, between frames and once more before the new frames replace the old ones.</param>
    /// <exception cref="ArgumentNullException"><paramref name="image"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="width"/> or <paramref name="height"/> is not positive.</exception>
    /// <exception cref="ArgumentException">A mask of <paramref name="options"/> does not have the size of the image.</exception>
    /// <exception cref="UnsupportedImageFeatureException">The pixels are CMYK inks, or a seam removes the pixel of a cursor hotspot. The image is unchanged.</exception>
    /// <exception cref="ImageResourceLimitException">The new canvas exceeds the size limits, or the working memory and the new frames exceed the allocation limit. The image is unchanged.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled. The image is unchanged.</exception>
    /// <exception cref="InvalidOperationException">A pixel lease is active.</exception>
    /// <exception cref="ObjectDisposedException">The image is disposed.</exception>
    public static void SeamCarve(this Image image, int width, int height, SeamCarvingOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);
        ThrowIfCmyk(image.PixelFormat, nameof(SeamCarve));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        options ??= SeamCarvingOptions.Default;
        ValidateMask(options.ProtectedRegion, image.Size, nameof(options));
        ValidateMask(options.RemovedRegion, image.Size, nameof(options));
        image.SeamCarveCore(new Size(width, height), options, cancellationToken);
    }

    private static void ValidateMask(PixelMask? mask, Size size, string paramName)
    {
        if (mask is not null && mask.Size != size)
            throw new ArgumentException(string.Create(CultureInfo.InvariantCulture, $"The mask is {mask.Width}x{mask.Height} and the image is {size.Width}x{size.Height}: a mask must have the size of the image."), paramName);
    }
}
