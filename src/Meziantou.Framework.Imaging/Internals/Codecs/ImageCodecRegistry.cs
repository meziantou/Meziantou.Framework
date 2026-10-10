namespace Meziantou.Framework.Imaging.Internals;

/// <summary>The ordered set of codecs used for content-based dispatch.</summary>
/// <remarks>
/// The public API always uses <see cref="Current"/>: <see cref="Default"/> (PNG/APNG, GIF, JPEG, WebP, ANI, QOI, BMP, TIFF/BigTIFF, ICO, CUR, PNM, TGA), unless a test replaced it
/// for the current asynchronous flow with <see cref="Override"/>. Tests use this to plug test-only decoders into the real
/// public entry points (paths, streams, spans, sync and async) without any public extensibility.
/// </remarks>
internal sealed class ImageCodecRegistry
{
    private static readonly AsyncLocal<ImageCodecRegistry?> CurrentOverride = new();

    public ImageCodecRegistry(IEnumerable<ImageCodec> codecs)
    {
        ArgumentNullException.ThrowIfNull(codecs);
        Codecs = [.. codecs];
        foreach (var codec in Codecs)
        {
            ArgumentNullException.ThrowIfNull(codec, nameof(codecs));
        }
    }

    /// <summary>
    /// Gets the built-in codecs: PNG (including APNG), GIF, JPEG, WebP, ANI, QOI, BMP, TIFF (including BigTIFF), ICO, CUR,
    /// PNM and TGA. TGA has no signature and is recognized by the plausibility of its header, so it is always consulted last;
    /// ICO and CUR have a very weak one and are therefore consulted before it (<see cref="IcoFormat.MatchesSignature"/>).
    /// </summary>
    public static ImageCodecRegistry Default { get; } = new([PngCodec.Instance, GifCodec.Instance, JpegCodec.Instance, WebPCodec.Instance, AniCodec.Instance, QoiCodec.Instance, BmpCodec.Instance, TiffCodec.Instance, IcoCodec.Icon, IcoCodec.Cursor, PnmCodec.Instance, TgaCodec.Instance]);

    /// <summary>Gets the registry used by the public API in the current asynchronous flow.</summary>
    public static ImageCodecRegistry Current => CurrentOverride.Value ?? Default;

    /// <summary>Gets the codecs, in detection order.</summary>
    public IReadOnlyList<ImageCodec> Codecs { get; }

    /// <summary>Replaces <see cref="Current"/> for the current asynchronous flow until the returned scope is disposed (tests only).</summary>
    /// <param name="registry">The registry.</param>
    /// <returns>A scope restoring the previous registry.</returns>
    public static IDisposable Override(ImageCodecRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        var previous = CurrentOverride.Value;
        CurrentOverride.Value = registry;
        return new OverrideScope(previous);
    }

    /// <summary>
    /// The shortest prefix examined: PNG, GIF, JPEG and QOI signatures are recognized from 8 bytes (BMP from 2), WebP and ANI need
    /// the 12-byte RIFF header and TGA its whole 18-byte header (<see cref="Image.FormatDetectionPrefixLength"/>).
    /// </summary>
    public const int MinimumPrefixLength = 8;

    /// <summary>Selects the codec whose signature matches the first bytes of the data.</summary>
    /// <param name="prefix">The first bytes of the data.</param>
    /// <returns>The codec, or <see langword="null"/> when the prefix is shorter than <see cref="MinimumPrefixLength"/> or not recognized (each codec checks that the prefix holds its whole signature).</returns>
    public ImageCodec? Detect(ReadOnlySpan<byte> prefix)
    {
        if (prefix.Length < MinimumPrefixLength)
            return null;

        foreach (var codec in Codecs)
        {
            if (codec.IsMatch(prefix))
                return codec;
        }

        return null;
    }

    private sealed class OverrideScope(ImageCodecRegistry? previous) : IDisposable
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
