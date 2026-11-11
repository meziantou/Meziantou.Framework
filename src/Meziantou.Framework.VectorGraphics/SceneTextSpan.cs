namespace Meziantou.Framework.VectorGraphics;

/// <summary>
/// A part of a text with its own style (<see cref="SceneText.Spans"/>): what it does not set is the style of the text.
/// The spans are drawn one after the other on the baseline of the text.
/// </summary>
/// <param name="text">The characters of the span.</param>
/// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
public sealed class SceneTextSpan(string text)
{
    /// <summary>Gets the characters of the span.</summary>
    public string Text { get; } = text ?? throw new ArgumentNullException(nameof(text));

    /// <summary>Gets the font of the span, or <see langword="null"/> for the font of the text.</summary>
    public SceneFont? Font { get; init; }

    /// <summary>Gets the color of the span, or <see langword="null"/> for the color of the text.</summary>
    public SceneColor? Color { get; init; }

    /// <summary>Gets the lines drawn with the span, or <see langword="null"/> for the lines of the text.</summary>
    public SceneTextDecorations? Decorations { get; init; }
}
