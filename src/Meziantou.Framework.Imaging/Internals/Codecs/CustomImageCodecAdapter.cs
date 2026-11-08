namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The internal codec of a <see cref="CustomImageCodec"/>: the input is buffered whole (a random-access codec, read through
/// <see cref="WholeInputParser{TResult}"/> or a seekable stream) and handed to the public codec in one call.
/// </summary>
internal sealed class CustomImageCodecAdapter(CustomImageCodec codec) : ImageCodec
{
    public CustomImageCodec Codec { get; } = codec;

    public override ImageFormat Format => ImageFormat.Custom;

    /// <summary>Gets a value indicating that the input is read whole before the public codec sees it.</summary>
    public override bool RequiresRandomAccess => true;

    public override bool IsMatch(ReadOnlySpan<byte> prefix) => Codec.IsMatch(prefix);

    public override ImageInfo IdentifyRandomAccess(RandomAccessSource source, ImageIdentifyMode mode, ImageCodecContext context)
    {
        var decodeContext = new CustomImageDecodeContext(Codec, context, request: null, mode);
        PooledBuffer? buffer = null;
        ImageInfo info;
        try
        {
            info = Codec.Identify(GetData(source, out buffer), decodeContext);
        }
        finally
        {
            buffer?.Dispose();
            decodeContext.Complete(result: null, expectsImage: false);
        }

        if (info is null || !ReferenceEquals(info.CustomFormatInfo, Codec.FormatInfo))
            throw new InvalidOperationException($"The {Codec.FormatInfo.Name} codec must describe its files with CustomImageCodec.CreateImageInfo.");

        return info;
    }

    public override Image DecodeRandomAccess(RandomAccessSource source, ImageDecodeRequest request, ImageCodecContext context)
    {
        var decodeContext = new CustomImageDecodeContext(Codec, context, request, identifyMode: null);
        PooledBuffer? buffer = null;
        Image? image = null;
        var returned = false;
        try
        {
            image = Codec.Decode(GetData(source, out buffer), decodeContext);
            returned = true;
        }
        finally
        {
            buffer?.Dispose();
            decodeContext.Complete(image, expectsImage: returned);
        }

        try
        {
            return Complete(image!, request, context);
        }
        catch
        {
            image!.Dispose();
            throw;
        }
    }

    /// <summary>Applies the frame limit, the per-input frame accounting and the requested pixel format to a decoded image.</summary>
    private static Image Complete(Image image, ImageDecodeRequest request, ImageCodecContext context)
    {
        image.Metadata.SourceFormat = ImageFormat.Custom;
        if (request.FrameLimit is { } limit)
        {
            while (image.Frames.Count > limit)
            {
                image.RemoveFrame(image.Frames.Count - 1);
            }
        }

        // The first frame was charged when the image was created; the frames the codec appended are charged now
        for (var i = 1; i < image.Frames.Count; i++)
        {
            context.Tracker.ChargeFrame(image.Size);
        }

        if (image.PosterFrame is not null)
        {
            context.Tracker.ChargeFrame(image.Size, isPoster: true);
        }

        if (request.PixelFormat is not { } pixelFormat || pixelFormat == image.PixelFormat)
            return image;

        var converted = image.CloneAs(pixelFormat, request.Conversion);
        image.Dispose();
        return converted;
    }

    /// <summary>Gets the whole input: in place when it is in memory, otherwise read into a buffer charged to the operation.</summary>
    private static ReadOnlySpan<byte> GetData(RandomAccessSource source, out PooledBuffer? buffer)
    {
        buffer = null;
        if (source.TryGetSpan(0, source.Length, out var span))
            return span;

        buffer = source.ReadToBuffer(0, source.Length);
        return buffer.Span;
    }
}
