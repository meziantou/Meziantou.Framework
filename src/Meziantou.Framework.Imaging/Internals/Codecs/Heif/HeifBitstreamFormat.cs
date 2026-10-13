namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The sample format and the color description that the bitstream of a coded image declares, whatever its codec: what the
/// container layer compares with the configuration record of the image, and what converts its planes to pixels.
/// </summary>
/// <param name="BitDepth">The number of bits of a sample.</param>
/// <param name="IsMonochrome">Whether the image has no chroma planes.</param>
/// <param name="SubsamplingX">The horizontal subsampling of the chroma planes: 0 or 1.</param>
/// <param name="SubsamplingY">The vertical subsampling of the chroma planes: 0 or 1.</param>
/// <param name="ChromaSamplePosition">The position of subsampled chroma samples, as an AV1 <c>chroma_sample_position</c> code point.</param>
/// <param name="MatrixCoefficients">The matrix coefficients code point of ITU-T H.273.</param>
/// <param name="FullRange">Whether the samples use the full range.</param>
/// <param name="ColorDescriptionPresent">Whether the bitstream signals its color primaries and transfer characteristics.</param>
/// <param name="ColorPrimaries">The color primaries code point of ITU-T H.273.</param>
/// <param name="TransferCharacteristics">The transfer characteristics code point of ITU-T H.273.</param>
internal readonly record struct HeifBitstreamFormat(
    int BitDepth,
    bool IsMonochrome,
    int SubsamplingX,
    int SubsamplingY,
    int ChromaSamplePosition,
    int MatrixCoefficients,
    bool FullRange,
    bool ColorDescriptionPresent,
    int ColorPrimaries,
    int TransferCharacteristics)
{
    /// <summary>
    /// Gets whether the bitstream states the range of its samples. Otherwise <see cref="FullRange"/> is the range that the
    /// codec infers, which applies to the samples of a color image and not to the ones of an alpha plane.
    /// </summary>
    public bool IsRangeSignaled { get; init; } = true;

    /// <summary>Gets the number of planes: 1 for a monochrome image, otherwise 3.</summary>
    public int PlaneCount => IsMonochrome ? 1 : 3;

    /// <summary>Returns whether the bitstream has the sample format that a configuration record declares.</summary>
    public bool HasSampleFormat(int bitDepth, bool isMonochrome, int subsamplingX, int subsamplingY)
        => BitDepth == bitDepth && IsMonochrome == isMonochrome && SubsamplingX == subsamplingX && SubsamplingY == subsamplingY;
}
