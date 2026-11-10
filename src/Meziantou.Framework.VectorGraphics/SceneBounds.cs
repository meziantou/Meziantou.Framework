using System.Runtime.InteropServices;

namespace Meziantou.Framework.VectorGraphics;

/// <summary>A rectangle.</summary>
/// <param name="X">The left edge.</param>
/// <param name="Y">The top edge.</param>
/// <param name="Width">The width.</param>
/// <param name="Height">The height.</param>
[StructLayout(LayoutKind.Auto)]
public readonly record struct SceneBounds(double X, double Y, double Width, double Height)
{
    /// <summary>Gets the right edge.</summary>
    public double Right => X + Width;

    /// <summary>Gets the bottom edge.</summary>
    public double Bottom => Y + Height;

    /// <summary>Gets the x coordinate of the center.</summary>
    public double CenterX => X + (Width / 2);

    /// <summary>Gets the y coordinate of the center.</summary>
    public double CenterY => Y + (Height / 2);
}
