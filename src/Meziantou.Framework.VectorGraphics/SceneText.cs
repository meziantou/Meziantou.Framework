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

    /// <summary>
    /// Gets or sets the path the text follows, as an SVG <c>textPath</c>, or <see langword="null"/> for a straight text
    /// on its baseline. With a path, <see cref="X"/>, <see cref="Y"/> and <see cref="Rotation"/> are ignored.
    /// </summary>
    /// <remarks>
    /// The baseline of the text is on the path, and each character is turned to the direction of the path at the middle
    /// of its advance. The text starts at <see cref="PathStartOffset"/> along the path, where <see cref="Anchor"/> places
    /// it: its start, its middle or its end is there. The characters whose middle is beyond the ends of the path are not
    /// drawn. The subpaths are followed one after the other.
    /// </remarks>
    public PathData? Path { get; set; }

    /// <summary>Gets or sets the distance along <see cref="Path"/> at which the text is anchored, in pixels. The default is 0.</summary>
    public double PathStartOffset { get; set; }

    /// <summary>Gets or sets the advance width of the text, as the layout measured it.</summary>
    public double MeasuredWidth { get; set; }

    /// <summary>
    /// Gets or sets how the glyphs are painted, or <see langword="null"/> (the default) to paint them with
    /// <see cref="Color"/>. A gradient or a pattern is in the coordinates of the text, as for a shape.
    /// </summary>
    /// <remarks>To draw the outline of the glyphs only, set a <see cref="Stroke"/> and a transparent fill.</remarks>
    public ScenePaint? Fill { get; set; }

    /// <summary>Gets or sets how the outline of the glyphs is painted, or <see langword="null"/> (the default) for no outline.</summary>
    /// <remarks>The outline is centered on the edges of the glyphs: it covers half its width of the fill, unless <see cref="PaintOrder"/> paints it first.</remarks>
    public ScenePaint? Stroke { get; set; }

    /// <summary>Gets or sets the width of the outline of the glyphs. The default is 1.</summary>
    public double StrokeWidth { get; set; } = 1;

    /// <summary>Gets or sets the lengths of the dashes and gaps of the outline of the glyphs, or <see langword="null"/> for a solid outline.</summary>
    public IReadOnlyList<double>? StrokeDashArray { get; set; }

    /// <summary>
    /// Gets or sets the distance into <see cref="StrokeDashArray"/> at which the dashes of the outline start. A negative
    /// distance starts before the pattern, and a value that is not finite is 0.
    /// </summary>
    public double StrokeDashOffset { get; set; }

    /// <summary>
    /// Gets or sets the limit of <see cref="SceneLineJoin.Miter"/> joins of the outline of the glyphs. The default is 4,
    /// as in SVG; a value less than 1 or not finite is 4.
    /// </summary>
    public double MiterLimit { get; set; } = 4;

    /// <summary>Gets or sets the shape of the ends of the open outlines of the glyphs.</summary>
    public SceneLineCap LineCap { get; set; }

    /// <summary>Gets or sets the shape of the corners of the outline of the glyphs.</summary>
    public SceneLineJoin LineJoin { get; set; }

    /// <summary>
    /// Gets or sets the order in which the fill and the outline of the glyphs are painted, as the SVG <c>paint-order</c>
    /// property. A text has no markers: only whether the stroke comes before the fill matters. The default paints the
    /// fill first.
    /// </summary>
    public ScenePaintOrder PaintOrder { get; set; }

    /// <summary>
    /// Gets or sets how the lines of the text (<see cref="Decorations"/>) are painted, or <see langword="null"/> (the
    /// default) to paint them as the glyphs of each span.
    /// </summary>
    /// <remarks>
    /// SVG paints the lines with the fill of the element that declares them: a text whose spans have their own colors has
    /// lines of the color of the text. Set this paint to draw the lines that way.
    /// </remarks>
    public ScenePaint? DecorationFill { get; set; }

    /// <summary>
    /// Gets or sets the baseline that is on <see cref="Y"/>, as the SVG <c>dominant-baseline</c> property. The default is
    /// the alphabetic baseline. Each span is aligned by the baseline of its own font.
    /// </summary>
    public SceneTextBaseline Baseline { get; set; }

    /// <summary>
    /// Gets or sets the advance the text is stretched or compressed to, in pixels, as the SVG <c>textLength</c>
    /// attribute, or <see langword="null"/> (the default) for the natural advance of its glyphs.
    /// </summary>
    /// <remarks>
    /// The length is the sum of the advances of all the spans, without their shifts (<see cref="SceneTextSpan.Dx"/>):
    /// with <see cref="SceneTextLengthAdjust.Spacing"/>, the difference is spread over the gaps between the grapheme
    /// clusters (none after the last one); with <see cref="SceneTextLengthAdjust.SpacingAndGlyphs"/>, every span is scaled
    /// horizontally from its start. Each text chunk (see <see cref="SceneTextSpan.X"/>) is then anchored with its new
    /// advance. On a path, the glyphs are not scaled: the spacing changes. A value that is negative or not finite is
    /// ignored, as SVG ignores it.
    /// </remarks>
    public double? TextLength { get; set; }

    /// <summary>Gets or sets how the text is fitted to <see cref="TextLength"/>. The default changes the spacing only.</summary>
    public SceneTextLengthAdjust LengthAdjust { get; set; }

    /// <summary>
    /// Gets or sets the direction the text is written in, as the SVG <c>direction</c> property. The default is from left
    /// to right.
    /// </summary>
    /// <remarks>
    /// From right to left, <see cref="SceneTextAnchor.Start"/> is the right end of the text, the spans follow each other
    /// to the left, and the characters of each span are ordered by the Unicode Bidirectional Algorithm with a
    /// right-to-left paragraph. From left to right, the paragraph of each span has the direction of its first strong
    /// character. The algorithm does not reorder the spans themselves.
    /// </remarks>
    public SceneTextDirection Direction { get; set; }

    private static string Concat(IEnumerable<SceneTextSpan> spans, out SceneTextSpan[] list)
    {
        ArgumentNullException.ThrowIfNull(spans);
        list = [.. spans];
        if (Array.IndexOf(list, null) >= 0)
            throw new ArgumentException("The spans cannot contain null.", nameof(spans));

        return string.Concat(list.Select(span => span.Text));
    }
}
