namespace Meziantou.Framework.Imaging.Internals;

/// <summary>The encoder of each output format.</summary>
/// <remarks>
/// The public API always uses <see cref="Current"/>: <see cref="Default"/>, unless a test replaced it for the current
/// asynchronous flow with <see cref="Override"/> to plug test-only encoders behind the real writer and save entry points.
/// Every built-in format has its encoder: PNG and APNG, GIF, JPEG, WebP, QOI, BMP, TGA, PNM, TIFF/BigTIFF, ICO/CUR and ANI.
/// </remarks>
internal sealed class ImageEncoderRegistry
{
    private static readonly AsyncLocal<ImageEncoderRegistry?> CurrentOverride = new();

    public ImageEncoderRegistry(IEnumerable<ImageEncoderCodec> codecs)
    {
        ArgumentNullException.ThrowIfNull(codecs);
        Codecs = [.. codecs];
        foreach (var codec in Codecs)
        {
            ArgumentNullException.ThrowIfNull(codec, nameof(codecs));
        }
    }

    /// <summary>Gets the built-in encoders: static PNG and APNG, through the PNG codec, GIF, baseline JPEG, WebP (lossless and lossy), QOI, BMP, TGA, PNM, TIFF and BigTIFF, ICO and CUR, and ANI.</summary>
    public static ImageEncoderRegistry Default { get; } = new(
    [
        PngEncoderCodec.Instance,
        GifEncoderCodec.Instance,
        JpegEncoderCodec.Instance,
        WebPEncoderCodec.Instance,
        QoiEncoderCodec.Instance,
        BmpEncoderCodec.Instance,
        TgaEncoderCodec.Instance,
        PnmEncoderCodec.Instance,
        TiffEncoderCodec.Instance,
        IcoEncoderCodec.Icon,
        IcoEncoderCodec.Cursor,
        AniEncoderCodec.Instance,
    ]);

    /// <summary>Gets the registry used by the public API in the current asynchronous flow.</summary>
    public static ImageEncoderRegistry Current => CurrentOverride.Value ?? Default;

    public IReadOnlyList<ImageEncoderCodec> Codecs { get; }

    /// <summary>Replaces <see cref="Current"/> for the current asynchronous flow until the returned scope is disposed (tests only).</summary>
    /// <param name="registry">The registry.</param>
    /// <returns>A scope restoring the previous registry.</returns>
    public static IDisposable Override(ImageEncoderRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        var previous = CurrentOverride.Value;
        CurrentOverride.Value = registry;
        return new OverrideScope(previous);
    }

    /// <summary>Gets the encoder of a format.</summary>
    /// <param name="format">The format.</param>
    /// <returns>The encoder.</returns>
    /// <exception cref="NotSupportedException">No encoder is registered for the format.</exception>
    public ImageEncoderCodec Get(ImageFormat format)
    {
        foreach (var codec in Codecs)
        {
            if (codec.Format == format)
                return codec;
        }

        throw new NotSupportedException($"No encoder is registered for the {ImageFormatNames.Get(format)} format.");
    }

    private sealed class OverrideScope(ImageEncoderRegistry? previous) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            CurrentOverride.Value = previous;
        }
    }
}
