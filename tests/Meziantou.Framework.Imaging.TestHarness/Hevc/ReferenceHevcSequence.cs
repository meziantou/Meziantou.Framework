namespace Meziantou.Framework.Imaging.TestHarness.Hevc;

/// <summary>The sample arrays of the pictures of an HEVC sequence, as its sequence parameter set describes them.</summary>
/// <param name="Width"><c>pic_width_in_luma_samples</c>: the width of the decoded luma sample array.</param>
/// <param name="Height"><c>pic_height_in_luma_samples</c>.</param>
/// <param name="ChromaFormatIdc"><c>chroma_format_idc</c>: 0 for monochrome, 1 for 4:2:0, 2 for 4:2:2, 3 for 4:4:4.</param>
/// <param name="BitDepthLuma"><c>BitDepthY</c>.</param>
/// <param name="BitDepthChroma"><c>BitDepthC</c>.</param>
/// <param name="CropLeft">The horizontal position of the conformance cropping window, in luma samples.</param>
/// <param name="CropTop">The vertical position of the conformance cropping window, in luma samples.</param>
/// <param name="CropWidth">The width of the conformance cropping window, the pictures that a decoder outputs, in luma samples.</param>
/// <param name="CropHeight">The height of the conformance cropping window, in luma samples.</param>
public sealed record ReferenceHevcSequence(int Width, int Height, int ChromaFormatIdc, int BitDepthLuma, int BitDepthChroma, int CropLeft, int CropTop, int CropWidth, int CropHeight)
{
    /// <summary>Gets <c>SubWidthC</c> (Table 6-1): 1 for a monochrome sequence.</summary>
    public int SubWidthC => ChromaFormatIdc is 1 or 2 ? 2 : 1;

    /// <summary>Gets <c>SubHeightC</c> (Table 6-1): 1 for a monochrome sequence.</summary>
    public int SubHeightC => ChromaFormatIdc is 1 ? 2 : 1;

    /// <summary>Gets the number of sample arrays of a picture.</summary>
    public int ComponentCount => ChromaFormatIdc is 0 ? 1 : 3;
}
