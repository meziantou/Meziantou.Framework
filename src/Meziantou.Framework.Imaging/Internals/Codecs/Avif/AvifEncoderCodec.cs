using Meziantou.Framework.Imaging.Formats;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The AVIF encoder registration: still images and image sequences, coded by the AV1 encoder of the library
/// (<see cref="AvifBinding"/>) and written by the encoding session of the HEIF container layer.
/// </summary>
internal sealed class AvifEncoderCodec : ImageEncoderCodec
{
    // AV1 codes 8, 10 or 12 bits per sample
    private const int MaxBitDepth = 12;

    public static AvifEncoderCodec Instance { get; } = new();

    public override ImageFormat Format => ImageFormat.Avif;

    public override ImageEncoderSession CreateSession(ImageEncoderSessionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var encoder = (AvifEncoder)options.Encoder;
        var settings = new HeifEncoderSettings(
            nameof(AvifEncoder),
            Lossless: encoder.Compression is AvifCompression.Lossless,
            encoder.Quality,
            encoder.Effort,
            SubsamplingX: encoder.ChromaSubsampling is AvifChromaSubsampling.Ratio444 ? 0 : 1,
            SubsamplingY: encoder.ChromaSubsampling is AvifChromaSubsampling.Auto or AvifChromaSubsampling.Ratio420 ? 1 : 0,
            encoder.BitDepth,
            MaxBitDepth,
            encoder.AllowBitDepthReduction,
            encoder.ClearTransparentColors,
            encoder.KeyFrameInterval,
            encoder.DurationRounding,
            encoder.MetadataHandling,
            encoder.CleanAperture,
            encoder.PremultiplyAlpha,
            encoder.GridTileSize);

        return new HeifEncoderSession(options, AvifBinding.Instance, settings);
    }
}
