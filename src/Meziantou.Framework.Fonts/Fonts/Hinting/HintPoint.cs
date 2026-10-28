using System.Runtime.InteropServices;

namespace Meziantou.Framework.Fonts;

/// <summary>A point of a zone of the TrueType interpreter: where it is, where it was before the instructions, and where the font puts it.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct HintPoint
{
    /// <summary>The point is on the curve.</summary>
    public const int OnCurve = 1;

    /// <summary>An instruction moved the point along the x axis.</summary>
    public const int TouchedX = 2;

    /// <summary>An instruction moved the point along the y axis.</summary>
    public const int TouchedY = 4;

    /// <summary>The current position, in 26.6 pixels.</summary>
    public int X;

    /// <summary>The current position, in 26.6 pixels.</summary>
    public int Y;

    /// <summary>The position before the instructions moved the point, in 26.6 pixels.</summary>
    public int OriginalX;

    /// <summary>The position before the instructions moved the point, in 26.6 pixels.</summary>
    public int OriginalY;

    /// <summary>The position in the font, in font units.</summary>
    public int UnscaledX;

    /// <summary>The position in the font, in font units.</summary>
    public int UnscaledY;

    public int Flags;
}
