using System.Runtime.InteropServices;
using Meziantou.Framework.Imaging.Formats;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// What <see cref="HeifEncoderSession"/> reads from the public encoder of a format of the HEIF family: the encoder codec of
/// the format maps its settings to these.
/// </summary>
/// <param name="EncoderName">The name of the public encoder type, for the messages that name one of its properties.</param>
/// <param name="Lossless">Whether the color images are coded without loss.</param>
/// <param name="Quality">The quality of lossy color images, 0 (smallest) to 100 (best).</param>
/// <param name="Effort">The effort of the search, 0 (fastest) to 9 (smallest).</param>
/// <param name="SubsamplingX">1 when the chroma planes of lossy color images have half the columns.</param>
/// <param name="SubsamplingY">1 when the chroma planes of lossy color images have half the rows.</param>
/// <param name="BitDepth">The number of bits of a sample, or <see langword="null"/> for the depth of the pixel format.</param>
/// <param name="MaxBitDepth">The largest number of bits of a sample that the format stores.</param>
/// <param name="AllowBitDepthReduction">Whether deeper pixel formats may be reduced to <paramref name="MaxBitDepth"/> bits.</param>
/// <param name="ClearTransparentColors">Whether the color of fully transparent pixels is replaced with transparent black.</param>
/// <param name="KeyFrameInterval">The number of images from a key frame to the next one in an image sequence; 0 when the first image is the only one.</param>
/// <param name="DurationRounding">How the durations of the images of a sequence are stored.</param>
/// <param name="MetadataHandling">What happens to the metadata that the format cannot store.</param>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct HeifEncoderSettings(
    string EncoderName,
    bool Lossless,
    int Quality,
    int Effort,
    int SubsamplingX,
    int SubsamplingY,
    int? BitDepth,
    int MaxBitDepth,
    bool AllowBitDepthReduction,
    bool ClearTransparentColors,
    int KeyFrameInterval,
    FrameDurationRounding DurationRounding,
    MetadataHandling MetadataHandling);
