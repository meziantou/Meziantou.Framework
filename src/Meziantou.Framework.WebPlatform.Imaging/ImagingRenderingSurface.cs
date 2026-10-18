using System.Buffers;
using Meziantou.Framework.Imaging;
using Meziantou.Framework.WebPlatform.Rendering;

namespace Meziantou.Framework.WebPlatform.Imaging;

/// <summary>
/// A <see cref="RenderingSurface"/> of an <see cref="ImagingRenderingBackend"/>: an image of RGBA pixels (straight alpha) drawn by
/// the drawing API of Meziantou.Framework.Imaging. The bitmap of a canvas has 8 bits per component, as in the browsers; the other
/// surfaces (the snapshots of the view transitions, the SVG images) have 16 bits per component, as the screenshots.
/// </summary>
internal sealed class ImagingRenderingSurface : RenderingSurface
{
    /// <summary>The maximum number of pixels of a surface (the limit of the canvases of the browsers is of the same order).</summary>
    private const long MaxPixelCount = 268_435_456;

    private readonly ImagingRenderingBackend _backend;
    private readonly Image<Rgba32>? _image;
    private readonly Image<Rgba64>? _wideImage;
    private readonly RenderingColorSpace _colorSpace;
    private readonly bool _hasAlpha;

    private ImagingRenderingSurface(ImagingRenderingBackend backend, Image<Rgba32>? image, Image<Rgba64>? wideImage, RenderingSurfaceOptions options)
    {
        _backend = backend;
        _image = image;
        _wideImage = wideImage;
        _colorSpace = options.ColorSpace;
        _hasAlpha = options.HasAlpha;
    }

    /// <inheritdoc/>
    public override int Width => _image?.Width ?? _wideImage!.Width;

    /// <inheritdoc/>
    public override int Height => _image?.Height ?? _wideImage!.Height;

    private ImageFrame Frame => _image is not null ? _image.Frames[0] : _wideImage!.Frames[0];

    /// <summary>Creates a surface, or returns null when it is too large or cannot be allocated.</summary>
    public static ImagingRenderingSurface? Create(ImagingRenderingBackend backend, int width, int height, RenderingSurfaceOptions options)
    {
        if (width < 1 || height < 1 || (long)width * height > MaxPixelCount)
            return null;

        try
        {
            // A transparent surface is initially transparent black, an opaque one opaque black
            if (options.IsCanvasBitmap)
            {
                var image = options.HasAlpha ? new Image<Rgba32>(width, height) : new Image<Rgba32>(width, height, new Rgba32(0, 0, 0, 255));
                return new ImagingRenderingSurface(backend, image, wideImage: null, options);
            }

            var wideImage = options.HasAlpha ? new Image<Rgba64>(width, height) : new Image<Rgba64>(width, height, new Rgba64(0, 0, 0, ushort.MaxValue));
            return new ImagingRenderingSurface(backend, image: null, wideImage, options);
        }
        catch (Exception exception) when (exception is OutOfMemoryException or OverflowException or ImageResourceLimitException)
        {
            return null;
        }
    }

    /// <inheritdoc/>
    public override void Draw(DisplayList displayList)
    {
        ArgumentNullException.ThrowIfNull(displayList);
        _backend.Rasterize(Frame, displayList, _colorSpace);
        MakeOpaque();
    }

    /// <inheritdoc/>
    public override void Clear(DisplayColor color)
    {
        var rgba64 = ColorSpaces.ToRgba64(color, _colorSpace);
        if (_image is not null)
        {
            var pixel = new Rgba32(PixelConversions.ToByte(rgba64.R), PixelConversions.ToByte(rgba64.G), PixelConversions.ToByte(rgba64.B), PixelConversions.ToByte(rgba64.A));
            _image.Frames[0].ProcessPixelRows(pixel, static (accessor, pixel) =>
            {
                for (var y = 0; y < accessor.Height; y++)
                {
                    accessor.GetRowSpan(y).Fill(pixel);
                }
            });
        }
        else
        {
            _wideImage!.Frames[0].ProcessPixelRows(rgba64, static (accessor, pixel) =>
            {
                for (var y = 0; y < accessor.Height; y++)
                {
                    accessor.GetRowSpan(y).Fill(pixel);
                }
            });
        }

        MakeOpaque();
    }

    /// <inheritdoc/>
    public override void ReadPixels(int x, int y, int width, int height, Span<byte> destination)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(width);
        ArgumentOutOfRangeException.ThrowIfNegative(height);
        var length = (long)width * height * 4;
        if (destination.Length < length)
            throw new ArgumentException("The destination is too small.", nameof(destination));

        // The pixels outside of the surface are transparent black
        destination[..(int)length].Clear();
        var area = Intersect(x, y, width, height);
        if (area.Width <= 0 || area.Height <= 0)
            return;

        var buffer = ArrayPool<byte>.Shared.Rent(area.Width * area.Height * 4);
        try
        {
            if (_image is not null)
            {
                _image.Frames[0].ProcessPixelRows((buffer, area), static (accessor, state) =>
                {
                    var (buffer, area) = state;
                    for (var row = 0; row < area.Height; row++)
                    {
                        var rowBytes = unsafe(System.Runtime.InteropServices.MemoryMarshal.AsBytes(accessor.GetRowSpan(area.Y + row).Slice(area.X, area.Width)));
                        rowBytes.CopyTo(buffer.AsSpan(row * area.Width * 4));
                    }
                });
            }
            else
            {
                _wideImage!.Frames[0].ProcessPixelRows((buffer, area), static (accessor, state) =>
                {
                    var (buffer, area) = state;
                    for (var row = 0; row < area.Height; row++)
                    {
                        PixelConversions.ToBytes(accessor.GetRowSpan(area.Y + row).Slice(area.X, area.Width), buffer.AsSpan(row * area.Width * 4, area.Width * 4));
                    }
                });
            }

            for (var row = 0; row < area.Height; row++)
            {
                buffer.AsSpan(row * area.Width * 4, area.Width * 4).CopyTo(destination[(int)(((((long)area.Y + row - y) * width) + (area.X - x)) * 4)..]);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <inheritdoc/>
    public override void WritePixels(int x, int y, int width, int height, ReadOnlySpan<byte> source)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(width);
        ArgumentOutOfRangeException.ThrowIfNegative(height);
        if (width == 0 || height == 0)
            return;

        var length = (long)width * height * 4;
        if (source.Length < length)
            throw new ArgumentException("The source is too small.", nameof(source));

        // The pixels replace the ones of the surface, without compositing
        var area = Intersect(x, y, width, height);
        if (area.Width <= 0 || area.Height <= 0)
            return;

        var buffer = ArrayPool<byte>.Shared.Rent(area.Width * area.Height * 4);
        try
        {
            for (var row = 0; row < area.Height; row++)
            {
                source.Slice((int)(((((long)area.Y + row - y) * width) + (area.X - x)) * 4), area.Width * 4).CopyTo(buffer.AsSpan(row * area.Width * 4));
            }

            if (_image is not null)
            {
                _image.Frames[0].ProcessPixelRows((buffer, area), static (accessor, state) =>
                {
                    var (buffer, area) = state;
                    for (var row = 0; row < area.Height; row++)
                    {
                        buffer.AsSpan(row * area.Width * 4, area.Width * 4).CopyTo(unsafe(System.Runtime.InteropServices.MemoryMarshal.AsBytes(accessor.GetRowSpan(area.Y + row).Slice(area.X, area.Width))));
                    }
                });
            }
            else
            {
                _wideImage!.Frames[0].ProcessPixelRows((buffer, area), static (accessor, state) =>
                {
                    var (buffer, area) = state;
                    for (var row = 0; row < area.Height; row++)
                    {
                        PixelConversions.FromBytes(buffer.AsSpan(row * area.Width * 4, area.Width * 4), accessor.GetRowSpan(area.Y + row).Slice(area.X, area.Width));
                    }
                });
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        MakeOpaque();
    }

    /// <inheritdoc/>
    public override RenderingImage CreateSnapshot() => _image is not null ? new ImagingRenderingImage(_image.Clone(), _colorSpace) : new ImagingRenderingImage(_wideImage!.Clone(), _colorSpace);

    /// <summary>The intersection of a rectangle with the surface.</summary>
    private (int X, int Y, int Width, int Height) Intersect(int x, int y, int width, int height)
    {
        var left = Math.Max(0, x);
        var top = Math.Max(0, y);
        var right = (int)Math.Min(Width, (long)x + width);
        var bottom = (int)Math.Min(Height, (long)y + height);
        return (left, top, right - left, bottom - top);
    }

    /// <summary>
    /// The pixels of a surface without alpha channel are opaque: a pixel that is not opaque gets the color it has over black,
    /// as on the frames without alpha of Meziantou.Framework.Imaging.
    /// </summary>
    private void MakeOpaque()
    {
        if (_hasAlpha)
            return;

        if (_image is not null)
        {
            _image.Frames[0].ProcessPixelRows(static accessor =>
            {
                for (var y = 0; y < accessor.Height; y++)
                {
                    foreach (ref var pixel in accessor.GetRowSpan(y))
                    {
                        if (pixel.A != 255)
                        {
                            pixel = new Rgba32(Multiply8(pixel.R, pixel.A), Multiply8(pixel.G, pixel.A), Multiply8(pixel.B, pixel.A), 255);
                        }
                    }
                }
            });
        }
        else
        {
            _wideImage!.Frames[0].ProcessPixelRows(static accessor =>
            {
                for (var y = 0; y < accessor.Height; y++)
                {
                    foreach (ref var pixel in accessor.GetRowSpan(y))
                    {
                        if (pixel.A != ushort.MaxValue)
                        {
                            pixel = new Rgba64(Multiply16(pixel.R, pixel.A), Multiply16(pixel.G, pixel.A), Multiply16(pixel.B, pixel.A), ushort.MaxValue);
                        }
                    }
                }
            });
        }

    }

    private static byte Multiply8(byte value, byte alpha) => (byte)(((value * alpha) + 127) / 255);

    private static ushort Multiply16(ushort value, ushort alpha) => (ushort)((((long)value * alpha) + 32767) / 65535);

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _image?.Dispose();
            _wideImage?.Dispose();
        }
    }
}
