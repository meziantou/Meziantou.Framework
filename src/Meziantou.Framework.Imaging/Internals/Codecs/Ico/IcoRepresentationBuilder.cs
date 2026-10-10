using System.Buffers.Binary;
using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Probes one icon or cursor payload and resolves it into an <see cref="IcoRepresentation"/>: a PNG payload through its
/// header chunks, a DIB payload through its <c>BITMAPINFOHEADER</c> and the icon-specific rules the BMP decoder must not
/// apply (doubled height, AND mask, implicit alpha of a 32-bit <c>BI_RGB</c> payload).
/// </summary>
internal readonly struct IcoRepresentationBuilder(
    RandomAccessSource source,
    ImageCodecContext context,
    ImageFormat format,
    int index,
    long offset,
    long length,
    Point? hotspot,
    int declaredWidth,
    int declaredHeight)
{
    private const int PngIhdrOffset = 8;
    private const int PngIhdrDataOffset = PngIhdrOffset + 8;
    private const int PngFirstChunkOffset = PngIhdrDataOffset + 13 + 4;

    public IcoRepresentation Resolve()
    {
        Span<byte> signature = stackalloc byte[8];
        if (length < signature.Length)
            throw Invalid(string.Create(CultureInfo.InvariantCulture, $"The payload of the icon directory entry {index} has {length} bytes, too few for any supported payload."));

        source.Read(offset, signature);
        return PngCodec.MatchesSignature(signature) ? ResolvePng() : ResolveDib();
    }

    private IcoRepresentation ResolvePng()
    {
        if (length < PngFirstChunkOffset)
            throw Invalid(string.Create(CultureInfo.InvariantCulture, $"The PNG payload of the icon directory entry {index} has {length} bytes, too few for its header."));

        Span<byte> ihdr = stackalloc byte[8 + 13];
        source.Read(offset + PngIhdrOffset, ihdr);
        if (BinaryPrimitives.ReadUInt32BigEndian(ihdr) != 13 || !ihdr[4..8].SequenceEqual("IHDR"u8))
            throw Invalid(string.Create(CultureInfo.InvariantCulture, $"The PNG payload of the icon directory entry {index} does not start with an IHDR chunk."));

        var data = ihdr[8..];
        var width = BinaryPrimitives.ReadUInt32BigEndian(data);
        var height = BinaryPrimitives.ReadUInt32BigEndian(data[4..]);
        var bitDepth = data[8];
        var colorType = data[9];
        var size = ValidateSize(width, height);
        var (hasTransparency, isAnimated) = ProbePngChunks();
        if (isAnimated)
            throw Unsupported(string.Create(CultureInfo.InvariantCulture, $"The payload of the icon directory entry {index} is an animated PNG; an icon representation is a still image."), "Payload: APNG");

        var pixelFormat = DefaultPixelFormats.ForPng(colorType, bitDepth, hasTransparency, isAnimated: false);
        var channels = colorType switch { 0 => 1, 2 => 3, 3 => 1, 4 => 2, _ => 4 };
        return new IcoRepresentation
        {
            Index = index,
            Offset = offset,
            Length = length,
            PayloadFormat = ImageFormat.Png,
            Size = size,
            BitsPerPixel = bitDepth * channels,
            PixelFormat = pixelFormat,
            ColorModel = colorType switch
            {
                0 => ImageColorModel.Grayscale,
                2 => ImageColorModel.Rgb,
                3 => ImageColorModel.Indexed,
                4 => ImageColorModel.GrayscaleAlpha,
                _ => ImageColorModel.Rgba,
            },
            BitsPerComponent = bitDepth,
            MayHaveTransparency = hasTransparency || colorType is 4 or 6,
            Hotspot = ValidateHotspot(size),
            Metadata = new ImageMetadata { SourceFormat = format },
        };
    }

    /// <summary>Walks the chunk headers before the first <c>IDAT</c>, reading 8 bytes per chunk and skipping the data.</summary>
    private (bool HasTransparency, bool IsAnimated) ProbePngChunks()
    {
        var hasTransparency = false;
        var position = (long)PngFirstChunkOffset;
        Span<byte> chunk = stackalloc byte[8];
        while (position + 12 <= length)
        {
            source.Read(offset + position, chunk);
            var dataLength = BinaryPrimitives.ReadUInt32BigEndian(chunk);
            if (dataLength > int.MaxValue)
                throw Invalid(string.Create(CultureInfo.InvariantCulture, $"A chunk of the PNG payload of the icon directory entry {index} declares {dataLength} bytes."));

            var type = chunk[4..8];
            if (type.SequenceEqual("IDAT"u8))
                return (hasTransparency, IsAnimated: false);

            if (type.SequenceEqual("acTL"u8))
                return (hasTransparency, IsAnimated: true);

            if (type.SequenceEqual("tRNS"u8))
            {
                hasTransparency = true;
            }

            position += 12 + dataLength;
        }

        throw Invalid(string.Create(CultureInfo.InvariantCulture, $"The PNG payload of the icon directory entry {index} has no image data."));
    }

    private IcoRepresentation ResolveDib()
    {
        Span<byte> sizeField = stackalloc byte[4];
        source.Read(offset, sizeField);
        var headerLength = BinaryPrimitives.ReadUInt32LittleEndian(sizeField);
        if (!BmpInfoHeader.IsSupportedHeaderLength(headerLength))
            throw Unsupported(string.Create(CultureInfo.InvariantCulture, $"The payload of the icon directory entry {index} is neither a PNG nor a supported DIB (its header declares {headerLength} bytes)."), string.Create(CultureInfo.InvariantCulture, $"Payload: DIB header of {headerLength} bytes"));

        if (headerLength > length)
            throw Invalid(string.Create(CultureInfo.InvariantCulture, $"The DIB payload of the icon directory entry {index} has {length} bytes, fewer than its {headerLength}-byte header."));

        using var headerBuffer = source.ReadToBuffer(offset, headerLength);
        BmpInfoHeader header;
        try
        {
            header = BmpInfoHeader.Parse(headerBuffer.Span, (int)headerLength);
        }
        catch (ImageException exception)
        {
            throw Rewrap(exception);
        }

        var payloadOffset = (long)headerLength;
        if (header.TrailingMaskLength > 0)
        {
            if (payloadOffset + header.TrailingMaskLength > length)
                throw Invalid(string.Create(CultureInfo.InvariantCulture, $"The DIB payload of the icon directory entry {index} is too short for its color masks."));

            using var masks = source.ReadToBuffer(offset + payloadOffset, header.TrailingMaskLength);
            header = header.WithTrailingMasks(masks.Span);
            payloadOffset += header.TrailingMaskLength;
        }

        if (header.IsTopDown)
            throw Unsupported(string.Create(CultureInfo.InvariantCulture, $"The DIB payload of the icon directory entry {index} declares a negative height (top-down rows); icon payloads store bottom-up rows and a doubled height."), "Payload: top-down DIB");

        if (header.Height % 2 != 0)
            throw Invalid(string.Create(CultureInfo.InvariantCulture, $"The DIB payload of the icon directory entry {index} declares the odd height {header.Height}; an icon stores the color rows and the AND mask, so the height is always doubled."));

        var size = ValidateSize((uint)header.Width, (uint)(header.Height / 2));
        var paletteOffset = (int)payloadOffset;
        payloadOffset += header.PaletteLength;
        if (payloadOffset > length)
            throw Invalid(string.Create(CultureInfo.InvariantCulture, $"The DIB payload of the icon directory entry {index} is too short for its {header.PaletteEntries}-entry palette."));

        var colorLength = BmpFormat.GetRowLength(size.Width, header.BitsPerPixel) * size.Height;
        if (payloadOffset + colorLength > length)
            throw Invalid(string.Create(CultureInfo.InvariantCulture, $"The DIB payload of the icon directory entry {index} is too short for its {size.Width}x{size.Height} color rows."));

        var maskLength = BmpFormat.GetRowLength(size.Width, 1) * size.Height;
        var hasAndMask = payloadOffset + colorLength + maskLength <= length;
        var hasImplicitAlpha = header.BitsPerPixel == 32 && header.Compression == BmpCompression.Rgb;
        if (hasImplicitAlpha)
        {
            // An icon payload stores alpha in the fourth byte of a 32-bit BI_RGB pixel; a standalone BMP does not
            header = header.WithIconAlphaChannel();
        }

        return new IcoRepresentation
        {
            Index = index,
            Offset = offset,
            Length = length,
            PayloadFormat = ImageFormat.Bmp,
            Size = size,
            BitsPerPixel = header.BitsPerPixel,
            PixelFormat = PixelFormat.Rgba32,
            ColorModel = header.IsIndexed ? ImageColorModel.Indexed : ImageColorModel.Rgba,
            BitsPerComponent = header.IsIndexed ? header.BitsPerPixel : 8,
            MayHaveTransparency = true,
            Hotspot = ValidateHotspot(size),
            Metadata = new ImageMetadata { SourceFormat = format },
            DibHeader = header,
            DibPaletteOffset = paletteOffset,
            HasAndMask = hasAndMask,
            HasImplicitAlpha = hasImplicitAlpha,
        };
    }

    private Size ValidateSize(uint width, uint height)
    {
        if (width is 0 or > IcoFormat.MaxDimension || height is 0 or > IcoFormat.MaxDimension)
            throw Invalid(string.Create(CultureInfo.InvariantCulture, $"The representation {index} is {width}x{height}; an icon or cursor stores representations of 1 to {IcoFormat.MaxDimension} pixels per side."));

        var size = new Size((int)width, (int)height);
        context.Limits.EnsureCanvasWithinLimits(size.Width, size.Height);

        // The directory bytes cannot express more than 256 and are routinely wrong; they are only checked for plausibility
        if (declaredWidth != size.Width && declaredWidth != IcoFormat.MaxDimension)
            throw Invalid(string.Create(CultureInfo.InvariantCulture, $"The icon directory entry {index} declares a width of {declaredWidth} but its payload is {size.Width} pixels wide."));

        if (declaredHeight != size.Height && declaredHeight != IcoFormat.MaxDimension)
            throw Invalid(string.Create(CultureInfo.InvariantCulture, $"The icon directory entry {index} declares a height of {declaredHeight} but its payload is {size.Height} pixels high."));

        return size;
    }

    private Point? ValidateHotspot(Size size)
    {
        if (hotspot is not { } point)
            return null;

        if (point.X >= size.Width || point.Y >= size.Height)
            throw Invalid(string.Create(CultureInfo.InvariantCulture, $"The hotspot ({point.X}, {point.Y}) of the cursor representation {index} is outside its {size.Width}x{size.Height} image."));

        return point;
    }

    private InvalidImageContentException Invalid(string message) => IcoFormat.Invalid(format, message);

    private UnsupportedImageFeatureException Unsupported(string message, string feature) => IcoFormat.Unsupported(format, message, feature);

    /// <summary>Reports a DIB error as an error of the icon or cursor file, not of a standalone BMP.</summary>
    private Exception Rewrap(ImageException exception) => exception switch
    {
        UnsupportedImageFeatureException unsupported => IcoFormat.Unsupported(format, $"The DIB payload of the icon directory entry {index} is not supported: {unsupported.Message}", unsupported.Feature ?? "Payload: DIB"),
        _ => IcoFormat.Invalid(format, $"The DIB payload of the icon directory entry {index} is malformed: {exception.Message}"),
    };
}
