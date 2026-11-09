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
        if (_spans.Count > 0 && !_spans[^1].IsPlaceholder && _spans[^1].RubyText is null && ReferenceEquals(_spans[^1].Style, style))
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

    /// <summary>
    /// Adds a ruby: a text, the base, with an annotation set beside it, such as the reading of Japanese kanji in kana
    /// (furigana), or a pronunciation.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The base is part of the text, with its style: it is shaped, measured and hit like the rest of the text, but a
    /// line never ends inside it. The annotation is not part of the text: it is laid out on its own, with
    /// <paramref name="annotationStyle"/> (usually half the size of the base), and drawn with the glyphs of the layout,
    /// centered on the base. A base shorter than its annotation gets the difference as room, half before it and half
    /// after it, so that the annotation does not run over the text around it. The line is as tall as it needs for the
    /// annotation to stand on the ascent of the font of the base.
    /// </para>
    /// <para>
    /// In a horizontal layout, the annotation is above the base. In a vertical layout
    /// (<see cref="TextLayoutOptions.WritingMode"/>), it is set in the same writing mode and orientation as the text, in
    /// a column on the right of the base, centered along it: the side of Japanese and Chinese vertical text. The
    /// annotation is not part of the positions of the text (<see cref="TextLayout.HitTest"/>, the carets and the
    /// selections), and is not hyphenated, wrapped or justified.
    /// </para>
    /// </remarks>
    /// <param name="text">The base: a text that is not empty.</param>
    /// <param name="style">The style of the base.</param>
    /// <param name="annotation">The annotation; an empty one adds a base that a line never ends inside, with nothing beside it.</param>
    /// <param name="annotationStyle">The style of the annotation.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="text"/>, <paramref name="style"/>, <paramref name="annotation"/> or <paramref name="annotationStyle"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="text"/> is empty.</exception>
    /// <example>
    /// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../../samples/Meziantou.Framework.Imaging.Samples/TextExamples.cs" ref:region="Imaging_DrawRuby" ref:dedent="true">
    /// public static void DrawRuby(string fontPath, string outputPath)
    /// {
    ///     var font = Font.Load(fontPath);
    ///     var text = new TextStyle(font, size: 28);
    ///     var reading = new TextStyle(font, size: 14);
    ///
    ///     // The base is part of the text; the annotation is set beside it, at half the size, and a line never ends inside the base
    ///     var rubyText = new RichTextBuilder()
    ///         .AppendRuby("東京", text, "とうきょう", reading)
    ///         .Append("の", text)
    ///         .AppendRuby("日本語", text, "にほんご", reading)
    ///         .Build();
    ///
    ///     // Above the base in a horizontal layout, on its right in a vertical one
    ///     var horizontal = TextLayout.Create(rubyText);
    ///     var vertical = TextLayout.Create(rubyText, new TextLayoutOptions { WritingMode = TextWritingMode.VerticalRightToLeft });
    ///
    ///     var width = (int)Math.Ceiling(horizontal.Width + vertical.Width) + 40;
    ///     var height = (int)Math.Ceiling(Math.Max(horizontal.Height, vertical.Height)) + 20;
    ///     using var image = new Image&lt;Rgba32&gt;(width, height, new Rgba32(255, 255, 255));
    ///     image.Draw(canvas =&gt;
    ///     {
    ///         canvas.FillText(new Rgba32(0, 0, 0), horizontal, new PointD(10, 10));
    ///         canvas.FillText(new Rgba32(0, 0, 0), vertical, new PointD(horizontal.Width + 30, 10));
    ///     });
    ///     image.Save(outputPath);
    /// }
    /// </code>
    /// </example>
    public RichTextBuilder AppendRuby(string text, TextStyle style, string annotation, TextStyle annotationStyle)
    {
        ArgumentException.ThrowIfNullOrEmpty(text);
        ArgumentNullException.ThrowIfNull(style);
        ArgumentNullException.ThrowIfNull(annotation);
        ArgumentNullException.ThrowIfNull(annotationStyle);
        _spans.Add(new TextSpan(_text.Length, text.Length, style, annotation, annotationStyle));
        _text.Append(text);
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
