namespace Meziantou.Framework.VectorGraphics;

/// <summary>A line of text, positioned by its baseline.</summary>
/// <param name="x">The x coordinate of the anchor.</param>
/// <param name="y">The y coordinate of the baseline.</param>
/// <param name="text">The text.</param>
/// <param name="font">The size and the weight of the font.</param>
/// <param name="color">The color of the text.</param>
public sealed class SceneText(double x, double y, string text, SceneFont font, SceneColor color) : SceneElement
{
    /// <summary>Initializes a new instance of the <see cref="SceneText"/> class whose parts have their own styles.</summary>
    /// <param name="x">The x coordinate of the anchor.</param>
    /// <param name="y">The y coordinate of the baseline.</param>
    /// <param name="spans">The parts of the text, drawn one after the other.</param>
    /// <param name="font">The font of the spans that set none.</param>
    /// <param name="color">The color of the spans that set none.</param>
    /// <exception cref="ArgumentException"><paramref name="spans"/> contains <see langword="null"/>.</exception>
    public SceneText(double x, double y, IEnumerable<SceneTextSpan> spans, SceneFont font, SceneColor color)
        : this(x, y, Concat(spans, out var list), font, color)
    {
        Spans = list;
    }

    /// <summary>Gets the x coordinate of the anchor.</summary>
    public double X { get; } = x;

    /// <summary>Gets the y coordinate of the baseline.</summary>
    public double Y { get; } = y;

    /// <summary>Gets the text.</summary>
    public string Text { get; } = text ?? throw new ArgumentNullException(nameof(text));

    /// <summary>Gets the size and the weight of the font.</summary>
    public SceneFont Font { get; } = font;

    /// <summary>Gets the color of the text.</summary>
    public SceneColor Color { get; } = color;

    /// <summary>Gets or sets where the text is anchored horizontally.</summary>
    public SceneTextAnchor Anchor { get; set; }

    /// <summary>Gets or sets the rotation of the text around its anchor, in degrees, clockwise.</summary>
    public double Rotation { get; set; }

    /// <summary>Gets or sets the lines drawn with the text: an underline, a strikethrough, an overline, or several of them. They have the color of the text, and the position and the thickness the font gives them.</summary>
    public SceneTextDecorations Decorations { get; set; }

    /// <summary>
    /// Gets or sets the space added after each character, in pixels, as the CSS <c>letter-spacing</c>; negative to
    /// tighten. The default is 0.
    /// </summary>
    /// <remarks>
    /// The space is added after each grapheme cluster (a character with its marks), the last one included, and the
    /// optional ligatures are not formed when it is not 0. A value that is not finite is 0.
    /// </remarks>
    public double LetterSpacing { get; set; }

    /// <summary>
    /// Gets or sets the space added to each space (U+0020 and U+00A0), in pixels, as the CSS <c>word-spacing</c>;
    /// negative to tighten. The default is 0.
    /// </summary>
    /// <remarks>A value that is not finite is 0.</remarks>
    public double WordSpacing { get; set; }

    /// <summary>
    /// Gets the parts of the text that have their own styles, or an empty list when the whole text has the style of the
    /// text. <see cref="Text"/> is the text of the spans, one after the other.
    /// </summary>
    public IReadOnlyList<SceneTextSpan> Spans { get; } = [];

    /// <summary>Gets or sets the advance width of the text, as the layout measured it.</summary>
    public double MeasuredWidth { get; set; }

    private static string Concat(IEnumerable<SceneTextSpan> spans, out SceneTextSpan[] list)
    {
        ArgumentNullException.ThrowIfNull(spans);
        list = [.. spans];
        if (Array.IndexOf(list, null) >= 0)
            throw new ArgumentException("The spans cannot contain null.", nameof(spans));

        return string.Concat(list.Select(span => span.Text));
    }
}
