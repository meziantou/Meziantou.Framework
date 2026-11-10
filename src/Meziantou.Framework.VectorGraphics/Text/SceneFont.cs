using System.Runtime.InteropServices;

namespace Meziantou.Framework.VectorGraphics;

/// <summary>The size, the weight and the style of a text. The family is the one of the drawing (<see cref="Scene.FontFamily"/>).</summary>
/// <param name="Size">The size of the font, in pixels.</param>
/// <param name="Bold">Whether the text is bold.</param>
/// <param name="Italic">Whether the text is italic.</param>
[StructLayout(LayoutKind.Auto)]
public readonly record struct SceneFont(double Size, bool Bold = false, bool Italic = false);
