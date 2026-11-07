using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>What the caller of <see cref="Av1Encoder"/> chooses for a coded video sequence.</summary>
/// <param name="Width">The width of the frames, 1 to 65536.</param>
/// <param name="Height">The height of the frames, 1 to 65536.</param>
/// <param name="BitDepth">8, 10 or 12.</param>
/// <param name="Monochrome">Whether the frames have a luma plane only.</param>
/// <param name="SubsamplingX">1 when the chroma planes have half the columns.</param>
/// <param name="SubsamplingY">1 when the chroma planes have half the rows.</param>
/// <param name="ColorPrimaries">The <c>color_primaries</c> code point.</param>
/// <param name="TransferCharacteristics">The <c>transfer_characteristics</c> code point.</param>
/// <param name="MatrixCoefficients">The <c>matrix_coefficients</c> code point.</param>
/// <param name="FullRange">The value of <c>color_range</c>.</param>
/// <param name="ChromaSamplePosition">The <c>chroma_sample_position</c> of 4:2:0 samples.</param>
/// <param name="StillPicture">Whether the sequence is a single frame, written with a reduced still picture header.</param>
/// <param name="Lossless">Whether the decoded samples are the samples that were encoded.</param>
/// <param name="Quality">The quality of a lossy sequence, 0 (smallest) to 100 (best).</param>
/// <param name="Effort">The effort of the search, 0 (fastest) to 9 (smallest).</param>
/// <param name="KeyFrameInterval">
/// The number of frames from a key frame to the next one: 1 when every frame is a key frame, 0 when the first frame is the
/// only one. The other frames are predicted from the frame that precedes them.
/// </param>
/// <param name="MaxDegreeOfParallelism">
/// The largest number of workers that encode the tiles of a frame at once: the bytes are the same whatever the value.
/// </param>
/// <param name="MinTileColsLog2">
/// The smallest base 2 logarithm of the number of tile columns: the frame has the fewest tiles that the limits of the
/// specification allow otherwise. A measurement knob (the tile layout never depends on the number of workers).
/// </param>
/// <param name="MinTileRowsLog2">The smallest base 2 logarithm of the number of tile rows, like <paramref name="MinTileColsLog2"/>.</param>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct Av1EncoderSettings(
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
    int ChromaSamplePosition,
    bool StillPicture,
    bool Lossless,
    int Quality,
    int Effort,
    int KeyFrameInterval = 0,
    int MaxDegreeOfParallelism = 1,
    int MinTileColsLog2 = 0,
    int MinTileRowsLog2 = 0);
