namespace Meziantou.Framework.Pdf;

/// <summary>The shape of the corners of paths that are stroked (ISO 32000-2, 8.4.3.4).</summary>
public enum PdfLineJoin
{
    /// <summary>The outer edges of the two segments are extended until they meet, within the miter limit.</summary>
    Miter = 0,

    /// <summary>An arc of a circle with a diameter equal to the line width is drawn around the corner.</summary>
    Round = 1,

    /// <summary>The two segments are finished with butt caps and the notch is filled with a triangle.</summary>
    Bevel = 2,
}
