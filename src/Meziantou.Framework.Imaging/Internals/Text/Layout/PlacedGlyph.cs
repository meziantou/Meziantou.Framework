using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>A glyph on a line of a layout that is being created, in pixels from the start of the text of its line.</summary>
[StructLayout(LayoutKind.Auto)]
internal struct PlacedGlyph
{
    /// <summary>The identifier of the glyph; -1 for a tabulation, which only takes space.</summary>
    public int Glyph;

    /// <summary>The index in the text of the first character of the cluster of the glyph.</summary>
    public int Cluster;

    /// <summary>Where the pen is before the glyph.</summary>
    public double PenX;

    /// <summary>The horizontal distance from the pen to the origin of the glyph.</summary>
    public double XOffset;

    /// <summary>The vertical distance from the baseline up to the origin of the glyph.</summary>
    public double YOffset;

    /// <summary>How far the pen moves after the glyph.</summary>
    public double Advance;

    /// <summary>The font of the glyph, in the list of the fonts of the layout.</summary>
    public int Font;

    public double Size;

    /// <summary>The span of the text the cluster of the glyph belongs to.</summary>
    public int Span;

    /// <summary>The glyph draws nothing: a space, the glyph of a character without visible form.</summary>
    public bool IsInvisible;

    /// <summary>The glyph is a space between words that is not at the end of its line: justification widens it.</summary>
    public bool IsExpandable;

    /// <summary>The glyph is white space that hangs at the end of a wrapped line: it is not decorated.</summary>
    public bool IsHanging;

    /// <summary>The text the glyph belongs to goes from right to left: its first character is on its right.</summary>
    public bool IsRightToLeft;
}
