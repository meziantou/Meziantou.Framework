namespace Meziantou.Framework.Pdf.Rendering;

/// <summary>The purpose of a rendering, which selects the annotations and the optional content drawn (ISO 32000-2, 8.11.4.4 and 12.5.3).</summary>
public enum PdfRenderIntent
{
    /// <summary>The page is shown on a screen: the annotations without the NoView flag are drawn, and the optional content follows its View usage.</summary>
    View,

    /// <summary>The page is printed: the annotations with the Print flag are drawn, and the optional content follows its Print usage.</summary>
    Print,

    /// <summary>The page is exported to another format: the annotations are drawn as for <see cref="View"/>, and the optional content follows its Export usage.</summary>
    Export,
}
