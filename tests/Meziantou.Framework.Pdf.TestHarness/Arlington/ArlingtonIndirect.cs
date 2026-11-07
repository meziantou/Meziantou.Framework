namespace Meziantou.Framework.Pdf.TestHarness.Arlington;

/// <summary>Whether a value must be an indirect reference (the <c>IndirectReference</c> column).</summary>
public enum ArlingtonIndirect
{
    /// <summary>Direct or indirect.</summary>
    Any,

    /// <summary>The value must be an indirect reference.</summary>
    MustBeIndirect,

    /// <summary>The value must be a direct object.</summary>
    MustBeDirect,
}
