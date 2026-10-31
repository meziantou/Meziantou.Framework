namespace Meziantou.Framework.Imaging.Formats;

/// <summary>The coder of the code-blocks of a JPEG 2000 output.</summary>
public enum Jpeg2000BlockCoder
{
    /// <summary>
    /// The block coder of Rec. ITU-T T.800 | ISO/IEC 15444-1 (EBCOT): three coding passes per bit-plane with an arithmetic
    /// coder, which every JPEG 2000 reader reads, and whose passes the quality layers can split.
    /// </summary>
    Standard = 0,

    /// <summary>
    /// The high-throughput block coder of Rec. ITU-T T.814 | ISO/IEC 15444-15 (HTJ2K): one HT cleanup pass per code-block,
    /// which codes every bit-plane at once with variable-length codes. On photographs, a lossless output is coded about 4
    /// times and decoded about 6 times faster than with <see cref="Standard"/>, for about 8% more bytes. A reader needs to
    /// support Part 15: a file is a JPH file (<c>.jph</c>), a codestream a <c>.jhc</c> one.
    /// </summary>
    HighThroughput = 1,
}
