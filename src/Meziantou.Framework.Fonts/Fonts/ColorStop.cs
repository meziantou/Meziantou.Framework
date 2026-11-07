using System.Runtime.InteropServices;

namespace Meziantou.Framework.Fonts;

/// <summary>A color of a paint of a color glyph: a stop of a color line, or the color of a solid paint.</summary>
/// <param name="Offset">The position of the stop on its color line, from 0 to 1 once the line is normalized.</param>
/// <param name="Red">The red component, from 0 to 1, not multiplied by the alpha.</param>
/// <param name="Green">The green component.</param>
/// <param name="Blue">The blue component.</param>
/// <param name="Alpha">The opacity, from 0 to 1.</param>
/// <param name="IsForeground">Whether the color is the one of the text: its components are then those of the brush the text is drawn with, and <paramref name="Alpha"/> multiplies its opacity.</param>
[StructLayout(LayoutKind.Auto)]
public readonly record struct ColorStop(double Offset, double Red, double Green, double Blue, double Alpha, bool IsForeground);
