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
        if (_spans.Count > 0 && ReferenceEquals(_spans[^1].Style, style))
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
