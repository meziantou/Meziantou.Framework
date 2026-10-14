namespace Meziantou.Framework.Imaging.Typography;

/// <summary>The direction of the lines of a text, and the side its lines follow each other to.</summary>
/// <remarks>See <see cref="TextLayoutOptions.WritingMode"/>.</remarks>
public enum TextWritingMode
{
    /// <summary>Lines are horizontal and follow each other from the top to the bottom. This is the default.</summary>
    Horizontal = 0,

    /// <summary>
    /// Lines are vertical, from the top to the bottom, and follow each other from the right to the left: Chinese,
    /// Japanese and Korean set vertically.
    /// </summary>
    VerticalRightToLeft = 1,

    /// <summary>Lines are vertical, from the top to the bottom, and follow each other from the left to the right: Mongolian.</summary>
    VerticalLeftToRight = 2,
}
