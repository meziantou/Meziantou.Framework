namespace Meziantou.Framework.Imaging.Formats;

/// <summary>Selects the entropy coding of JPEG output (ITU-T T.81).</summary>
public enum JpegEntropyCoding
{
    /// <summary>Huffman coding, which every JPEG decoder reads. This is the default.</summary>
    Huffman = 0,

    /// <summary>
    /// Arithmetic coding (T.81 Annex D): the same coefficients or samples as Huffman coding, coded with adaptive
    /// probability estimates, so the file is usually smaller and decodes to the same pixels. Some decoders (most web
    /// browsers among them) do not read it.
    /// </summary>
    Arithmetic = 1,
}
