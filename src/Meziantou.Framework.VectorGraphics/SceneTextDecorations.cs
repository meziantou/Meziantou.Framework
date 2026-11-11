namespace Meziantou.Framework.VectorGraphics;

/// <summary>The lines drawn with a text (<see cref="SceneText.Decorations"/>).</summary>
[Flags]
public enum SceneTextDecorations
{
    /// <summary>No line.</summary>
    None = 0,

    /// <summary>A line under the text.</summary>
    Underline = 1,

    /// <summary>A line through the middle of the text.</summary>
    Strikethrough = 2,

    /// <summary>A line over the text, at the ascent of its font.</summary>
    Overline = 4,
}
