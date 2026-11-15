using System.Runtime.InteropServices;
using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Decodes the images of a KTX 2.0 texture: one subresource into a still image, or the layers of the base level of an
/// animation (<c>KTXanimData</c>) into the frames of an animated image.
/// </summary>
/// <remarks>
/// <para>
/// A level that is not supercompressed is read row of blocks by row of blocks, where its image is. A Zstandard or ZLIB
/// level is inflated whole (its images are one stream), into a buffer of its uncompressed length, which must be produced
/// exactly. A BasisLZ level is decoded image by image: the slices of the image, located by its image descriptor, give the
/// ETC1S blocks, which are ETC1 blocks (Khronos Data Format Specification, section 21.1) decoded as such.
/// </para>
/// <para>
/// Each row of blocks is decoded into a strip of RGBA texels in the precision of the format, mapped by the swizzle, and
/// its rows are given to the frame sink. The texels of the blocks that are outside the image (the right and the bottom
/// of an image whose size is not a multiple of the block size) are decoded and dropped.
/// </para>
/// </remarks>
internal static class Ktx2Decoder
{
    /// <summary>Describes a texture for <c>Image.Identify</c>: its default image, and the number of its subresources.</summary>
    public static ImageInfo Identify(RandomAccessSource source, ImageIdentifyMode mode, ImageCodecContext context)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(context);
        var texture = Ktx2Texture.Read(source, context);
        EnsureDecodable(texture);
        var output = Ktx2Output.Create(texture);
        var metadata = Ktx2Output.CreateMetadata(texture);
        var animation = texture.Animation;
        var frameCount = animation is null ? 1 : texture.Layers;
        if (mode == ImageIdentifyMode.FullScan)
        {
            for (var i = 0; i < frameCount; i++)
            {
                context.Tracker.ChargeScannedFrame();
            }
        }

        return new ImageInfo(
            ImageFormat.Ktx2,
            new Size(texture.GetLevelWidth(0), texture.GetLevelHeight(0)),
            output.DefaultPixelFormat,
            output.ColorModel,
            output.BitsPerComponent,
            frameCount,
            isAnimated: animation is not null,
            hasPosterFrame: false,
            output.MayHaveTransparency,
            animation is null ? null : CreateAnimationMetadata(animation.Value),
            metadata,
            mode,
            collectionEntryCount: (int)texture.SubresourceCount);
    }

    /// <summary>Decodes the default image: the base level of the first face of the first layer, or the animation.</summary>
    public static Image Decode(RandomAccessSource source, ImageDecodeRequest request, ImageCodecContext context)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);
        var texture = Ktx2Texture.Read(source, context);
        EnsureDecodable(texture);
        return texture.Animation is null
            ? DecodeSubresource(source, texture, default, request, context)
            : DecodeAnimation(source, texture, request, context);
    }

    /// <summary>Rejects the textures whose images the decoder does not produce, before anything is allocated.</summary>
    /// <exception cref="UnsupportedImageFeatureException">The format, the alpha or the color description is not supported.</exception>
    public static void EnsureDecodable(Ktx2Texture texture)
    {
        if (texture.Format is null)
        {
            var (reason, feature) = Ktx2VkFormats.GetRejection(texture.VkFormat);
            throw Ktx2Format.Unsupported("The KTX 2.0 file stores " + Ktx2VkFormats.GetName(texture.VkFormat) + " texels: " + reason, feature);
        }

        if (texture.IsPremultiplied)
            throw Ktx2Format.Unsupported("The KTX 2.0 texture has premultiplied alpha (KHR_DF_FLAG_ALPHA_PREMULTIPLIED): dividing its colors by alpha would lose precision, and the library keeps straight alpha only.", "Premultiplied alpha");
    }

    /// <summary>Decodes one subresource into a still image.</summary>
    public static Image DecodeSubresource(RandomAccessSource source, Ktx2Texture texture, Ktx2Subresource subresource, ImageDecodeRequest request, ImageCodecContext context)
    {
        var output = Ktx2Output.Create(texture);
        var metadata = Ktx2Output.CreateMetadata(texture);
        var size = new Size(texture.GetLevelWidth(subresource.MipLevel), texture.GetLevelHeight(subresource.MipLevel));
        context.Limits.EnsureCanvasWithinLimits(size.Width, size.Height);
        using var sink = DecodedFrameSink.Create(context, request, ImageFormat.Ktx2, size, output.DefaultPixelFormat, output.SourcePixelFormat, iccProfile: null);
        using var level = Ktx2LevelData.Open(source, texture, subresource.MipLevel, context);
        if (NeedsStaging(sink, output))
        {
            using var staging = CreateStaging(output, size, context);
            WriteImage(sink, texture, output, level, subresource, previousIndices: null, context, staging.FrameCore(0))?.Dispose();
            WriteStaged(sink, [staging.FrameCore(0)], FrameDuration.Zero);
        }
        else
        {
            sink.BeginFrame(FrameDuration.Zero);

            // The ETC1S indices are the reference of a next P-frame only: a still image releases them
            WriteImage(sink, texture, output, level, subresource, previousIndices: null, context)?.Dispose();
        }

        sink.EndImage();
        return sink.Build(metadata, animation: null);
    }

    private static Image DecodeAnimation(RandomAccessSource source, Ktx2Texture texture, ImageDecodeRequest request, ImageCodecContext context)
    {
        var animation = texture.Animation!.Value;
        var output = Ktx2Output.Create(texture);
        var metadata = Ktx2Output.CreateMetadata(texture);
        var size = new Size(texture.GetLevelWidth(0), texture.GetLevelHeight(0));
        using var sink = DecodedFrameSink.Create(context, request, ImageFormat.Ktx2, size, output.DefaultPixelFormat, output.SourcePixelFormat, iccProfile: null);
        using var level = Ktx2LevelData.Open(source, texture, 0, context);
        var duration = new FrameDuration(animation.Duration, animation.Timescale);
        if (NeedsStaging(sink, output))
        {
            // The floating-point frames are converted together, as CloneAs converts them: the peak of the tone mapping is
            // the one of the whole animation (the frames of floating-point formats are never P-frames)
            var count = Math.Min(texture.Layers, request.FrameLimit ?? int.MaxValue);
            EnsureFramesWithinLimits(context, count, size);
            var stagings = new List<Image>(count);
            try
            {
                for (var layer = 0; layer < count; layer++)
                {
                    context.CancellationToken.ThrowIfCancellationRequested();
                    var staging = CreateStaging(output, size, context);
                    stagings.Add(staging);
                    WriteImage(sink, texture, output, level, new Ktx2Subresource(0, layer, 0, 0), previousIndices: null, context, staging.FrameCore(0))?.Dispose();
                }

                WriteStaged(sink, [.. stagings.Select(staging => staging.FrameCore(0))], duration);
            }
            finally
            {
                foreach (var staging in stagings)
                {
                    staging.Dispose();
                }
            }

            sink.EndImage();
            return sink.Build(metadata, CreateAnimationMetadata(animation));
        }

        Etc1SIndices? previous = null;
        try
        {
            for (var layer = 0; layer < texture.Layers && !sink.IsFrameLimitReached; layer++)
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                sink.BeginFrame(duration);
                var indices = WriteImage(sink, texture, output, level, new Ktx2Subresource(0, layer, 0, 0), previous, context);
                previous?.Dispose();
                previous = indices;
            }
        }
        finally
        {
            previous?.Dispose();
        }

        sink.EndImage();
        return sink.Build(metadata, CreateAnimationMetadata(animation));
    }

    /// <summary>Decodes the image of a subresource whose BasisLZ data are P-frames: the frames before it are decoded first, without pixels.</summary>
    public static Image DecodeCollectionEntry(RandomAccessSource source, Ktx2Texture texture, Ktx2Subresource subresource, ImageDecodeRequest request, ImageCodecContext context)
    {
        if (texture.Scheme != Ktx2Format.SchemeBasisLz || !texture.BasisImages[texture.GetBasisImageIndex(subresource)].IsPFrame)
            return DecodeSubresource(source, texture, subresource, request, context);

        // A P-frame refers to the indices of the frame before it, and so on back to an I-frame (Appendix D.5.3)
        var output = Ktx2Output.Create(texture);
        var metadata = Ktx2Output.CreateMetadata(texture);
        var size = new Size(texture.GetLevelWidth(0), texture.GetLevelHeight(0));
        using var sink = DecodedFrameSink.Create(context, request, ImageFormat.Ktx2, size, output.DefaultPixelFormat, output.SourcePixelFormat, iccProfile: null);
        using var level = Ktx2LevelData.Open(source, texture, 0, context);
        var first = subresource.ArrayLayer;
        while (first > 0 && texture.BasisImages[texture.GetBasisImageIndex(new Ktx2Subresource(0, first, 0, 0))].IsPFrame)
        {
            first--;
        }

        Etc1SIndices? previous = null;
        try
        {
            for (var layer = first; layer < subresource.ArrayLayer; layer++)
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                context.Tracker.ChargeHiddenPixels(size.Area);
                var indices = level.DecodeEtc1S(new Ktx2Subresource(0, layer, 0, 0), previous, context);
                previous?.Dispose();
                previous = indices;
            }

            sink.BeginFrame(FrameDuration.Zero);
            WriteImage(sink, texture, output, level, subresource, previous, context)?.Dispose();
        }
        finally
        {
            previous?.Dispose();
        }

        sink.EndImage();
        return sink.Build(metadata, animation: null);
    }

    private static AnimationMetadata CreateAnimationMetadata(Ktx2Animation animation)
        => new() { TotalPlays = animation.LoopCount == 0 ? null : (int)Math.Min(animation.LoopCount, int.MaxValue) };

    /// <summary>
    /// Determines whether the texels are decoded to a staging image first: floating-point texels converted to another
    /// format, whose conversion needs the peak of the whole image (tone mapping) and validates every sample before a row is
    /// converted (<see cref="Image.PrepareConversion"/>), as <c>CloneAs</c> does.
    /// </summary>
    private static bool NeedsStaging(DecodedFrameSink sink, Ktx2Output output) => !sink.Plan.IsIdentity && output.Precision is Ktx2OutputPrecision.Half or Ktx2OutputPrecision.Float;

    /// <summary>
    /// Checks the frames of a staged animation against the frame and pixel limits before any of them is decoded:
    /// <see cref="DecodedFrameSink.BeginFrame"/> charges them once they are written, after the decoding.
    /// </summary>
    /// <exception cref="ImageResourceLimitException">The frames exceed <see cref="ImageResourceLimits.MaxFrames"/> or <see cref="ImageResourceLimits.MaxTotalPixels"/>.</exception>
    private static void EnsureFramesWithinLimits(ImageCodecContext context, int count, Size size)
    {
        var limits = context.Limits;
        var frames = context.Tracker.Frames + (long)count;
        if (frames > limits.MaxFrames)
            throw new ImageResourceLimitException(ImageResourceLimitKind.Frames, limits.MaxFrames, frames);

        var pixels = context.Tracker.TotalPixels + ((long)count * size.Area);
        if (pixels > limits.MaxTotalPixels)
            throw new ImageResourceLimitException(ImageResourceLimitKind.TotalPixels, limits.MaxTotalPixels, pixels);
    }

    private static Image CreateStaging(Ktx2Output output, Size size, ImageCodecContext context) => output.Precision == Ktx2OutputPrecision.Half
        ? new Image<RgbaHalf>(context.Configuration, size, context.Scope, layoutOptions: null)
        : new Image<RgbaFloat>(context.Configuration, size, context.Scope, layoutOptions: null);

    /// <summary>Prepares the conversion of the staged frames (the peak, the validation), then writes them to the sink, one frame each.</summary>
    private static void WriteStaged(DecodedFrameSink sink, IReadOnlyList<ImageFrame> frames, FrameDuration duration)
    {
        Image.PrepareConversion(sink.Plan, frames);
        foreach (var frame in frames)
        {
            sink.BeginFrame(duration);
            using var source = frame.GetStorage().AcquireLease();
            using var destination = sink.LeaseCurrentFrame();
            for (var y = 0; y < source.Height; y++)
            {
                sink.WriteRow(destination, y, source.GetRowBytes(y));
            }
        }
    }

    /// <summary>Writes the rows of one image to the current frame of the sink, or to a staging frame of the source layout.</summary>
    /// <returns>The ETC1S indices of a BasisLZ image (the reference of the next P-frame), or <see langword="null"/>.</returns>
    private static Etc1SIndices? WriteImage(DecodedFrameSink sink, Ktx2Texture texture, Ktx2Output output, Ktx2LevelData level, Ktx2Subresource subresource, Etc1SIndices? previousIndices, ImageCodecContext context, ImageFrame? staging = null)
    {
        var format = texture.Format!;
        var width = texture.GetLevelWidth(subresource.MipLevel);
        var height = texture.GetLevelHeight(subresource.MipLevel);
        var (columns, rows) = texture.GetBlockCounts(subresource.MipLevel);
        var componentLength = output.ComponentLength;
        var stripStride = Ktx2Format.ToBufferLength(Ktx2Format.Multiply(Ktx2Format.Multiply(columns, format.BlockWidth), 4 * componentLength), context.Limits);
        var rowBlocksLength = Ktx2Format.ToBufferLength(Ktx2Format.Multiply(columns, format.BytesPerBlock), context.Limits);
        using var strip = context.Scope.Rent(Ktx2Format.ToBufferLength(Ktx2Format.Multiply(stripStride, format.BlockHeight), context.Limits), AllocationKind.DecoderState, clear: true);
        using var blocks = context.Scope.Rent(rowBlocksLength, AllocationKind.DecoderState, clear: false);
        Etc1SIndices? indices = null;
        try
        {
            if (format.Encoding == Ktx2Encoding.Etc1S)
            {
                indices = level.DecodeEtc1S(subresource, previousIndices, context);
            }

            var imageOffset = format.Encoding == Ktx2Encoding.Etc1S ? 0 : texture.GetImageIndex(subresource) * texture.GetImageLength(subresource.MipLevel);
            using var lease = staging is null ? sink.LeaseCurrentFrame() : staging.GetStorage().AcquireLease();
            var rowLength = width * 4 * componentLength;
            for (var row = 0; row < rows; row++)
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                var stripSpan = strip.Span;
                if (indices is not null)
                {
                    indices.DecodeRow(row, stripSpan, stripStride);
                }
                else
                {
                    level.Read(imageOffset + ((long)row * rowBlocksLength), blocks.Span);
                    DecodeBlockRow(format, blocks.Span, columns, stripSpan, stripStride, output.Precision, texture.TransferFunction == Ktx2Format.TransferSrgb);
                }

                for (var y = 0; y < format.BlockHeight; y++)
                {
                    var imageRow = (row * format.BlockHeight) + y;
                    if (imageRow >= height)
                        break;

                    var texels = stripSpan.Slice(y * stripStride, rowLength);
                    if (!output.IsIdentity)
                    {
                        ApplySwizzle(texels, output.Mapping, output.Precision);
                    }

                    if (staging is null)
                    {
                        sink.WriteRow(lease, imageRow, texels);
                    }
                    else
                    {
                        texels.CopyTo(lease.GetRowBytes(imageRow));
                    }
                }
            }

            var result = indices;
            indices = null;
            return result;
        }
        finally
        {
            indices?.Dispose();
        }
    }

    private static void DecodeBlockRow(Ktx2VkFormat format, ReadOnlySpan<byte> blocks, int columns, Span<byte> strip, int stripStride, Ktx2OutputPrecision precision, bool srgbTransfer)
    {
        if (format.Encoding == Ktx2Encoding.Uncompressed)
        {
            Ktx2UncompressedTexels.DecodeRow(format, blocks, columns, strip);
            return;
        }

        var texelLength = 4 * (precision switch { Ktx2OutputPrecision.Byte => 1, Ktx2OutputPrecision.Float => 4, _ => 2 });
        for (var column = 0; column < columns; column++)
        {
            var block = blocks.Slice(column * format.BytesPerBlock, format.BytesPerBlock);
            var destination = strip[(column * format.BlockWidth * texelLength)..];
            switch (format.Encoding)
            {
                case Ktx2Encoding.Etc2:
                    Etc2BlockDecoder.Decode(format, block, destination, stripStride);
                    break;
                case Ktx2Encoding.Eac:
                    Etc2BlockDecoder.DecodeEac(format, block, destination, stripStride);
                    break;
                case Ktx2Encoding.Bcn:
                    Ktx2BcnBlocks.Decode(format, block, destination, stripStride);
                    break;
                case Ktx2Encoding.Astc:
                    AstcBlockDecoder.Decode(block, format.BlockWidth, format.BlockHeight, AstcBlockDecoder.GetProfile(format, precision), destination, stripStride);
                    break;
                case Ktx2Encoding.Uastc:
                    UastcBlockDecoder.Decode(block, srgbTransfer, destination, stripStride);
                    break;
                default:
                    throw new InvalidOperationException("The encoding has no block decoder.");
            }
        }
    }

    /// <summary>Maps the channels of a row of RGBA texels by the swizzle of the texture.</summary>
    private static void ApplySwizzle(Span<byte> texels, int[] mapping, Ktx2OutputPrecision precision)
    {
        switch (precision)
        {
            case Ktx2OutputPrecision.Byte:
                Swizzle(texels, mapping, (byte)255);
                break;
            case Ktx2OutputPrecision.UInt16:
                Swizzle(unsafe(MemoryMarshal.Cast<byte, ushort>(texels)), mapping, ushort.MaxValue);
                break;
            case Ktx2OutputPrecision.Half:
                Swizzle(unsafe(MemoryMarshal.Cast<byte, ushort>(texels)), mapping, BitConverter.HalfToUInt16Bits(Half.One));
                break;
            default:
                Swizzle(unsafe(MemoryMarshal.Cast<byte, uint>(texels)), mapping, BitConverter.SingleToUInt32Bits(1f));
                break;
        }
    }

    private static void Swizzle<T>(Span<T> texels, int[] mapping, T one)
        where T : unmanaged
    {
        Span<T> pixel = stackalloc T[4];
        for (var i = 0; i < texels.Length; i += 4)
        {
            var texel = texels.Slice(i, 4);
            for (var c = 0; c < 4; c++)
            {
                pixel[c] = mapping[c] switch
                {
                    Ktx2Output.Zero => default,
                    Ktx2Output.One => one,
                    var source => texel[source],
                };
            }

            pixel.CopyTo(texel);
        }
    }
}
