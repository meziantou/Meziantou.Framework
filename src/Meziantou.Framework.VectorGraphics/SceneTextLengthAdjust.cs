namespace Meziantou.Framework.VectorGraphics;

/// <summary>How a text is stretched or compressed to its length (<see cref="SceneText.TextLength"/>), as the SVG <c>lengthAdjust</c> attribute.</summary>
public enum SceneTextLengthAdjust
{
    /// <summary>The space between the characters changes; the glyphs keep their shape (<c>spacing</c>).</summary>
    Spacing,

    /// <summary>The glyphs are scaled horizontally, with the space between them (<c>spacingAndGlyphs</c>).</summary>
    SpacingAndGlyphs,
}
