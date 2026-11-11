using System.Runtime.InteropServices;

namespace Meziantou.Framework.VectorGraphics;

/// <summary>The size, the weight, the style and the family of a text.</summary>
/// <param name="Size">The size of the font, in pixels.</param>
/// <param name="Bold">Whether the text is bold.</param>
/// <param name="Italic">Whether the text is italic.</param>
/// <param name="Family">
/// The family of the text, as a CSS <c>font-family</c> value, or <see langword="null"/> for the family of the drawing
/// (<see cref="Scene.FontFamily"/>). With fonts (<see cref="Scene.Fonts"/>), the first family of the list that the
/// collection has draws the text, and the default family of the collection when it has none of them.
/// </param>
[StructLayout(LayoutKind.Auto)]
public readonly record struct SceneFont(double Size, bool Bold = false, bool Italic = false, string? Family = null);
