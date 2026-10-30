using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// What <see cref="HeifEncoderSession"/> asks of the codec for a coded stream: the color images of a file, or its alpha
/// planes.
/// </summary>
/// <param name="Width">The width of the pictures.</param>
/// <param name="Height">The height of the pictures.</param>
/// <param name="BitDepth">The number of bits of a sample.</param>
/// <param name="Monochrome">Whether the pictures have a luma plane only.</param>
/// <param name="SubsamplingX">1 when the chroma planes have half the columns.</param>
/// <param name="SubsamplingY">1 when the chroma planes have half the rows.</param>
/// <param name="ColorPrimaries">The color primaries code point of ITU-T H.273.</param>
/// <param name="TransferCharacteristics">The transfer characteristics code point of ITU-T H.273.</param>
/// <param name="MatrixCoefficients">The matrix coefficients code point of ITU-T H.273.</param>
/// <param name="FullRange">Whether the samples use the full range.</param>
/// <param name="StillPicture">Whether the stream is a single picture: a still image, not an image sequence.</param>
/// <param name="Lossless">Whether the decoded samples are the samples that were encoded.</param>
/// <param name="Quality">The quality of a lossy stream, 0 (smallest) to 100 (best).</param>
/// <param name="Effort">The effort of the search, 0 (fastest) to 9 (smallest).</param>
/// <param name="KeyFrameInterval">
/// The number of pictures from a key frame to the next one: 1 when every picture is a key frame, 0 when the first picture is
/// the only one.
/// </param>
/// <param name="MaxDegreeOfParallelism">
/// The largest number of workers that the codec may use for a picture (the independent tiles of an AV1 frame): 1 when
/// the pictures are coded at the same time as others. The output does not depend on it.
/// </param>
/// <param name="AutoTiling">
/// Whether the codec splits a picture into independent parts from its size (the AV1 tiles of <c>AvifEncoder.AutoTiling</c>),
/// so that workers can code and decode them at once; the fewest parts otherwise.
/// </param>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct HeifPictureSettings(
    int Width,
    int Height,
    int BitDepth,
    bool Monochrome,
    int SubsamplingX,
    int SubsamplingY,
    int ColorPrimaries,
    int TransferCharacteristics,
    int MatrixCoefficients,
    bool FullRange,
    bool StillPicture,
    bool Lossless,
    int Quality,
    int Effort,
    int KeyFrameInterval,
    int MaxDegreeOfParallelism = 1,
    bool AutoTiling = false);
