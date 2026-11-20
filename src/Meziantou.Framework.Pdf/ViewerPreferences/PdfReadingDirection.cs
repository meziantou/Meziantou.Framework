namespace Meziantou.Framework.Pdf;

/// <summary>The reading order of the text, which sets how viewers lay out pages side by side (<c>/Direction</c>, ISO 32000-2, 12.2, table 147).</summary>
public enum PdfReadingDirection
{
    /// <summary>Left to right (<c>L2R</c>), the default.</summary>
    LeftToRight,

    /// <summary>Right to left, for vertical writing systems and languages such as Arabic and Hebrew (<c>R2L</c>).</summary>
    RightToLeft,
}
