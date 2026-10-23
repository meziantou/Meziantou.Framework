namespace Meziantou.Framework.Pdf;

/// <summary>The shape of the ends of open subpaths that are stroked (ISO 32000-2, 8.4.3.3).</summary>
public enum PdfLineCap
{
    /// <summary>The stroke is squared off at the endpoint.</summary>
    Butt = 0,

    /// <summary>A semicircular arc with a diameter equal to the line width is drawn around the endpoint.</summary>
    Round = 1,

    /// <summary>The stroke continues beyond the endpoint for half the line width and is squared off.</summary>
    ProjectingSquare = 2,
}
