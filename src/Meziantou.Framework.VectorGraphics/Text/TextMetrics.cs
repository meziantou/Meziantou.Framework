using System.Runtime.InteropServices;

namespace Meziantou.Framework.VectorGraphics;

/// <summary>The dimensions of a line of text.</summary>
/// <param name="Width">The advance width of the text.</param>
/// <param name="Ascent">The distance from the baseline to the top of the line, positive.</param>
/// <param name="Descent">The distance from the baseline to the bottom of the line, positive.</param>
[StructLayout(LayoutKind.Auto)]
public readonly record struct TextMetrics(double Width, double Ascent, double Descent)
{
    /// <summary>Gets the height of the line, <see cref="Ascent"/> plus <see cref="Descent"/>.</summary>
    public double Height => Ascent + Descent;
}
