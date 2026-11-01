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
    private Dictionary<RenderingColorSpace, Image<Rgba64>>? _normalizedImages;

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
        if (image is not (Image<Rgba32> or Image<Rgba64> or Image<RgbaHalf>))
            throw new ArgumentException("The pixels of the image must be Rgba32, Rgba64 or RgbaHalf.", nameof(image));

        _image = image;
        _width = image.Width;
        _height = image.Height;
        _colorSpace = colorSpace;
        PixelFormat = image is Image<RgbaHalf> or Image<Rgba64> ? Dom.ImageDataPixelFormat.RgbaFloat16 : Dom.ImageDataPixelFormat.RgbaUnorm8;
    }

    /// <inheritdoc/>
    public override int Width => _width;

    /// <inheritdoc/>
    public override int Height => _height;

    /// <inheritdoc/>
    public override Dom.ImageDataPixelFormat PixelFormat { get; }

    private static Image<Rgba32> Import(int width, int height, ReadOnlySpan<byte> rgba)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(rgba.Length, (long)width * height * 4);
        return Image.ImportPixelBytes<Rgba32>(rgba[..(width * height * 4)], width, height, width * 4);
    }

    /// <inheritdoc/>
    public override void ReadPixels(Span<byte> destination) => ReadPixels(destination, Dom.ImageDataPixelFormat.RgbaUnorm8);

    /// <inheritdoc/>
    public override void ReadPixels(Span<byte> destination, Dom.ImageDataPixelFormat pixelFormat)
    {
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_image is null, this);
            var length = checked(_width * _height * (pixelFormat == Dom.ImageDataPixelFormat.RgbaFloat16 ? 8 : 4));
            if (destination.Length < length)
                throw new ArgumentException("The destination is too small.", nameof(destination));

            if (pixelFormat == Dom.ImageDataPixelFormat.RgbaFloat16)
            {
                switch (GetImageCore(RenderingColorSpace.Srgb))
                {
                    case Image<RgbaHalf> halfImage:
                        halfImage.Frames[0].CopyPixelBytesTo(destination[..length], 0);
                        return;

                    case Image<Rgba64> wideImage:
                        var pixels = new Rgba64[_width * _height];
                        wideImage.Frames[0].CopyPixelDataTo(pixels, 0);
                        var halves = unsafe(System.Runtime.InteropServices.MemoryMarshal.Cast<byte, Half>(destination[..length]));
                        for (var i = 0; i < pixels.Length; i++)
                        {
                            halves[i * 4] = (Half)(pixels[i].R / 65535.0);
                            halves[(i * 4) + 1] = (Half)(pixels[i].G / 65535.0);
                            halves[(i * 4) + 2] = (Half)(pixels[i].B / 65535.0);
                            halves[(i * 4) + 3] = (Half)(pixels[i].A / 65535.0);
                        }

                        return;

                    default:
                        base.ReadPixels(destination, pixelFormat);
                        return;
                }
            }

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

                case Image<RgbaHalf> image:
                    var halves = new byte[_width * _height * 8];
                    image.Frames[0].CopyPixelBytesTo(halves, 0);
                    Dom.CanvasRenderingContext2DBase.CopyHalfToBytes(halves, destination[..length]);
                    break;
            }
        }
    }

    /// <summary>Returns the frame of the pixels of the image in a color space (converted the first time).</summary>
    public ImageFrame GetFrame(RenderingColorSpace colorSpace, bool preserveRange = true)
    {
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_image is null, this);
            var image = GetImageCore(colorSpace);
            if (preserveRange || image is not Image<RgbaHalf> halfImage)
                return image.Frames[0];

            _normalizedImages ??= [];
            if (!_normalizedImages.TryGetValue(colorSpace, out var normalized))
            {
                var pixels = new RgbaHalf[_width * _height];
                halfImage.Frames[0].CopyPixelDataTo(pixels, 0);
                var normalizedPixels = new Rgba64[pixels.Length];
                for (var i = 0; i < pixels.Length; i++)
                {
                    var pixel = pixels[i];
                    normalizedPixels[i] = new Rgba64(ColorSpaces.ToUInt16((float)pixel.R), ColorSpaces.ToUInt16((float)pixel.G), ColorSpaces.ToUInt16((float)pixel.B), ColorSpaces.ToUInt16((float)pixel.A));
                }

                var bytes = unsafe(System.Runtime.InteropServices.MemoryMarshal.AsBytes(normalizedPixels.AsSpan()));
                normalized = Image.ImportPixelBytes<Rgba64>(bytes, _width, _height, _width * 8);
                _normalizedImages.Add(colorSpace, normalized);
            }

            return normalized.Frames[0];
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
                case Image<RgbaHalf> half:
                {
                    var converted = half.Clone();
                    converted.Frames[0].ProcessPixelRows(spaces, (accessor, spaces) =>
                    {
                        for (var y = 0; y < accessor.Height; y++)
                        {
                            ColorSpaces.Convert(accessor.GetRowSpan(y), spaces.From, spaces.To);
                        }
                    });
                    _converted = converted;
                    break;
                }

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
                if (_normalizedImages is not null)
                {
                    foreach (var image in _normalizedImages.Values)
                    {
                        image.Dispose();
                    }

                    _normalizedImages = null;
                }
            }
        }
    }
}
