using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging;

/// <summary>
/// Computes one palette from the pixels of one or more frames, for example a palette shared by every frame of an animation
/// (<see cref="QuantizeOptions.Palette"/>).
/// </summary>
/// <remarks>
/// <para>
/// The pixels are read as 8-bit RGBA, with the conversion of <see cref="Image.CloneAs{TPixel}(PixelConversionOptions?)"/> (a floating-point sample
/// outside [0, 1] is rejected). Every pixel counts once, whatever its frame. <see cref="Build"/> returns the exact colors when
/// at most the requested number are used, and otherwise a deterministic variance-based median cut: the colors are counted on
/// a histogram of fixed size (5 bits per channel for the opaque colors, 4 bits per channel and 3 bits of alpha for the
/// translucent ones), the box with the largest weighted squared error is split along its axis of largest variance at the
/// point that maximizes the between-part variance, and each palette color is the weighted mean of its box; k-means then
/// refines the palette (each color moves to its nearest entry and each entry to the weighted mean of its colors, at most
/// eight times, on exact integer sums). Opaque and translucent colors are never in the same box or cluster (unless a single
/// color is requested), and translucent colors are compared premultiplied by their alpha. Fully transparent pixels give the palette one transparent entry, which counts toward the
/// number of colors.
/// </para>
/// <para>
/// The result is the same for the same pixels on every platform, whatever the order in which the frames are added. The
/// histogram (about 2.6 MiB) is charged to the resource limits of the configuration given at creation. A builder supports one
/// operation at a time and builds one palette.
/// </para>
/// </remarks>
/// <example>
/// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../../tests/Meziantou.Framework.Imaging.Processing.Tests/QuantizeTests.cs" ref:region="PaletteBuilder_SharedPalette" ref:dedent="true">
/// [Fact]
/// public void PaletteBuilder_SharedPalette()
/// {
///     // An animation of two frames with different colors
///     using var animation = new Image&lt;Rgba32&gt;(32, 32);
///     animation.AppendFrame();
///     for (var i = 0; i &lt; animation.Frames.Count; i++)
///     {
///         var shift = i * 100;
///         animation.Frames[i].ProcessPixelRows(pixels =&gt;
///         {
///             for (var y = 0; y &lt; pixels.Height; y++)
///             {
///                 var row = pixels.GetRowSpan(y);
///                 for (var x = 0; x &lt; row.Length; x++)
///                 {
///                     row[x] = new Rgba32((byte)(x * 8), (byte)((y * 4) + shift), (byte)shift);
///                 }
///             }
///         });
///     }
///
///     // One palette for every frame, then every frame maps to it
///     ImagePalette palette;
///     using (var builder = new PaletteBuilder())
///     {
///         builder.Add(animation);
///         palette = builder.Build(maxColors: 64);
///     }
///
///     animation.Quantize(new QuantizeOptions { Palette = palette, Dithering = DitheringMode.Sierra });
///
///     foreach (var frame in animation.Frames)
///     {
///         var colors = new HashSet&lt;Rgba32&gt;();
///         frame.ProcessPixelRows(pixels =&gt;
///         {
///             for (var y = 0; y &lt; pixels.Height; y++)
///             {
///                 colors.UnionWith(pixels.GetRowSpan(y).ToArray());
///             }
///         });
///
///         Assert.True(colors.IsSubsetOf(palette.Colors.ToArray()));
///     }
/// }
/// </code>
/// </example>
public sealed class PaletteBuilder : IDisposable
{
    private readonly AllocationScope _scope;
    private PaletteQuantizer? _quantizer;
    private bool _hasPixels;
    private bool _built;

    /// <summary>Initializes a new instance of the <see cref="PaletteBuilder"/> class.</summary>
    /// <param name="configuration">The configuration whose resource limits the histogram is charged to, or <see langword="null"/> for the default configuration.</param>
    /// <exception cref="ImageResourceLimitException">The histogram exceeds the live-allocation limit.</exception>
    public PaletteBuilder(ImageConfiguration? configuration = null)
    {
        _scope = AllocationScope.Create(configuration, nameof(PaletteBuilder));
        _quantizer = new PaletteQuantizer(_scope, translucent: true);
        _quantizer.Begin(ImagePalette.MaxCount);
    }

    /// <summary>Adds the pixels of every displayed frame of an image and of its poster frame.</summary>
    /// <param name="image">The image.</param>
    /// <param name="cancellationToken">The token checked between rows.</param>
    /// <exception cref="ArgumentNullException"><paramref name="image"/> is <see langword="null"/>.</exception>
    /// <exception cref="UnsupportedImageFeatureException">A floating-point sample is outside [0, 1] or is not a number.</exception>
    /// <exception cref="InvalidOperationException">The palette was already built, the builder would count more than 2^31 - 1 pixels, or a pixel lease is active.</exception>
    /// <exception cref="ObjectDisposedException">The builder or the image is disposed.</exception>
    public void Add(Image image, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);
        foreach (var frame in image.Frames)
        {
            Add(frame, cancellationToken);
        }

        if (image.PosterFrame is { } poster)
        {
            Add(poster, cancellationToken);
        }
    }

    /// <summary>Adds the pixels of a frame.</summary>
    /// <param name="frame">The frame.</param>
    /// <param name="cancellationToken">The token checked between rows.</param>
    /// <exception cref="ArgumentNullException"><paramref name="frame"/> is <see langword="null"/>.</exception>
    /// <exception cref="UnsupportedImageFeatureException">A floating-point sample is outside [0, 1] or is not a number.</exception>
    /// <exception cref="InvalidOperationException">The palette was already built, the builder would count more than 2^31 - 1 pixels, or a pixel lease is active.</exception>
    /// <exception cref="ObjectDisposedException">The builder is disposed, or the frame is no longer attached to a live image.</exception>
    public void Add(ImageFrame frame, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(frame);
        var quantizer = GetQuantizer();
        var storage = frame.GetStorage();
        cancellationToken.ThrowIfCancellationRequested();
        using var rows = new FrameQuantization(_scope, storage.Width);
        using var lease = storage.AcquireLease();
        rows.AddRows(quantizer, lease, frame.PixelFormat, cancellationToken);
        _hasPixels = true;
    }

    /// <summary>Builds the palette of the pixels added so far. The builder can then only be disposed.</summary>
    /// <param name="maxColors">The largest number of colors, including the transparent entry. Defaults to 256.</param>
    /// <returns>The palette: opaque colors first, then translucent colors, then the transparent entry.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxColors"/> is not between 2 and 256.</exception>
    /// <exception cref="InvalidOperationException">No pixel was added, or the palette was already built.</exception>
    /// <exception cref="ObjectDisposedException">The builder is disposed.</exception>
    public ImagePalette Build(int maxColors = ImagePalette.MaxCount)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxColors, 2);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maxColors, ImagePalette.MaxCount);
        var quantizer = GetQuantizer();
        if (!_hasPixels)
            throw new InvalidOperationException("No pixel was added to the palette builder.");

        quantizer.BuildPalette(maxColors, includeTransparent: true);
        _built = true;
        return ImagePalette.FromKeys(quantizer.Palette);
    }

    /// <summary>Releases the histogram.</summary>
    public void Dispose()
    {
        _quantizer?.Dispose();
        _quantizer = null;
    }

    private PaletteQuantizer GetQuantizer()
    {
        ObjectDisposedException.ThrowIf(_quantizer is null, this);
        if (_built)
            throw new InvalidOperationException("The palette was already built: a palette builder builds one palette.");

        return _quantizer;
    }
}
