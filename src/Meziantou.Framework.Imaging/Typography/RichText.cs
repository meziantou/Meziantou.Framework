using System.Collections.ObjectModel;

namespace Meziantou.Framework.Imaging.Typography;

/// <summary>A text whose parts have their own style: fonts, size, paint, decorations. Built by <see cref="RichTextBuilder"/>.</summary>
/// <remarks>
/// <para>
/// The spans follow each other and cover the text. A span is not a paragraph and not a line: letters of two spans can
/// kern, join and form a ligature when their spans only differ by what is painted (brush, decorations).
/// </para>
/// <para>A rich text is immutable.</para>
/// </remarks>
public sealed class RichText
{
    private readonly TextSpan[] _spans;

    internal RichText(string text, TextSpan[] spans)
    {
        Text = text;
        _spans = spans;
        Spans = new ReadOnlyCollection<TextSpan>(spans);
    }

    /// <summary>Gets the whole text.</summary>
    public string Text { get; }

    /// <summary>Gets the spans, in the order of the text.</summary>
    public IReadOnlyList<TextSpan> Spans { get; }

    internal ReadOnlySpan<TextSpan> SpanList => _spans;
}
