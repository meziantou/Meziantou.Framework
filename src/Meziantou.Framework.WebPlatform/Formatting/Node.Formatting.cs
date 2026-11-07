using Meziantou.Framework.WebPlatform.Formatting;

namespace Meziantou.Framework.WebPlatform.Dom;

public partial class Node
{
    /// <summary>
    /// Returns the markup of the node, formatted by <paramref name="formatter"/> (<see cref="HtmlFormatter.Default"/> when
    /// null): an element with its start tag, its descendants and its end tag (as <see cref="Element.OuterHtml"/>), the
    /// children of a document or a document fragment.
    /// </summary>
    [NotInIdl]
    public string ToHtml(HtmlFormatter? formatter = null) => (formatter ?? HtmlFormatter.Default).Format(this);

    /// <summary>Same as <see cref="ToHtml(HtmlFormatter?)"/>, but writes the markup to <paramref name="output"/> instead of building a string.</summary>
    [NotInIdl]
    public void ToHtml(TextWriter output, HtmlFormatter? formatter = null) => (formatter ?? HtmlFormatter.Default).Format(this, output);
}
