namespace Meziantou.Framework.Pdf;

/// <summary>The compression of a bilevel image (<see cref="PdfImage.FromBilevel"/>). Every compression is lossless.</summary>
public enum PdfBilevelCompression
{
    /// <summary>
    /// <c>CCITTFaxDecode</c> with Group 4 coding (ISO 32000-2, 7.4.6; ITU-T T.6): the compression of fax machines and
    /// scanners, which every PDF reader decodes.
    /// </summary>
    CcittGroup4,

    /// <summary>
    /// <c>JBIG2Decode</c> (ISO 32000-2, 7.4.7; ITU-T T.88), PDF 1.4: one generic region coded with the arithmetic coder.
    /// It is usually smaller than Group 4 coding, especially for halftones and dithered images. The symbol coding of
    /// text, which can be lossy, is not used.
    /// </summary>
    Jbig2,

    /// <summary><c>FlateDecode</c> of the rows of bits.</summary>
    Flate,
}
