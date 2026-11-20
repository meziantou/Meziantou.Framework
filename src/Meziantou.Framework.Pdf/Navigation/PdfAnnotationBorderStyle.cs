namespace Meziantou.Framework.Pdf;

/// <summary>The style of the border of an annotation (<c>/BS /S</c>, ISO 32000-2, 12.5.4, table 168).</summary>
public enum PdfAnnotationBorderStyle
{
    /// <summary>A solid rectangle (<c>S</c>).</summary>
    Solid,

    /// <summary>A dashed rectangle (<c>D</c>), with the dashes of <see cref="PdfAnnotationBorder.DashPattern"/>.</summary>
    Dashed,

    /// <summary>A rectangle that looks embossed above the page (<c>B</c>).</summary>
    Beveled,

    /// <summary>A rectangle that looks engraved in the page (<c>I</c>).</summary>
    Inset,

    /// <summary>A line at the bottom of the annotation (<c>U</c>).</summary>
    Underline,
}
