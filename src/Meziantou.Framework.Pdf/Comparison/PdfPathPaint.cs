namespace Meziantou.Framework.Pdf;

/// <summary>How a path is painted (ISO 32000-2, 8.5.3).</summary>
public enum PdfPathPaint
{
    /// <summary>The path is filled.</summary>
    Fill,

    /// <summary>The path is stroked.</summary>
    Stroke,

    /// <summary>The path is filled, then stroked.</summary>
    FillAndStroke,
}
