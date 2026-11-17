namespace Meziantou.Framework.Imaging.Metadata;

/// <summary>How the components of a DPX image element are packed into 32-bit words (SMPTE ST 268, Table 3B, field 21.10).</summary>
/// <remarks>
/// Only 10-bit and 12-bit components are filled: 1, 8, 16, 32 and 64-bit components never need it, and files store any of
/// the three values for them.
/// </remarks>
public enum DpxPacking
{
    /// <summary>Packed into 32-bit words: no padding bit between components, a component may span two words.</summary>
    Packed = 0,

    /// <summary>
    /// Filled to 32-bit words, method A (the standard one): the padding bits precede the data, three 10-bit components per
    /// 32-bit word with two padding bits at the least significant end, or one 12-bit component per 16-bit word with four.
    /// </summary>
    FilledMethodA = 1,

    /// <summary>
    /// Filled to 32-bit words, method B (no longer standard): the padding bits follow the data, at the most significant end
    /// of the 32-bit word (10 bits) or of the 16-bit word (12 bits).
    /// </summary>
    FilledMethodB = 2,
}
