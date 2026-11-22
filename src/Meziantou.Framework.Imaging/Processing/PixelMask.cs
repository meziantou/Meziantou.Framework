using System.Numerics;
using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging;

/// <summary>
/// A set of pixels of an image: a selection
/// (<see cref="FromSimilarColors(ImageFrame, Point, ColorSelectionOptions?, CancellationToken)"/>, the magic wand of image
/// editors), the region to fill of
/// <see cref="ImageProcessingExtensions.Inpaint(Image, PixelMask, InpaintingOptions?, CancellationToken)"/>, or the
/// regions to keep and to remove of
/// <see cref="ImageProcessingExtensions.SeamCarve(Image, int, int, SeamCarvingOptions?, CancellationToken)"/>.
/// </summary>
/// <remarks>
/// A new mask is empty. It holds one bit per pixel, in memory owned by the mask (not charged to an image). Masks of the
/// same size combine with <see cref="UnionWith"/>, <see cref="IntersectWith"/>, <see cref="ExceptWith"/>,
/// <see cref="SymmetricExceptWith"/> and <see cref="Invert"/>, and <see cref="ToImage"/> turns a mask into an alpha
/// image that the drawing layers and masks apply to an image. It is not thread-safe for writes.
/// </remarks>
public sealed class PixelMask
{
    private readonly ulong[] _bits;

    /// <summary>Initializes a new instance of the <see cref="PixelMask"/> class, empty.</summary>
    /// <param name="width">The width, in pixels. Must be positive.</param>
    /// <param name="height">The height, in pixels. Must be positive.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="width"/> or <paramref name="height"/> is not positive, or the mask would have more than <see cref="int.MaxValue"/> pixels.</exception>
    public PixelMask(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        if ((long)width * height > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(height), height, "The mask must not have more than int.MaxValue pixels.");

        Width = width;
        Height = height;
        _bits = new ulong[(int)(((long)width * height + 63) / 64)];
    }

    /// <summary>Initializes a new instance of the <see cref="PixelMask"/> class, empty.</summary>
    /// <param name="size">The size. Must not be empty.</param>
    /// <exception cref="ArgumentOutOfRangeException">A side of <paramref name="size"/> is not positive, or the mask would have more than <see cref="int.MaxValue"/> pixels.</exception>
    public PixelMask(Size size)
        : this(size.Width, size.Height)
    {
    }

    /// <summary>Gets the width, in pixels.</summary>
    public int Width { get; }

    /// <summary>Gets the height, in pixels.</summary>
    public int Height { get; }

    /// <summary>Gets the size.</summary>
    public Size Size => new(Width, Height);

    /// <summary>Gets the number of pixels of the mask.</summary>
    public int Count
    {
        get
        {
            var count = 0;
            foreach (var word in _bits)
            {
                count += BitOperations.PopCount(word);
            }

            return count;
        }
    }

    /// <summary>Gets or sets whether a pixel belongs to the mask.</summary>
    /// <param name="x">The column, from 0 to <c>Width - 1</c>.</param>
    /// <param name="y">The row, from 0 to <c>Height - 1</c>.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="x"/> or <paramref name="y"/> is outside the mask.</exception>
    public bool this[int x, int y]
    {
        get
        {
            var index = GetIndex(x, y);
            return (_bits[index >> 6] & (1UL << (index & 63))) != 0;
        }
        set
        {
            var index = GetIndex(x, y);
            if (value)
            {
                _bits[index >> 6] |= 1UL << (index & 63);
            }
            else
            {
                _bits[index >> 6] &= ~(1UL << (index & 63));
            }
        }
    }

    /// <summary>Adds the pixels of a rectangle to the mask, or removes them; the part of the rectangle outside the mask is ignored.</summary>
    /// <param name="rectangle">The rectangle.</param>
    /// <param name="value"><see langword="true"/> to add the pixels, <see langword="false"/> to remove them.</param>
    public void Fill(Rectangle rectangle, bool value = true)
    {
        var area = Rectangle.Intersect(rectangle, new Rectangle(0, 0, Width, Height));
        for (var y = area.Y; y < area.Bottom; y++)
        {
            for (var x = area.X; x < area.Right; x++)
            {
                this[x, y] = value;
            }
        }
    }

    /// <summary>Removes every pixel from the mask.</summary>
    public void Clear() => Array.Clear(_bits);

    /// <summary>Adds the pixels of another mask to this one.</summary>
    /// <param name="other">The other mask, of the same size.</param>
    /// <exception cref="ArgumentNullException"><paramref name="other"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="other"/> does not have the size of this mask.</exception>
    public void UnionWith(PixelMask other)
    {
        var bits = GetBitsOfSameSize(other);
        for (var i = 0; i < _bits.Length; i++)
        {
            _bits[i] |= bits[i];
        }
    }

    /// <summary>Keeps only the pixels that also belong to another mask.</summary>
    /// <param name="other">The other mask, of the same size.</param>
    /// <exception cref="ArgumentNullException"><paramref name="other"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="other"/> does not have the size of this mask.</exception>
    public void IntersectWith(PixelMask other)
    {
        var bits = GetBitsOfSameSize(other);
        for (var i = 0; i < _bits.Length; i++)
        {
            _bits[i] &= bits[i];
        }
    }

    /// <summary>Removes the pixels of another mask from this one.</summary>
    /// <param name="other">The other mask, of the same size.</param>
    /// <exception cref="ArgumentNullException"><paramref name="other"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="other"/> does not have the size of this mask.</exception>
    public void ExceptWith(PixelMask other)
    {
        var bits = GetBitsOfSameSize(other);
        for (var i = 0; i < _bits.Length; i++)
        {
            _bits[i] &= ~bits[i];
        }
    }

    /// <summary>Keeps the pixels that belong to one of the two masks but not to both.</summary>
    /// <param name="other">The other mask, of the same size.</param>
    /// <exception cref="ArgumentNullException"><paramref name="other"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="other"/> does not have the size of this mask.</exception>
    public void SymmetricExceptWith(PixelMask other)
    {
        var bits = GetBitsOfSameSize(other);
        for (var i = 0; i < _bits.Length; i++)
        {
            _bits[i] ^= bits[i];
        }
    }

    /// <summary>Adds the pixels that do not belong to the mask and removes those that do.</summary>
    public void Invert()
    {
        for (var i = 0; i < _bits.Length; i++)
        {
            _bits[i] = ~_bits[i];
        }

        // The bits after the last pixel stay clear, so that Count is the number of pixels
        var used = (int)(((long)Width * Height) & 63);
        if (used != 0)
        {
            _bits[^1] &= (1UL << used) - 1;
        }
    }

    /// <summary>Creates a copy of the mask.</summary>
    /// <returns>A new mask with the same size and pixels.</returns>
    public PixelMask Clone()
    {
        var clone = new PixelMask(Width, Height);
        _bits.CopyTo(clone._bits, 0);
        return clone;
    }

    /// <summary>
    /// Creates an <see cref="A8"/> image of the size of the mask: the pixels of the mask are opaque (255), the others are
    /// transparent (0). Drawn on an image in a layer, it keeps the pixels of the mask
    /// (<see cref="Drawing.CompositeMode.DestinationIn"/>) or erases them
    /// (<see cref="Drawing.CompositeMode.DestinationOut"/>), and as the mask of
    /// <see cref="Drawing.DrawingCanvas.Mask(Drawing.DrawingAction, Drawing.DrawingAction, Drawing.MaskMode)"/> it limits
    /// a drawing to them. Blur it first to soften its edges. <see cref="FromFrame(ImageFrame)"/> reads the mask back.
    /// </summary>
    /// <param name="configuration">The configuration of the image, or <see langword="null"/> for <see cref="ImageConfiguration.Default"/>.</param>
    /// <returns>A new image, owned by the caller.</returns>
    /// <exception cref="ImageResourceLimitException">The image exceeds a configured limit.</exception>
    public Image<A8> ToImage(ImageConfiguration? configuration = null)
    {
        var image = new Image<A8>(Width, Height, configuration);
        try
        {
            using var lease = image.Frames[0].GetStorage().AcquireLease();
            for (var y = 0; y < Height; y++)
            {
                var row = lease.GetRowBytes(y)[..Width];
                var rowStart = y * Width;
                for (var x = 0; x < Width; x++)
                {
                    if (Get(rowStart + x))
                    {
                        row[x] = byte.MaxValue;
                    }
                }
            }
        }
        catch
        {
            image.Dispose();
            throw;
        }

        return image;
    }

    /// <summary>
    /// Creates the mask of the pixels of a frame whose color is close to the color of a seed pixel: the magic wand of
    /// image editors. By default, the mask holds the pixels of the exact color of the seed that are connected to it by
    /// their sides; <paramref name="options"/> sets the tolerance, the diagonals, and the selection of every pixel of a
    /// close color in the frame instead.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each pixel is read as its 16-bit color with the rules of
    /// <see cref="Image.CloneAs{TPixel}(PixelConversionOptions?)"/> (8-bit and 16-bit samples exactly, a gray level as its
    /// three colors, an opaque alpha for a format without alpha), and matches when each of its four samples, alpha
    /// included, is within <see cref="ColorSelectionOptions.Tolerance"/> of the sample of the seed: the comparison of
    /// <see cref="ImageProcessingExtensions.ReplaceColor(Image, Rgba64, Rgba64, double, CancellationToken)"/>. The seed
    /// always belongs to the mask.
    /// </para>
    /// <para>
    /// No color profile is applied: the colors are those of the color space of the image. The comparison is made on
    /// integers: the mask is the same on every platform. A floating-point pixel is compared through its 16-bit color, so
    /// its samples must be in [0, 1]. The frame is not changed. Besides the mask, the selection uses a second mask and a
    /// list of the runs of pixels it has yet to visit, in memory of its own (not charged to the image).
    /// </para>
    /// </remarks>
    /// <param name="frame">The frame.</param>
    /// <param name="seed">The pixel the selection starts from, inside the frame.</param>
    /// <param name="options">The options, or <see langword="null"/> for <see cref="ColorSelectionOptions.Default"/>.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A mask of the size of the frame.</returns>
    /// <example>
    /// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../../tests/Meziantou.Framework.Imaging.Processing.Tests/ResizeTests.cs" ref:region="PixelMask_RemoveBackground" ref:dedent="true">
    /// [Fact]
    /// public void PixelMask_RemoveBackground()
    /// {
    ///     // A blue square on a white background, with a slightly darker white pixel in the background
    ///     using var image = new Image&lt;Rgba32&gt;(6, 4, new Rgba32(255, 255, 255, 255));
    ///     image.Frames[0][5, 3] = new Rgba32(250, 250, 250, 255);
    ///     for (var y = 1; y &lt; 3; y++)
    ///     {
    ///         for (var x = 2; x &lt; 4; x++)
    ///         {
    ///             image.Frames[0][x, y] = new Rgba32(30, 60, 200, 255);
    ///         }
    ///     }
    ///
    ///     // The magic wand: the pixels connected to the top-left corner whose samples are within 5% of its color
    ///     var background = PixelMask.FromSimilarColors(image.Frames[0], new Point(0, 0), new ColorSelectionOptions { Tolerance = 0.05 });
    ///     Assert.Equal((6 * 4) - 4, background.Count);
    ///
    ///     // The selection is erased: drawn as an alpha image in a layer that removes what it covers
    ///     using var mask = background.ToImage();
    ///     image.Draw(canvas =&gt; canvas.Layer(1, layer =&gt; layer.DrawImage(mask.Frames[0], new PointD(0, 0)), CompositeMode.DestinationOut));
    ///
    ///     Assert.Equal(0, image.Frames[0][0, 0].A);
    ///     Assert.Equal(0, image.Frames[0][5, 3].A);
    ///     Assert.Equal(new Rgba32(30, 60, 200, 255), image.Frames[0][2, 1]);
    /// }
    /// </code>
    /// </example>
    /// <exception cref="ArgumentNullException"><paramref name="frame"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="seed"/> is outside the frame.</exception>
    /// <exception cref="UnsupportedImageFeatureException">The frame has CMYK pixels, or a floating-point sample is outside [0, 1] or is not a number.</exception>
    /// <exception cref="InvalidOperationException">A pixel lease is active.</exception>
    /// <exception cref="ObjectDisposedException">The frame is no longer attached to a live image.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public static PixelMask FromSimilarColors(ImageFrame frame, Point seed, ColorSelectionOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (PixelFormats.IsCmyk(frame.PixelFormat))
            throw new UnsupportedImageFeatureException($"A selection compares colors of light, and the {frame.PixelFormat} pixels of the frame are CMYK inks.", ImageFormat.Unknown, "CMYK pixels");

        if (seed.X < 0 || seed.X >= frame.Width || seed.Y < 0 || seed.Y >= frame.Height)
            throw new ArgumentOutOfRangeException(nameof(seed), seed, "The seed must be inside the frame.");

        return ColorSelector.Select(frame, seed, options ?? ColorSelectionOptions.Default, cancellationToken);
    }

    /// <summary>
    /// Creates the mask of the light pixels of a frame: those whose Rec. 709 luma, premultiplied by their alpha, is at least
    /// half of the largest sample, such as the white strokes of a mask painted in black and white. For an
    /// <see cref="PixelFormat.A8"/> frame, the pixels whose alpha is at least half.
    /// </summary>
    /// <param name="frame">The frame.</param>
    /// <returns>A mask of the size of the frame.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="frame"/> is <see langword="null"/>.</exception>
    /// <exception cref="UnsupportedImageFeatureException">The frame has CMYK pixels.</exception>
    /// <exception cref="InvalidOperationException">A pixel lease is active.</exception>
    /// <exception cref="ObjectDisposedException">The frame is no longer attached to a live image.</exception>
    public static PixelMask FromFrame(ImageFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (PixelFormats.IsCmyk(frame.PixelFormat))
            throw new UnsupportedImageFeatureException($"A mask is read from colors of light, and the {frame.PixelFormat} pixels of the frame are CMYK inks.", ImageFormat.Unknown, "CMYK pixels");

        var storage = frame.GetStorage();
        var mask = new PixelMask(frame.Width, frame.Height);
        var format = frame.PixelFormat;
        var max = (long)ToneSamples.GetLevelCount(format) - 1;
        using var row = ToneSamples.RentRow(frame.OwnerImage.Owner.Scope, frame.Width);
        var samples = ToneSamples.GetSamples(row, frame.Width);
        var luma = ToneSamples.GetLuma(row, frame.Width);
        var bytesPerPixel = PixelFormats.GetBytesPerPixel(format);
        using var lease = storage.AcquireLease();
        for (var y = 0; y < frame.Height; y++)
        {
            ToneSamples.Read(format, lease.GetRowBytes(y)[..(frame.Width * bytesPerPixel)], samples);
            ToneSamples.ComputeLuma(samples, luma);
            for (var x = 0; x < frame.Width; x++)
            {
                var alpha = (long)samples[(4 * x) + 3];
                var value = format == PixelFormat.A8 ? alpha * max : luma[x] * alpha;
                if (2 * value >= max * max)
                {
                    mask[x, y] = true;
                }
            }
        }

        return mask;
    }

    /// <summary>Gets whether a pixel belongs to the mask, by its index in row order (internal callers validate it).</summary>
    internal bool Get(int index) => (_bits[index >> 6] & (1UL << (index & 63))) != 0;

    /// <summary>Adds a pixel to the mask, by its index in row order (internal callers validate it).</summary>
    internal void Set(int index) => _bits[index >> 6] |= 1UL << (index & 63);

    private ulong[] GetBitsOfSameSize(PixelMask other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (other.Width != Width || other.Height != Height)
            throw new ArgumentException($"The mask must have the size of this mask ({Width}x{Height}), not {other.Width}x{other.Height}.", nameof(other));

        return other._bits;
    }

    private int GetIndex(int x, int y)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(x);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(x, Width);
        ArgumentOutOfRangeException.ThrowIfNegative(y);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(y, Height);
        return (y * Width) + x;
    }
}
