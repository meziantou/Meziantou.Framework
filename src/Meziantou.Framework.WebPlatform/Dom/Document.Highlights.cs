using Meziantou.Framework.WebPlatform.Css.Om;

namespace Meziantou.Framework.WebPlatform.Dom;

public partial class Document
{
    private HighlightRegistry? _highlights;

    /// <summary>
    /// The custom highlights registered for the document (https://drafts.csswg.org/css-highlight-api-1/#highlight-registry),
    /// returned by <c>CSS.highlights</c> in the scripts of its window.
    /// </summary>
    [NotInIdl("The highlight registry of the document, exposed to the scripts by CSS.highlights")]
    public HighlightRegistry Highlights => _highlights ??= new HighlightRegistry(this);

    /// <summary>Whether the highlight registry of the document has highlights (without creating it).</summary>
    internal bool HasHighlights => _highlights is { Count: > 0 };
}
