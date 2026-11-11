using System.Runtime.InteropServices;

namespace Meziantou.Framework.VectorGraphics;

/// <summary>The size, the weight, the style and the family of a text.</summary>
/// <param name="Size">The size of the font, in pixels.</param>
/// <param name="Weight">
/// The weight of the font, from 1 to 1000, as in CSS: <see cref="NormalWeight"/> (400) for regular text, <see cref="BoldWeight"/>
/// (700) for bold text. The fonts of a scene draw the text with the face of the nearest weight; the renderers without
/// fonts draw the weights from 600 as bold.
/// </param>
/// <param name="Italic">Whether the text is italic.</param>
/// <param name="Family">
/// The family of the text, as a CSS <c>font-family</c> value, or <see langword="null"/> for the family of the drawing
/// (<see cref="Scene.FontFamily"/>). With fonts (<see cref="Scene.Fonts"/>), the first family of the list that the
/// collection has draws the text, and the default family of the collection when it has none of them.
/// </param>
[StructLayout(LayoutKind.Auto)]
public readonly record struct SceneFont(double Size, int Weight = SceneFont.NormalWeight, bool Italic = false, string? Family = null)
{
    /// <summary>The weight of regular text.</summary>
    public const int NormalWeight = 400;

    /// <summary>The weight of bold text.</summary>
    public const int BoldWeight = 700;

    /// <summary>Gets a value that indicates whether the weight is bold: 600 or more, as CSS chooses between a regular and a bold face.</summary>
    public bool IsBold => Weight >= 600;

    /// <summary>Gets the weight clamped to the range of CSS, from 1 to 1000.</summary>
    internal int ClampedWeight => Math.Clamp(Weight, 1, 1000);
}
