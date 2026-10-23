namespace Meziantou.Framework.Pdf;

/// <summary>How a destination shows its page (ISO 32000-2, 12.3.2.2, table 149).</summary>
public enum PdfDestinationKind
{
    /// <summary>The point (<see cref="PdfDestination.Left"/>, <see cref="PdfDestination.Top"/>) at the upper-left corner of the window, with a zoom factor (<c>XYZ</c>).</summary>
    Xyz,

    /// <summary>The whole page fits the window (<c>Fit</c>).</summary>
    Fit,

    /// <summary>The width of the page fits the window, <see cref="PdfDestination.Top"/> at the top (<c>FitH</c>).</summary>
    FitHorizontally,

    /// <summary>The height of the page fits the window, <see cref="PdfDestination.Left"/> at the left (<c>FitV</c>).</summary>
    FitVertically,

    /// <summary>A rectangle fits the window (<c>FitR</c>).</summary>
    FitRectangle,

    /// <summary>The bounding box of the content fits the window (<c>FitB</c>).</summary>
    FitBoundingBox,

    /// <summary>The width of the bounding box fits the window (<c>FitBH</c>).</summary>
    FitBoundingBoxHorizontally,

    /// <summary>The height of the bounding box fits the window (<c>FitBV</c>).</summary>
    FitBoundingBoxVertically,
}
