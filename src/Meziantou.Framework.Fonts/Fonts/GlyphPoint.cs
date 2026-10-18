using System.Runtime.InteropServices;

namespace Meziantou.Framework.Fonts;

/// <summary>A point of a TrueType outline being assembled.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct GlyphPoint
{
    /// <summary>The point is on the curve; otherwise it is the control point of a quadratic curve.</summary>
    public const int OnCurve = 1;

    /// <summary>The point is the last one of its contour.</summary>
    public const int EndOfContour = 2;

    public double X;
    public double Y;
    public int Flags;
}
