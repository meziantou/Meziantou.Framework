namespace Meziantou.Framework.Pdf;

/// <summary>The ending of a line or of a polyline (<c>/LE</c>, ISO 32000-2, 12.5.6.7, table 179).</summary>
public enum PdfLineEnding
{
    /// <summary>No ending (<c>None</c>).</summary>
    None,

    /// <summary>A square, filled with the interior color (<c>Square</c>).</summary>
    Square,

    /// <summary>A circle, filled with the interior color (<c>Circle</c>).</summary>
    Circle,

    /// <summary>A diamond, filled with the interior color (<c>Diamond</c>).</summary>
    Diamond,

    /// <summary>Two short lines that form an arrowhead (<c>OpenArrow</c>).</summary>
    OpenArrow,

    /// <summary>A triangular arrowhead, filled with the interior color (<c>ClosedArrow</c>).</summary>
    ClosedArrow,

    /// <summary>A short line perpendicular to the line (<c>Butt</c>).</summary>
    Butt,

    /// <summary>Two short lines that form an arrowhead pointing back along the line (<c>ROpenArrow</c>).</summary>
    ReversedOpenArrow,

    /// <summary>A triangular arrowhead pointing back along the line, filled with the interior color (<c>RClosedArrow</c>).</summary>
    ReversedClosedArrow,

    /// <summary>A short line at 30 degrees clockwise from the perpendicular to the line (<c>Slash</c>).</summary>
    Slash,
}
