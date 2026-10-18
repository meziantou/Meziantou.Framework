using Meziantou.Framework.Imaging;
using Meziantou.Framework.WebPlatform.Rendering;

namespace Meziantou.Framework.WebPlatform.Imaging;

/// <summary>
/// A <see cref="RenderingImage"/> of an <see cref="ImagingRenderingBackend"/>: an image of RGBA pixels (straight alpha, 8 or 16
/// bits per component) in a color space, and its conversion to the other color space when it is drawn on a bitmap of that
/// color space.
/// </summary>
internal sealed class ImagingRenderingImage : RenderingImage
{
    private readonly Lock _lock = new();
    private readonly RenderingColorSpace _colorSpace;
    private readonly int _width;
    private readonly int _height;
    private Image? _image;
    private Image? _converted;

    /// <summary>Creates an image of sRGB pixels.</summary>
    /// <param name="width">The width in pixels (at least 1).</param>
    /// <param name="height">The height in pixels (at least 1).</param>
    /// <param name="rgba">The pixels: 4 bytes per pixel (RGBA, not premultiplied), row by row.</param>
    public ImagingRenderingImage(int width, int height, ReadOnlySpan<byte> rgba)
        : this(Import(width, height, rgba), RenderingColorSpace.Srgb)
    {
    }

    /// <summary>Creates an image that owns the pixels of an image (of <see cref="Rgba32"/> or <see cref="Rgba64"/> pixels) in a color space.</summary>
    public ImagingRenderingImage(Image image, RenderingColorSpace colorSpace)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (image is not (Image<Rgba32> or Image<Rgba64>))
            throw new ArgumentException("The pixels of the image must be Rgba32 or Rgba64.", nameof(image));

        _image = image;
        _width = image.Width;
        _height = image.Height;
        _colorSpace = colorSpace;
    }

    /// <inheritdoc/>
    public override int Width => _width;

    /// <inheritdoc/>
    public override int Height => _height;

    private static Image<Rgba32> Import(int width, int height, ReadOnlySpan<byte> rgba)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(rgba.Length, (long)width * height * 4);
        return Image.ImportPixelBytes<Rgba32>(rgba[..(width * height * 4)], width, height, width * 4);
    }

    /// <inheritdoc/>
    public override void ReadPixels(Span<byte> destination)
    {
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_image is null, this);
            var length = checked(_width * _height * 4);
            if (destination.Length < length)
                throw new ArgumentException("The destination is too small.", nameof(destination));

            // The pixels of an image are read in sRGB
            switch (GetImageCore(RenderingColorSpace.Srgb))
            {
                case Image<Rgba32> image:
                    image.Frames[0].CopyPixelBytesTo(destination[..length], 0);
                    break;

                case Image<Rgba64> image:
                    var pixels = new Rgba64[_width * _height];
                    image.Frames[0].CopyPixelDataTo(pixels, 0);
                    PixelConversions.ToBytes(pixels, destination[..length]);
                    break;
            }
        }
    }

    /// <summary>Returns the frame of the pixels of the image in a color space (converted the first time).</summary>
    public ImageFrame GetFrame(RenderingColorSpace colorSpace)
    {
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_image is null, this);
            return GetImageCore(colorSpace).Frames[0];
        }
    }

    private Image GetImageCore(RenderingColorSpace colorSpace)
    {
        var image = _image!;
        if (colorSpace == _colorSpace)
            return image;

        if (_converted is null)
        {
            var spaces = (From: _colorSpace, To: colorSpace);
            switch (image)
            {
                case Image<Rgba32> narrow:
                {
                    var converted = narrow.Clone();
                    converted.Frames[0].ProcessPixelRows(spaces, static (accessor, spaces) =>
                    {
                        for (var y = 0; y < accessor.Height; y++)
                        {
                            ColorSpaces.Convert(accessor.GetRowSpan(y), spaces.From, spaces.To);
                        }
                    });

                    _converted = converted;
                    break;
                }

                case Image<Rgba64> wide:
                {
                    var converted = wide.Clone();
                    converted.Frames[0].ProcessPixelRows(spaces, static (accessor, spaces) =>
                    {
                        for (var y = 0; y < accessor.Height; y++)
                        {
                            ColorSpaces.Convert(accessor.GetRowSpan(y), spaces.From, spaces.To);
                        }
                    });

                    _converted = converted;
                    break;
                }
            }
        }

        return _converted!;
    }

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            lock (_lock)
            {
                _image?.Dispose();
                _image = null;
                _converted?.Dispose();
                _converted = null;
            }
        }
    }
}
