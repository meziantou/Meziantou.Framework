namespace Meziantou.Framework.Imaging.Typography;

/// <summary>Builds a <see cref="RichText"/>, one span after the other.</summary>
/// <remarks>A builder is not thread-safe. It can be reused after <see cref="Build"/>, and emptied with <see cref="Clear"/>.</remarks>
public sealed class RichTextBuilder
{
    private readonly StringBuilder _text = new();
    private readonly List<TextSpan> _spans = [];

    /// <summary>Adds a text with a style.</summary>
    /// <param name="text">The text; an empty text adds nothing.</param>
    /// <param name="style">The style of the text.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> or <paramref name="style"/> is <see langword="null"/>.</exception>
    public RichTextBuilder Append(string text, TextStyle style)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(style);
        if (text.Length == 0)
            return this;

        // Two texts of the same style that follow each other are one span.
        if (_spans.Count > 0 && !_spans[^1].IsPlaceholder && ReferenceEquals(_spans[^1].Style, style))
        {
            _spans[^1] = new TextSpan(_spans[^1].Start, _spans[^1].Length + text.Length, style);
        }
        else
        {
            _spans.Add(new TextSpan(_text.Length, text.Length, style));
        }

        _text.Append(text);
        return this;
    }

    /// <summary>
    /// Adds a placeholder: room in the line for something the text does not draw, such as an icon or a badge, which
    /// moves with the text around it when lines are wrapped and aligned.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The placeholder is one character of the text (U+FFFC, the object replacement character): it has an index, a
    /// caret can be before or after it, and a line can end before or after it. It takes
    /// <paramref name="width"/> on its line, and its line is at least as tall as it needs: the placeholder rises
    /// <paramref name="baseline"/> above the baseline of the line, and goes <paramref name="height"/> less
    /// <paramref name="baseline"/> below it. A placeholder as tall as its baseline stands on the baseline, as a
    /// capital letter does.
    /// </para>
    /// <para>
    /// Nothing is drawn there: <see cref="TextLayout.Placeholders"/> gives the rectangle of each placeholder of a
    /// layout, for the caller to draw in after the text. The style gives the direction and the decorations the
    /// placeholder has among its neighbors, and its line the height of its fonts.
    /// </para>
    /// <para>
    /// In a vertical layout (<see cref="TextLayoutOptions.WritingMode"/>), the placeholder takes
    /// <paramref name="height"/> down its line and is centered on the line, which is at least as wide as
    /// <paramref name="width"/>: <paramref name="baseline"/> is not used. With
    /// <see cref="TextOrientation.Sideways"/>, the placeholder is turned with its line as the text is: its rectangle
    /// is <paramref name="height"/> wide and <paramref name="width"/> tall.
    /// </para>
    /// </remarks>
    /// <param name="width">The width of the placeholder, in pixels: zero or positive, up to 65536.</param>
    /// <param name="height">The height of the placeholder, in pixels: zero or positive, up to 65536.</param>
    /// <param name="baseline">The distance from the top of the placeholder down to the baseline of its line, in pixels: <paramref name="height"/> to stand on the baseline. Its magnitude must not exceed 65536.</param>
    /// <param name="style">The style of the placeholder: the one of the text around it, as a rule.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="style"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="width"/> or <paramref name="height"/> is not between 0 and 65536, or <paramref name="baseline"/> is not between -65536 and 65536.</exception>
    public RichTextBuilder AppendPlaceholder(double width, double height, double baseline, TextStyle style)
    {
        ArgumentNullException.ThrowIfNull(style);

        // Written so that a value that is not a number is rejected too
        if (!(width >= 0 && width <= Font.MaxSize))
            throw new ArgumentOutOfRangeException(nameof(width), width, "The width must be between 0 and 65536.");

        if (!(height >= 0 && height <= Font.MaxSize))
            throw new ArgumentOutOfRangeException(nameof(height), height, "The height must be between 0 and 65536.");

        if (!(Math.Abs(baseline) <= Font.MaxSize))
            throw new ArgumentOutOfRangeException(nameof(baseline), baseline, "The baseline must be between -65536 and 65536.");

        _spans.Add(new TextSpan(_text.Length, style, width, height, baseline));
        _text.Append('\uFFFC');
        return this;
    }

    /// <summary>Creates the text with the spans added so far.</summary>
    /// <returns>The text; it does not change when the builder does.</returns>
    public RichText Build() => new(_text.ToString(), [.. _spans]);

    /// <summary>Removes every span.</summary>
    public void Clear()
    {
        _text.Clear();
        _spans.Clear();
    }
}
