namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Decodes one representation of an icon or cursor into an owned <see cref="Image"/>.
/// </summary>
/// <remarks>
/// <para>
/// A PNG payload is decoded by the PNG codec itself, over the bytes of the entry only; a DIB payload is expanded with the
/// shared BMP primitives plus the rules that only apply to icons:
/// </para>
/// <list type="bullet">
/// <item><description>the stored height covers the color rows and the 1-bit AND mask, so the displayed height is half of it;</description></item>
/// <item><description>rows are stored bottom-up, and each row of the color data and of the mask is padded to four bytes;</description></item>
/// <item><description>
/// a 32-bit <c>BI_RGB</c> payload stores alpha in its fourth byte (a standalone BMP does not). When that channel is
/// entirely zero it is unused — an all-transparent icon is never what the file means — and the AND mask alone determines
/// transparency. When the payload has a real alpha channel, the AND mask is ignored: the two mechanisms are redundant and
/// files whose mask contradicts their alpha are common.
/// </description></item>
/// <item><description>every DIB-backed representation therefore decodes to <see cref="PixelFormat.Rgba32"/>.</description></item>
/// </list>
/// <para>The hotspot of a cursor representation becomes the hotspot of the decoded frame.</para>
/// </remarks>
internal static class IcoEntryDecoder
{
    /// <summary>Decodes one representation.</summary>
    /// <param name="source">The input.</param>
    /// <param name="format">The container format (<see cref="ImageFormat.Ico"/> or <see cref="ImageFormat.Cur"/>).</param>
    /// <param name="representation">The resolved representation.</param>
    /// <param name="request">The requested representation and conversion policy.</param>
    /// <param name="context">The operation context.</param>
    /// <returns>A new image owned by the caller.</returns>
    public static Image Decode(RandomAccessSource source, ImageFormat format, IcoRepresentation representation, ImageDecodeRequest request, ImageCodecContext context)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(representation);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);
        var image = representation.PayloadFormat == ImageFormat.Png
            ? DecodePng(source, format, representation, request, context)
            : DecodeDib(source, format, representation, request, context);

        // The hotspot was validated against this representation when the directory was resolved
        Image.GetFrameForDecoder(image, 0).MetadataCore.SetHotspotUnchecked(representation.Hotspot);
        return image;
    }

    private static Image DecodePng(RandomAccessSource source, ImageFormat format, IcoRepresentation representation, ImageDecodeRequest request, ImageCodecContext context)
    {
        // The PNG codec charges the payload bytes itself, so the read does not charge them twice
        using var payload = source.ReadToBuffer(representation.Offset, representation.Length, charge: false);
        using var parser = PngCodec.Instance.CreateDecodeParser(request, context);
        var image = ImageInputPump.Run(payload.Span, parser, context);
        try
        {
            if (image.IsAnimated || image.Frames.Count != 1 || image.PosterFrame is not null)
                throw IcoFormat.Unsupported(format, string.Create(CultureInfo.InvariantCulture, $"The PNG payload of the representation {representation.Index} is an animation; an icon representation is a still image."), "Payload: APNG");

            image.Metadata.SourceFormat = format;
            return image;
        }
        catch
        {
            image.Dispose();
            throw;
        }
    }

    private static Image DecodeDib(RandomAccessSource source, ImageFormat format, IcoRepresentation representation, ImageDecodeRequest request, ImageCodecContext context)
    {
        var header = representation.DibHeader!.Value;
        var size = representation.Size;
        var layout = BmpDibLayout.Create(header);
        var scope = context.Scope;
        var colorRowLength = (int)BmpFormat.GetRowLength(size.Width, header.BitsPerPixel);
        var maskRowLength = (int)BmpFormat.GetRowLength(size.Width, 1);
        var expandedLength = size.Width * PixelFormats.GetBytesPerPixel(layout.SourcePixelFormat);
        var pixelsLength = size.Width * size.Height * 4;

        using var pixels = scope.Rent(pixelsLength, AllocationKind.DecoderState, clear: true);
        using var storedRow = scope.Rent(colorRowLength, AllocationKind.DecoderState, clear: false);
        using var expandedRow = scope.Rent(expandedLength, AllocationKind.DecoderState, clear: false);

        var dataOffset = representation.Offset + representation.DibPaletteOffset;
        if (header.IsIndexed)
        {
            using var palette = source.ReadToBuffer(dataOffset, header.PaletteLength);
            layout.SetPalette(palette.Span);
        }

        dataOffset += header.PaletteLength;
        var hasAlphaChannel = layout.HasAlpha;
        var sourceStep = PixelFormats.GetBytesPerPixel(layout.SourcePixelFormat);
        var anyAlpha = false;
        for (var row = 0; row < size.Height; row++)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            source.Read(dataOffset + ((long)row * colorRowLength), storedRow.Span);
            layout.ExpandRow(storedRow.Span, expandedRow.Span);

            // Icon rows are stored bottom-up
            var destination = pixels.Span.Slice((size.Height - 1 - row) * size.Width * 4, size.Width * 4);
            for (var x = 0; x < size.Width; x++)
            {
                var sourceOffset = x * sourceStep;
                var destinationOffset = x * 4;
                destination[destinationOffset] = expandedRow.Span[sourceOffset];
                destination[destinationOffset + 1] = expandedRow.Span[sourceOffset + 1];
                destination[destinationOffset + 2] = expandedRow.Span[sourceOffset + 2];
                var alpha = hasAlphaChannel ? expandedRow.Span[sourceOffset + 3] : (byte)255;
                destination[destinationOffset + 3] = alpha;
                anyAlpha |= alpha != 0;
            }
        }

        var useAlphaChannel = hasAlphaChannel && anyAlpha;
        if (!useAlphaChannel)
        {
            ApplyAndMask(source, representation, size, pixels.Span, dataOffset + ((long)colorRowLength * size.Height), maskRowLength, context);
        }

        var metadata = representation.Metadata.Clone();
        metadata.SourceFormat = format;
        using var sink = DecodedFrameSink.Create(context, request, format, size, PixelFormat.Rgba32, PixelFormat.Rgba32, iccProfile: null);
        sink.BeginFrame(FrameDuration.Zero);
        using (var lease = sink.LeaseCurrentFrame())
        {
            for (var y = 0; y < size.Height; y++)
            {
                sink.WriteRow(lease, y, pixels.Span.Slice(y * size.Width * 4, size.Width * 4));
            }
        }

        sink.EndImage();
        return sink.Build(metadata, animation: null);
    }

    /// <summary>Makes the pixels the 1-bit AND mask selects fully transparent, and the others fully opaque.</summary>
    private static void ApplyAndMask(RandomAccessSource source, IcoRepresentation representation, Size size, Span<byte> pixels, long maskOffset, int maskRowLength, ImageCodecContext context)
    {
        for (var i = 3; i < pixels.Length; i += 4)
        {
            pixels[i] = 255;
        }

        if (!representation.HasAndMask)
            return;

        using var maskRow = context.Scope.Rent(maskRowLength, AllocationKind.DecoderState, clear: false);
        for (var row = 0; row < size.Height; row++)
        {
            source.Read(maskOffset + ((long)row * maskRowLength), maskRow.Span);
            var destination = pixels.Slice((size.Height - 1 - row) * size.Width * 4, size.Width * 4);
            for (var x = 0; x < size.Width; x++)
            {
                if (((maskRow.Span[x >> 3] >> (7 - (x & 7))) & 1) != 0)
                {
                    destination[(x * 4) + 3] = 0;
                }
            }
        }
    }
}
