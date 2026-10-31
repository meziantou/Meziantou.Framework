using Meziantou.Framework.Imaging.Formats;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The HEIC encoder registration: still images and image sequences, coded by the HEVC encoder of the library
/// (<see cref="HeicBinding"/>) and written by the encoding session of the HEIF container layer.
/// </summary>
internal sealed class HeicEncoderCodec : ImageEncoderCodec
{
    // The encoder codes 8, 10 or 12 bits per sample
    private const int MaxBitDepth = 12;

    public static HeicEncoderCodec Instance { get; } = new();

    public override ImageFormat Format => ImageFormat.Heic;

    public override ImageEncoderSession CreateSession(ImageEncoderSessionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var encoder = (HeicEncoder)options.Encoder;
        var settings = new HeifEncoderSettings(
            nameof(HeicEncoder),
            Lossless: encoder.Compression is HeicCompression.Lossless,
            encoder.Quality,
            encoder.Effort,
            SubsamplingX: encoder.ChromaSubsampling is HeicChromaSubsampling.Ratio444 ? 0 : 1,
            SubsamplingY: encoder.ChromaSubsampling is HeicChromaSubsampling.Auto or HeicChromaSubsampling.Ratio420 ? 1 : 0,
            encoder.BitDepth,
            MaxBitDepth,
            encoder.AllowBitDepthReduction,
            encoder.ClearTransparentColors,
            encoder.KeyFrameInterval,
            encoder.DurationRounding,
            encoder.MetadataHandling,
            GridTileSize: encoder.GridTileSize,
            OptimizeQuantization: encoder.OptimizeQuantization);

        return new HeifEncoderSession(options, HeicBinding.Instance, settings);
    }
}
