namespace Meziantou.Framework.VectorGraphics;

/// <summary>The direction a text is written in (<see cref="SceneText.Direction"/>), as the SVG <c>direction</c> property.</summary>
public enum SceneTextDirection
{
    /// <summary>
    /// From left to right: the text starts at its left end, and the characters of each span are ordered by the Unicode
    /// Bidirectional Algorithm with the direction of its first strong character. The default.
    /// </summary>
    LeftToRight,

    /// <summary>
    /// From right to left: the text starts at its right end, its spans follow each other to the left, and the
    /// characters of each span are ordered by the Unicode Bidirectional Algorithm with a right-to-left paragraph.
    /// </summary>
    RightToLeft,
}
