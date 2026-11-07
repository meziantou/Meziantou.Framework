using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>What the caller of the HEVC encoder chooses for a coded video sequence.</summary>
/// <param name="Width">The width of the pictures, in luma samples.</param>
/// <param name="Height">The height of the pictures, in luma samples.</param>
/// <param name="BitDepth">The bit depth of the luma and chroma samples: 8, 10 or 12.</param>
/// <param name="ChromaFormatIdc"><c>chroma_format_idc</c>: 0 for monochrome, 1 for 4:2:0, 2 for 4:2:2, 3 for 4:4:4.</param>
/// <param name="ColourPrimaries">The <c>colour_primaries</c> code point (Rec. ITU-T H.273).</param>
/// <param name="TransferCharacteristics">The <c>transfer_characteristics</c> code point.</param>
/// <param name="MatrixCoeffs">The <c>matrix_coeffs</c> code point.</param>
/// <param name="FullRange">The value of <c>video_full_range_flag</c>.</param>
/// <param name="ChromaSampleLocType">The location of the chroma samples of 4:2:0 pictures (<c>chroma_sample_loc_type_top_field</c>, Figure E.1): 0 to 5.</param>
/// <param name="StillPicture">Whether the sequence is a single picture.</param>
/// <param name="Lossless">Whether the decoded samples are the samples that were encoded.</param>
/// <param name="Quality">The quality of a lossy sequence, 0 (smallest) to 100 (best).</param>
/// <param name="Effort">The effort of the search, 0 (fastest) to 9 (smallest).</param>
/// <param name="KeyFrameInterval">
/// The number of pictures from a key frame to the next one: 1 when every picture is a key frame, 0 when the first picture
/// is the only one. The other pictures are predicted from the picture that precedes them.
/// </param>
/// <param name="Wavefront">
/// Whether each row of coding tree blocks is a substream of the wavefront (<c>entropy_coding_sync_enabled_flag</c>), with
/// an entry point, which decoders can decode concurrently. The coding decisions are the same as without wavefront, apart
/// from the rates, which are those of the context variables of the wavefront.
/// </param>
/// <param name="OptimizeQuantization">Whether lossy transform blocks search quantization rounding offsets using rate and distortion.</param>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct HevcEncoderSettings(
    int Width,
    int Height,
    int BitDepth,
    int ChromaFormatIdc,
    int ColourPrimaries,
    int TransferCharacteristics,
    int MatrixCoeffs,
    bool FullRange,
    int ChromaSampleLocType,
    bool StillPicture,
    bool Lossless,
    int Quality,
    int Effort,
    int KeyFrameInterval = 0,
    bool Wavefront = false,
    bool OptimizeQuantization = false);
