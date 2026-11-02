namespace Meziantou.Framework.Pdf;

/// <summary>The kind of a text markup annotation (ISO 32000-2, 12.5.6.10).</summary>
public enum PdfTextMarkupKind
{
    /// <summary>The text is highlighted, as with a marker (<c>Highlight</c>).</summary>
    Highlight,

    /// <summary>The text is underlined (<c>Underline</c>).</summary>
    Underline,

    /// <summary>The text is underlined with a wavy line (<c>Squiggly</c>).</summary>
    Squiggly,

    /// <summary>The text is struck out (<c>StrikeOut</c>).</summary>
    StrikeOut,
}
