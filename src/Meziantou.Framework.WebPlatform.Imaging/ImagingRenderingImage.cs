using Meziantou.Framework.Imaging;
using Meziantou.Framework.WebPlatform.Rendering;

namespace Meziantou.Framework.WebPlatform.Imaging;

/// <summary>
/// A <see cref="RenderingImage"/> of an <see cref="ImagingRenderingBackend"/>: an image of RGBA pixels (straight alpha, 8 or 16
/// bits per component) in a color space, and its conversion to the other color space when it is drawn on a bitmap of that
/// color space.
/// </summary>
/// <remarks>
/// The pixels of a decoded image whose colors are outside of the gamut of sRGB are the ones clipped to sRGB, as the pages
/// draw them; the image of its colors without clipping (<see cref="SetWideGamutSource"/>) is decoded the first time a
/// bitmap that holds them (Display P3, or half floats) draws or reads the image.
/// </remarks>
internal sealed class ImagingRenderingImage : RenderingImage
{
    private readonly Lock _lock = new();
    private readonly RenderingColorSpace _colorSpace;
    private readonly int _width;
    private readonly int _height;
    private Image? _image;
    private Image? _converted;

    // The pixels of an image of 8 or 16 bits converted to the other color space as half floats, without clipping the colors
    // outside of its gamut (drawn on the bitmaps of half floats)
    private Image<RgbaHalf>? _extended;
    private Dictionary<RenderingColorSpace, Image<Rgba64>>? _normalizedImages;

    // The image of the colors of a decoded image outside of the gamut of sRGB, and how it is decoded the first time
    private Func<ImagingRenderingImage?>? _wideGamutSource;
    private ImagingRenderingImage? _wideGamut;
    private RenderingColorSpace? _wideGamutColorSpace;
    private Dom.ImageDataPixelFormat? _wideGamutPixelFormat;
    private readonly Dom.ImageDataPixelFormat _pixelFormat;

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
        _pixelFormat = image is Image<RgbaHalf> or Image<Rgba64> ? Dom.ImageDataPixelFormat.RgbaFloat16 : Dom.ImageDataPixelFormat.RgbaUnorm8;
    }

    /// <inheritdoc/>
    public override int Width => _width;

    /// <inheritdoc/>
    public override int Height => _height;

    /// <inheritdoc/>
    public override Dom.ImageDataPixelFormat PixelFormat => _wideGamutPixelFormat ?? _pixelFormat;

    /// <inheritdoc/>
    public override RenderingColorSpace ColorSpace => _wideGamutColorSpace ?? _colorSpace;

    /// <summary>
    /// Sets how the image of the colors of the image without clipping to sRGB is decoded (the first time a bitmap of a wider
    /// gamut or of half floats draws or reads the image), and the component format and the color space of its pixels.
    /// </summary>
    public void SetWideGamutSource(Func<ImagingRenderingImage?> source, Dom.ImageDataPixelFormat pixelFormat, RenderingColorSpace colorSpace)
    {
        lock (_lock)
        {
            _wideGamutSource = source;
            _wideGamutColorSpace = colorSpace;
            _wideGamutPixelFormat = pixelFormat;
        }
    }

    /// <summary>Returns the image of the colors outside of the gamut of sRGB (decoded the first time), or null.</summary>
    private ImagingRenderingImage? GetWideGamut()
    {
        lock (_lock)
        {
            if (_wideGamutSource is { } source)
            {
                _wideGamutSource = null;
                _wideGamut = source();
            }

            return _wideGamut;
        }
    }

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
    public override void ReadPixels(Span<byte> destination, Dom.ImageDataPixelFormat pixelFormat) => ReadPixels(destination, pixelFormat, RenderingColorSpace.Srgb);

    /// <inheritdoc/>
    public override void ReadPixels(Span<byte> destination, Dom.ImageDataPixelFormat pixelFormat, RenderingColorSpace colorSpace)
    {
        if ((pixelFormat == Dom.ImageDataPixelFormat.RgbaFloat16 || colorSpace != RenderingColorSpace.Srgb) && GetWideGamut() is { } wideGamut)
        {
            wideGamut.ReadPixels(destination, pixelFormat, colorSpace);
            return;
        }

        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_image is null, this);
            var length = checked(_width * _height * (pixelFormat == Dom.ImageDataPixelFormat.RgbaFloat16 ? 8 : 4));
            if (destination.Length < length)
                throw new ArgumentException("The destination is too small.", nameof(destination));

            if (pixelFormat == Dom.ImageDataPixelFormat.RgbaFloat16)
            {
                switch (GetImageCore(colorSpace, preserveRange: true))
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

            switch (GetImageCore(colorSpace, preserveRange: false))
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
        if ((preserveRange || colorSpace != RenderingColorSpace.Srgb) && GetWideGamut() is { } wideGamut)
            return wideGamut.GetFrame(colorSpace, preserveRange);

        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_image is null, this);
            var image = GetImageCore(colorSpace, preserveRange);
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

    /// <summary>
    /// Returns the pixels in a color space: the image, or its conversion (created the first time). The colors outside of the
    /// gamut of the color space are clipped, unless <paramref name="preserveRange"/> is set: a Display P3 image of 8 or 16
    /// bits is then converted to sRGB half floats.
    /// </summary>
    private Image GetImageCore(RenderingColorSpace colorSpace, bool preserveRange)
    {
        var image = _image!;
        if (colorSpace == _colorSpace)
            return image;

        if (preserveRange && image is not Image<RgbaHalf> && _colorSpace == RenderingColorSpace.DisplayP3)
        {
            if (_extended is null)
            {
                var pixels = new RgbaHalf[_width * _height];
                switch (image)
                {
                    case Image<Rgba32> narrow:
                    {
                        var source = new Rgba32[pixels.Length];
                        narrow.Frames[0].CopyPixelDataTo(source, 0);
                        for (var i = 0; i < pixels.Length; i++)
                        {
                            pixels[i] = new RgbaHalf((Half)(source[i].R / 255f), (Half)(source[i].G / 255f), (Half)(source[i].B / 255f), (Half)(source[i].A / 255f));
                        }

                        break;
                    }

                    case Image<Rgba64> wide:
                    {
                        var source = new Rgba64[pixels.Length];
                        wide.Frames[0].CopyPixelDataTo(source, 0);
                        for (var i = 0; i < pixels.Length; i++)
                        {
                            pixels[i] = new RgbaHalf((Half)(source[i].R / 65535f), (Half)(source[i].G / 65535f), (Half)(source[i].B / 65535f), (Half)(source[i].A / 65535f));
                        }

                        break;
                    }
                }

                ColorSpaces.Convert(pixels, _colorSpace, colorSpace);
                var bytes = unsafe(System.Runtime.InteropServices.MemoryMarshal.AsBytes(pixels.AsSpan()));
                _extended = Image.ImportPixelBytes<RgbaHalf>(bytes, _width, _height, _width * 8);
            }

            return _extended;
        }

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
                _extended?.Dispose();
                _extended = null;
                _wideGamut?.Dispose();
                _wideGamut = null;
                _wideGamutSource = null;
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
