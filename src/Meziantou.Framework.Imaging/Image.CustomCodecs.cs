using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging;

public abstract partial class Image
{
    /// <summary>
    /// Detects the format of data from its first bytes, with the custom codecs of a configuration
    /// (<see cref="ImageConfiguration.Codecs"/>) and the built-in ones, as <c>Load</c> and <c>Identify</c> do.
    /// </summary>
    /// <param name="prefix">
    /// The first bytes of the data. Provide at least <see cref="FormatDetectionPrefixLength"/> bytes; a prefix shorter than 8
    /// bytes is never recognized.
    /// </param>
    /// <param name="configuration">The configuration whose custom codecs are consulted first, or <see langword="null"/> for <see cref="ImageConfiguration.Default"/>.</param>
    /// <returns>
    /// The description of the format: the <see cref="CustomImageCodec.FormatInfo"/> of a custom codec, or the one of
    /// <see cref="ImageFormatInfo.Get(ImageFormat)"/>; <see langword="null"/> when no codec recognizes the data.
    /// </returns>
    public static ImageFormatInfo? DetectFormatInfo(ReadOnlySpan<byte> prefix, ImageConfiguration? configuration = null)
    {
        // Like DetectFormat, a pure function of the bytes and of the configuration (never a test registry)
        var registry = (configuration ?? ImageConfiguration.Default).CodecRegistry ?? ImageCodecRegistry.Default;
        prefix = prefix[..Math.Min(prefix.Length, FormatDetectionPrefixLength)];
        return registry.Detect(prefix) switch
        {
            null => null,
            CustomImageCodecAdapter custom => custom.Codec.FormatInfo,
            var codec => codec.GetDetectedFormat(prefix) is var format and not ImageFormat.Unknown ? ImageFormatInfo.Get(format) : null,
        };
    }

    /// <summary>Gets a still copy of one frame of this image: the image <see cref="SaveFrame(ImageFrame, Stream, ImageEncoder)"/> gives a custom encoder.</summary>
    private Image CloneStillFrame(ImageFrame frame)
        => ReferenceEquals(frame, PosterCore) ? ClonePosterFrame() : CloneFrame(_frames.IndexOf(frame));

    private static void SaveCustom(Image image, string path, CustomImageEncoder encoder)
    {
        var output = AtomicFileOutput.Create(path, asynchronous: false);
        try
        {
            encoder.Encode(image, output.Stream);
            output.Publish();
        }
        finally
        {
            output.Abort();
        }
    }

    private static async Task SaveCustomAsync(Image image, string path, CustomImageEncoder encoder, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var output = AtomicFileOutput.Create(path, asynchronous: true);
        try
        {
            await encoder.EncodeAsync(image, output.Stream, cancellationToken).ConfigureAwait(false);
            await output.PublishAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await output.AbortAsync().ConfigureAwait(false);
        }
    }

    private static async Task SaveCustomAsync(Image image, Stream stream, CustomImageEncoder encoder, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await encoder.EncodeAsync(image, stream, cancellationToken).ConfigureAwait(false);
    }

    private async Task SaveCustomFrameAsync(ImageFrame frame, string? path, Stream? stream, CustomImageEncoder encoder, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var still = CloneStillFrame(frame);
        if (path is not null)
        {
            await SaveCustomAsync(still, path, encoder, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await SaveCustomAsync(still, stream!, encoder, cancellationToken).ConfigureAwait(false);
        }
    }
}
