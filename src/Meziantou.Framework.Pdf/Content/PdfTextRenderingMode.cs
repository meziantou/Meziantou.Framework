namespace Meziantou.Framework.Pdf;

/// <summary>How the glyphs of the text are painted (ISO 32000-2, 9.3.6).</summary>
public enum PdfTextRenderingMode
{
    /// <summary>Fill the glyphs.</summary>
    Fill = 0,

    /// <summary>Stroke the glyphs.</summary>
    Stroke = 1,

    /// <summary>Fill, then stroke the glyphs.</summary>
    FillStroke = 2,

    /// <summary>Neither fill nor stroke the glyphs: the text is invisible (the text of a scanned document).</summary>
    Invisible = 3,

    /// <summary>Fill the glyphs and add them to the clipping path.</summary>
    FillClip = 4,

    /// <summary>Stroke the glyphs and add them to the clipping path.</summary>
    StrokeClip = 5,

    /// <summary>Fill, then stroke the glyphs and add them to the clipping path.</summary>
    FillStrokeClip = 6,

    /// <summary>Add the glyphs to the clipping path.</summary>
    Clip = 7,
}
