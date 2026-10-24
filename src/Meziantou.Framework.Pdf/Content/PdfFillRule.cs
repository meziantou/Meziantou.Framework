namespace Meziantou.Framework.Pdf;

/// <summary>The rule that decides which points are inside a path that is filled or clipped (ISO 32000-2, 8.5.3.3).</summary>
public enum PdfFillRule
{
    /// <summary>The nonzero winding number rule.</summary>
    NonZero,

    /// <summary>The even-odd rule.</summary>
    EvenOdd,
}
