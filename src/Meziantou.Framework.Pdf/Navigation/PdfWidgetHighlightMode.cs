namespace Meziantou.Framework.Pdf;

/// <summary>How a widget annotation looks while the mouse button is pressed on it (<c>/H</c>, ISO 32000-2, 12.5.6.19, table 191).</summary>
public enum PdfWidgetHighlightMode
{
    /// <summary>No highlighting (<c>N</c>).</summary>
    None,

    /// <summary>The colors of the content of the annotation are inverted (<c>I</c>), the default.</summary>
    Invert,

    /// <summary>The colors of the border of the annotation are inverted (<c>O</c>).</summary>
    Outline,

    /// <summary>The down appearance of the annotation is shown (<c>P</c>).</summary>
    Push,

    /// <summary>The same as <see cref="Push"/> (<c>T</c>).</summary>
    Toggle,
}
