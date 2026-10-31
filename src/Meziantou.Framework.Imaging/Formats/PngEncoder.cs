using System.IO.Compression;

namespace Meziantou.Framework.Imaging.Formats;

/// <summary>Immutable settings for lossless PNG and APNG encoding.</summary>
/// <remarks>
/// <para>
/// By default the PNG color type and bit depth follow the pixel format: grayscale for <see cref="Gray8"/>/<see cref="Gray16"/>,
/// grayscale with alpha for <see cref="La16"/>/<see cref="La32"/> (and <see cref="A8"/>, written as black with its alpha),
/// RGB for <see cref="Rgb24"/>/<see cref="Rgb48"/>, RGBA for <see cref="Rgba32"/>/<see cref="Bgra32"/>/<see cref="Rgba64"/>,
/// and 16-bit samples for the 16-bit pixel formats. <see cref="PngPixelLayout.Indexed"/> writes a palette instead, for the
/// images whose colors it can hold exactly. <see cref="PngOptimization.MinimumSize"/> chooses the layout, the filters and
/// the animation frame rectangles that give the smallest file instead. Encoding never reduces precision, never reduces
/// colors and never drops alpha.
/// </para>
/// <para>
/// Animated output is written with freshly computed control data: full-canvas frames by default, or the rectangles that
/// changed with <see cref="OptimizeFrames"/> or <see cref="PngOptimization.MinimumSize"/>; delta information from a
/// decoded source is never reused. PNG
/// and APNG writers require <see cref="ImageWriterOptions.ExpectedFrameCount"/>.
/// </para>
/// <para>
/// The output is always a standard PNG file: Apple's CgBI variant (iOS-optimized PNG), which the decoder reads, is never
/// written.
/// </para>
/// </remarks>
public sealed class PngEncoder : ImageEncoder
{
    /// <inheritdoc />
    public override ImageFormat Format => ImageFormat.Png;

    /// <inheritdoc />
    internal override bool StoresPixelFormat(PixelFormat format) => format is PixelFormat.La16 or PixelFormat.La32 or PixelFormat.Rgb48;

    /// <summary>Gets the animation mode. Defaults to <see cref="PngAnimationMode.Auto"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined <see cref="PngAnimationMode"/>.</exception>
    public PngAnimationMode AnimationMode
    {
        get;
        init
        {
            if (!Enum.IsDefined(value))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The animation mode is not valid.");

            field = value;
        }
    }

    /// <summary>Gets the pixel layout. Defaults to <see cref="PngPixelLayout.Auto"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined <see cref="PngPixelLayout"/>.</exception>
    public PngPixelLayout PixelLayout
    {
        get;
        init
        {
            if (!Enum.IsDefined(value))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The pixel layout is not valid.");

            field = value;
        }
    }

    /// <summary>
    /// Gets a value indicating whether <see cref="PngPixelLayout.Indexed"/> may reduce the colors of an image that its palette
    /// cannot store exactly: more than 256 distinct colors (or than <see cref="QuantizeOptions.MaxColors"/>), colors that are
    /// not in <see cref="QuantizeOptions.Palette"/>, 16-bit samples that 8 bits cannot represent, or, for a sequential writer,
    /// a later frame with colors that are not in the palette of the first image. Defaults to <see langword="false"/>: such an
    /// image is rejected with an <see cref="UnsupportedImageFeatureException"/>, as without this setting. When
    /// <see langword="true"/>, the colors are reduced as <see cref="Quantization"/> says. An image whose colors fit is always
    /// stored exactly. Ignored by the other layouts.
    /// </summary>
    public bool AllowColorReduction { get; init; }

    /// <summary>
    /// Gets how <see cref="PngPixelLayout.Indexed"/> reduces colors when <see cref="AllowColorReduction"/> is set. Defaults
    /// to <see cref="QuantizeOptions.Default"/>: at most 256 entries, without dithering. Ignored otherwise.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A PNG file has one palette, written before its first image. An eager save computes it from every frame and the poster
    /// (a deterministic variance-based median cut of their colors, see <see cref="PaletteBuilder"/>, or
    /// <see cref="QuantizeOptions.Palette"/>), and a sequential writer from the first image it is given; every image then maps
    /// to it, with the dithering of <see cref="QuantizeOptions.Dithering"/>, as
    /// <see cref="ImageProcessingExtensions.Quantize(Image, QuantizeOptions?, CancellationToken)"/> does. Alpha is reduced with
    /// the colors: entries may be translucent (<c>tRNS</c>), and fully transparent pixels share one entry.
    /// </para>
    /// <para>
    /// Each image is reduced into a copy of 8-bit RGBA pixels, charged to the resource limits of the writer: an eager save
    /// keeps the copies of every image until it completes, a sequential writer the copy of the image it writes.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">The value is <see langword="null"/>.</exception>
    public QuantizeOptions Quantization
    {
        get;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            field = value;
        }
    } = QuantizeOptions.Default;

    /// <summary>Gets the optimization mode. Defaults to <see cref="PngOptimization.None"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined <see cref="PngOptimization"/>.</exception>
    public PngOptimization Optimization
    {
        get;
        init
        {
            if (!Enum.IsDefined(value))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The optimization mode is not valid.");

            field = value;
        }
    }

    /// <summary>
    /// Gets the zlib compression level. Defaults to <see cref="System.IO.Compression.CompressionLevel.Optimal"/>. Ignored by
    /// <see cref="PngOptimization.MinimumSize"/>, which tries several levels and keeps the smallest data.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined <see cref="System.IO.Compression.CompressionLevel"/>.</exception>
    public CompressionLevel CompressionLevel
    {
        get;
        init
        {
            if (!Enum.IsDefined(value))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The compression level is not valid.");

            field = value;
        }
    } = CompressionLevel.Optimal;

    /// <summary>Gets the row filter strategy. Defaults to <see cref="PngFilter.Adaptive"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined <see cref="PngFilter"/>.</exception>
    public PngFilter Filter
    {
        get;
        init
        {
            if (!Enum.IsDefined(value))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The filter is not valid.");

            field = value;
        }
    }

    /// <summary>Gets a value indicating whether Adam7 interlacing is used. Defaults to <see langword="false"/>.</summary>
    public bool Interlaced { get; init; }

    /// <summary>
    /// Gets a value indicating whether the frames of an animation after the first one are written as the region of the
    /// canvas that changes, with the layout, the filters and the compression level of the other settings. Defaults to
    /// <see langword="false"/>: every frame covers the canvas. <see cref="PngOptimization.MinimumSize"/> always writes such
    /// regions (choosing them by compressing the candidates) and ignores the setting.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each frame stores the bounding box of the pixels that differ from the canvas it is drawn on, with the disposal of the
    /// previous frame (<c>APNG_DISPOSE_OP_NONE</c>, <c>BACKGROUND</c> or <c>PREVIOUS</c>) that gives the smallest box. The box
    /// is blended with <c>APNG_BLEND_OP_OVER</c>, its unchanged pixels fully transparent, when the layout has a transparent
    /// value (an alpha channel, or a fully transparent palette entry), some pixels do not change, and every pixel that
    /// changes is opaque, or not fully transparent where the canvas is (both give the pixel exactly); otherwise with
    /// <c>APNG_BLEND_OP_SOURCE</c>, which reproduces every pixel. A frame equal to the previous one is a 1x1 region. The first
    /// frame and the poster cover the canvas, and the displayed frames, their durations, the play count and the number of
    /// frames are exactly the same as without the setting. The palette indexes of <see cref="PngPixelLayout.Indexed"/> have
    /// 8 bits, because some decoders misplace the packed pixels of a frame region.
    /// </para>
    /// <para>
    /// The encoder keeps two copies of the canvas (4 bytes per pixel, 8 for the 16-bit pixel formats), and the compressed
    /// data of each frame until the next one is given: the <c>fcTL</c> chunk of a frame holds the disposal chosen for the
    /// next one.
    /// </para>
    /// </remarks>
    public bool OptimizeFrames { get; init; }

    /// <summary>
    /// Gets how frame durations are converted to APNG <c>delay_num / delay_den</c> (16-bit unsigned values). Defaults to
    /// <see cref="FrameDurationRounding.RequireExact"/>.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined <see cref="FrameDurationRounding"/>.</exception>
    public FrameDurationRounding DurationRounding
    {
        get;
        init
        {
            if (!Enum.IsDefined(value))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The duration rounding is not valid.");

            field = value;
        }
    }
}
