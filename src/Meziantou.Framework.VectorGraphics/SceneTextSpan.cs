namespace Meziantou.Framework.VectorGraphics;

/// <summary>
/// A part of a text with its own style (<see cref="SceneText.Spans"/>): what it does not set is the style of the text.
/// The spans are drawn one after the other on the baseline of the text, unless they are moved (<see cref="X"/>,
/// <see cref="Y"/>, <see cref="Dx"/>, <see cref="Dy"/>, <see cref="BaselineShift"/>).
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

    /// <summary>Gets the space added after each character of the span, or <see langword="null"/> for the spacing of the text (<see cref="SceneText.LetterSpacing"/>).</summary>
    public double? LetterSpacing { get; init; }

    /// <summary>Gets the space added to each space of the span, or <see langword="null"/> for the spacing of the text (<see cref="SceneText.WordSpacing"/>).</summary>
    public double? WordSpacing { get; init; }

    /// <summary>
    /// Gets how the glyphs of the span are painted, or <see langword="null"/> for <see cref="Color"/> when it is set, or
    /// else for the fill of the text (<see cref="SceneText.Fill"/>, then <see cref="SceneText.Color"/>).
    /// </summary>
    public ScenePaint? Fill { get; init; }

    /// <summary>Gets how the outline of the glyphs of the span is painted, or <see langword="null"/> for the outline of the text (<see cref="SceneText.Stroke"/>).</summary>
    /// <remarks>The width and the dashes of the outline are the ones of the text.</remarks>
    public ScenePaint? Stroke { get; init; }

    /// <summary>Gets how the lines of the span are painted, or <see langword="null"/> for the paint of the lines of the text (<see cref="SceneText.DecorationFill"/>), and else the paint of the glyphs of the span.</summary>
    public ScenePaint? DecorationFill { get; init; }

    /// <summary>
    /// Gets how far the baseline of the span is raised above the baseline of the text, in pixels, as the SVG
    /// <c>baseline-shift</c> property: positive for a superscript, negative for a subscript. The default is 0; a value
    /// that is not finite is 0.
    /// </summary>
    /// <remarks>The shift applies to the span only: the next span is back on the baseline of the text.</remarks>
    public double BaselineShift { get; init; }

    /// <summary>
    /// Gets the x coordinate the span starts at, as the <c>x</c> attribute of an SVG <c>tspan</c>, or
    /// <see langword="null"/> (the default) to start where the previous span ends.
    /// </summary>
    /// <remarks>
    /// A span with <see cref="X"/> or <see cref="Y"/> starts a new text chunk: the anchor of the text
    /// (<see cref="SceneText.Anchor"/>) applies to each chunk, from its first character. The spans that follow continue
    /// from the end of the span. On a path (<see cref="SceneText.Path"/>), <see cref="X"/> and <see cref="Y"/> are ignored.
    /// A value that is not finite is ignored.
    /// </remarks>
    public double? X { get; init; }

    /// <summary>
    /// Gets the y coordinate of the baseline of the span and of the spans that follow it, as the <c>y</c> attribute of an
    /// SVG <c>tspan</c>, or <see langword="null"/> (the default) for the baseline of the previous span.
    /// </summary>
    /// <remarks>See <see cref="X"/>: a span with <see cref="Y"/> starts a new text chunk.</remarks>
    public double? Y { get; init; }

    /// <summary>
    /// Gets the horizontal shift of the span, in pixels, as the <c>dx</c> attribute of an SVG <c>tspan</c>: the span and
    /// the spans that follow it move by this distance. On a path, the shift is along the path. The default is 0; a value
    /// that is not finite is 0.
    /// </summary>
    public double Dx { get; init; }

    /// <summary>
    /// Gets the vertical shift of the span, in pixels, as the <c>dy</c> attribute of an SVG <c>tspan</c>: the baseline of
    /// the span and of the spans that follow it moves down by this distance. On a path, the shift is across the path. The
    /// default is 0; a value that is not finite is 0.
    /// </summary>
    public double Dy { get; init; }

    /// <summary>
    /// Gets the rotation of each character of the span around its origin on the baseline, in degrees, clockwise, as a
    /// single value of the <c>rotate</c> attribute of an SVG <c>tspan</c>. The characters keep their advances. The default
    /// is 0; a value that is not finite is 0.
    /// </summary>
    /// <remarks>The characters of a rotated span are shaped one grapheme cluster at a time: no kerning applies between them.</remarks>
    public double GlyphRotation { get; init; }
}
