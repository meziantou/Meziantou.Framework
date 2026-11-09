namespace Meziantou.Framework.Imaging.Formats;

/// <summary>Selects the coding process of JPEG output (ITU-T T.81).</summary>
public enum JpegEncodingProcess
{
    /// <summary>
    /// Sequential DCT: one scan, decoded top to bottom. This is the default. 8-bit Huffman-coded output is a baseline frame
    /// (SOF0); 12-bit samples make it an extended sequential one (SOF1), and arithmetic coding an SOF9 frame.
    /// </summary>
    Baseline = 0,

    /// <summary>
    /// Progressive DCT (SOF2): the same coefficients as <see cref="Baseline"/>, sent in several scans (spectral selection and
    /// successive approximation) so that a decoder can show the whole image at increasing quality, with Huffman tables
    /// computed for each scan. The file is usually smaller; the coefficients of the whole image are kept in memory while it is
    /// written.
    /// </summary>
    Progressive = 1,

    /// <summary>
    /// Lossless (SOF3, or SOF11 with arithmetic coding; T.81 Annex H): every sample is predicted from its decoded
    /// neighbors (<see cref="JpegEncoder.LosslessPredictor"/>) and the difference is coded, so the decoded samples are
    /// exactly the encoded ones. Color pixel formats are written as RGB components without a color transform or chroma
    /// subsampling. The file is several times larger than a lossy one, and some decoders (most web browsers among them) do
    /// not read it.
    /// </summary>
    Lossless = 2,
}
