namespace Meziantou.Framework.Imaging.Formats;

/// <summary>Selects the coding process of JPEG output (ITU-T T.81).</summary>
public enum JpegEncodingProcess
{
    /// <summary>Baseline sequential DCT (SOF0): one scan, decoded top to bottom. This is the default.</summary>
    Baseline = 0,

    /// <summary>
    /// Progressive DCT (SOF2): the same coefficients as <see cref="Baseline"/>, sent in several scans (spectral selection and
    /// successive approximation) so that a decoder can show the whole image at increasing quality, with Huffman tables
    /// computed for each scan. The file is usually smaller; the coefficients of the whole image are kept in memory while it is
    /// written.
    /// </summary>
    Progressive = 1,
}
