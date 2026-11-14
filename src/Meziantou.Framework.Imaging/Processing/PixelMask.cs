using System.Numerics;
using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging;

/// <summary>
/// A set of pixels of an image: the regions to keep and to remove of
/// <see cref="ImageProcessingExtensions.SeamCarve(Image, int, int, SeamCarvingOptions?, CancellationToken)"/>.
/// </summary>
/// <remarks>
/// A new mask is empty. It holds one bit per pixel, in memory owned by the mask (not charged to an image). It is not
/// thread-safe for writes.
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

    private int GetIndex(int x, int y)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(x);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(x, Width);
        ArgumentOutOfRangeException.ThrowIfNegative(y);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(y, Height);
        return (y * Width) + x;
    }
}
