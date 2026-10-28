using Meziantou.Framework.Markdown.Extensions.GenericAttributes;
using Meziantou.Framework.Markdown.Syntax;

namespace Meziantou.Framework.Markdown.Renderers.Roundtrip;

/// <summary>
/// The changed attributes of an object, while the roundtrip renderer writes it.
/// </summary>
internal sealed class ChangedAttributes(MarkdownObject obj, AttributesTrivia? trivia, string? text)
{
    public MarkdownObject Object { get; } = obj;

    public AttributesTrivia? Trivia { get; } = trivia;

    /// <summary>
    /// Gets the attributes, as written in Markdown, or <see langword="null"/> when there are none to write.
    /// </summary>
    public string? Text { get; } = text;

    public bool IsWritten { get; set; } = text is null && trivia?.HasSourceText is not true;

    /// <summary>
    /// Gets or sets the text of the leaf block before an inline, without the indent of the containers.
    /// </summary>
    public string? TextBefore { get; set; }

    /// <summary>
    /// Gets whether the attributes still have to be written, at the place the object has for them as it has none in the text.
    /// </summary>
    public bool NeedsPlace => !IsWritten && Text is not null && Trivia?.HasSourceText is not true;

    /// <summary>
    /// Returns the attributes to write in place of attributes of the text: the first ones get the changed attributes, and the
    /// others none.
    /// </summary>
    public string? Take()
    {
        if (IsWritten)
        {
            return null;
        }

        IsWritten = true;
        return Text;
    }
}
