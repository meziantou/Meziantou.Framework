namespace Meziantou.Framework.Imaging;

/// <summary>
/// Immutable options of a histogram equalization
/// (<see cref="ImageProcessingExtensions.Equalize(Image, HistogramEqualizationOptions?, CancellationToken)"/>).
/// </summary>
/// <remarks>
/// <para>
/// The histograms have one bin per sample level: 256 for 8-bit pixel formats, 65,536 for 16-bit ones, so no precision is
/// lost. Fully transparent pixels are not counted (the color they hide does not take part in the contrast), but they are
/// remapped like the others.
/// </para>
/// <para>
/// The adaptive mode clips the histogram of a tile in 256 bins whatever the depth, as MATLAB's <c>adapthisteq</c> and
/// scikit-image's <c>equalize_adapthist</c> do by default, so that <see cref="ClipLimit"/> controls 16-bit frames as it
/// controls their 8-bit versions. A bin of 16-bit samples covers 256 levels: the samples it keeps after the clip follow the
/// histogram of its levels, and the samples it gets from the excess are spread evenly over its levels, so the mapping
/// keeps every level. Without limit, the mapping is the one of the histogram of the 65,536 levels.
/// </para>
/// <para>
/// The adaptive mode follows the contrast-limited adaptive histogram equalization of K. Zuiderveld ("Contrast Limited
/// Adaptive Histogram Equalization", Graphics Gems IV, 1994): the mapping of a tile is
/// <c>round(cdf(v) * max / n)</c>, where <c>n</c> is the number of counted samples of the tile, after its histogram is
/// clipped, rounded to the nearest level. The tables are interpolated at the center of each pixel, ties rounded upward.
/// On 8-bit frames whose dimensions are multiples of the tile counts, the tables are those of OpenCV's
/// <c>createCLAHE</c> with the same clip limit and tile grid, but OpenCV interpolates them at the top-left corner of each
/// pixel: the results differ by a fraction of the difference between neighboring tiles, a few levels. OpenCV also pads
/// the other frames by reflection to whole tiles, where the tiles of the library divide the frame as evenly as possible.
/// OpenCV clips 16-bit histograms in 65,536 bins, where the limit of a tile of fewer than <c>65,536 / ClipLimit</c>
/// samples is one sample per level: most clip limits then give the same result.
/// </para>
/// </remarks>
public sealed class HistogramEqualizationOptions
{
    /// <summary>The largest number of tiles along an axis.</summary>
    public const int MaxTileCount = 256;

    /// <summary>Gets the default options: a global equalization of the luminance of each frame.</summary>
    public static HistogramEqualizationOptions Default { get; } = new();

    /// <summary>Gets the algorithm. Defaults to <see cref="HistogramEqualizationMode.Global"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined <see cref="HistogramEqualizationMode"/>.</exception>
    public HistogramEqualizationMode Mode
    {
        get;
        init
        {
            if (!Enum.IsDefined(value))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The equalization mode is not valid.");

            field = value;
        }
    }

    /// <summary>Gets the samples that are remapped. Defaults to <see cref="HistogramColorMode.Luminance"/>, which keeps the hues.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined <see cref="HistogramColorMode"/>.</exception>
    public HistogramColorMode ColorMode
    {
        get;
        init
        {
            if (!Enum.IsDefined(value))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The color mode is not valid.");

            field = value;
        }
    }

    /// <summary>
    /// Gets the number of columns of tiles of the adaptive mode, from 1 to <see cref="MaxTileCount"/>. Defaults to 8. A frame
    /// narrower than that has one column of tiles per pixel column. Tile <c>k</c> covers the pixel columns from
    /// <c>floor(k * width / count)</c> included to <c>floor((k + 1) * width / count)</c> excluded. Ignored by the global mode.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not between 1 and <see cref="MaxTileCount"/>.</exception>
    public int TileCountX
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, MaxTileCount);
            field = value;
        }
    } = 8;

    /// <summary>
    /// Gets the number of rows of tiles of the adaptive mode, from 1 to <see cref="MaxTileCount"/>. Defaults to 8. A frame
    /// shorter than that has one row of tiles per pixel row. Ignored by the global mode.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not between 1 and <see cref="MaxTileCount"/>.</exception>
    public int TileCountY
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, MaxTileCount);
            field = value;
        }
    } = 8;

    /// <summary>
    /// Gets the contrast limit of the adaptive mode, as a multiple of the average bin height of a tile. Defaults to 2. The
    /// histogram of a tile is clipped in 256 bins, each one 8-bit level or 256 16-bit levels: a bin of a tile of <c>n</c>
    /// counted samples holds at most <c>max(1, floor(ClipLimit * n / 256))</c> samples; the excess is spread over every
    /// bin as Zuiderveld does: each bin gets the same share, then the remainder <c>r</c> of the division goes one sample at
    /// a time to every <c>max(1, floor(256 / r))</c>-th bin, from bin 0. For 8-bit samples, this is the clip limit of
    /// OpenCV's <c>createCLAHE</c>. A tile of fewer than <c>256 / ClipLimit</c> samples is limited to one sample per bin,
    /// so close clip limits can give the same result on small tiles. <see cref="double.PositiveInfinity"/> disables the
    /// limit (plain adaptive histogram equalization). Ignored by the global mode.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not positive, or is not a number.</exception>
    public double ClipLimit
    {
        get;
        init
        {
            if (!(value > 0))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The clip limit must be positive.");

            field = value;
        }
    } = 2;

    /// <summary>
    /// Gets a value indicating whether every frame of an image, and its poster frame, is remapped with the histograms of all
    /// of them together, so that the frames of an animation get the same mapping and do not flicker. Defaults to
    /// <see langword="false"/>: each frame is equalized alone. Ignored when one frame is equalized.
    /// </summary>
    public bool ShareHistogramAcrossFrames { get; init; }
}
