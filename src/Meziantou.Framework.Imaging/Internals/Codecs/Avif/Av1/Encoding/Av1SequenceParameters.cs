using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// What the encoder decides for a whole coded video sequence: the values of the syntax elements of the sequence header that
/// are not constant in the streams it writes (AV1 specification, section 5.5).
/// </summary>
/// <param name="Width">The width of every frame, 1 to 65536.</param>
/// <param name="Height">The height of every frame, 1 to 65536.</param>
/// <param name="BitDepth">8, 10 or 12.</param>
/// <param name="Monochrome">Whether the frames have a luma plane only.</param>
/// <param name="SubsamplingX">1 when the chroma planes have half the columns (4:2:0 and 4:2:2).</param>
/// <param name="SubsamplingY">1 when the chroma planes have half the rows (4:2:0).</param>
/// <param name="ColorPrimaries">The <c>color_primaries</c> code point.</param>
/// <param name="TransferCharacteristics">The <c>transfer_characteristics</c> code point.</param>
/// <param name="MatrixCoefficients">The <c>matrix_coefficients</c> code point.</param>
/// <param name="FullRange">The value of <c>color_range</c>.</param>
/// <param name="ChromaSamplePosition">The <c>chroma_sample_position</c> of 4:2:0 samples.</param>
/// <param name="StillPicture">
/// Whether the sequence is one key frame written with <c>reduced_still_picture_header</c>; otherwise a sequence of frames
/// that may predict from one another.
/// </param>
/// <param name="Use128x128Superblock">The size of the superblocks.</param>
/// <param name="EnableCdef">Whether frames may use the constrained directional enhancement filter.</param>
/// <param name="EnableRestoration">Whether frames may use the loop restoration filters.</param>
/// <param name="LevelIndex">The value of <c>seq_level_idx</c>.</param>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct Av1SequenceParameters(
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
    bool Use128x128Superblock,
    bool EnableCdef,
    bool EnableRestoration,
    int LevelIndex)
{
    /// <summary>The number of bits of an order hint in the sequences that are not still pictures.</summary>
    public const int OrderHintBits = 7;

    /// <summary>
    /// Gets <c>seq_profile</c> (section 6.4.1): Main holds 8-bit and 10-bit 4:2:0 and monochrome samples, High adds 4:4:4,
    /// Professional adds 4:2:2 and every 12-bit format.
    /// </summary>
    public int Profile
    {
        get
        {
            if (BitDepth is 12)
                return 2;

            if (Monochrome)
                return 0;

            if (SubsamplingX is 0)
                return 1;

            return SubsamplingY is 1 ? 0 : 2;
        }
    }
}
