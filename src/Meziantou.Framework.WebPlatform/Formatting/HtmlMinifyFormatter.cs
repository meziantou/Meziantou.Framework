using System.Collections.Frozen;

namespace Meziantou.Framework.WebPlatform.Formatting;

/// <summary>
/// Writes a minified HTML markup: the comments, the collapsible whitespace, the optional tags and the unneeded attribute
/// quotes are removed, and only the characters that must be escaped are escaped. The document is not modified.
/// </summary>
/// <remarks>
/// <para>
/// Only the HTML is minified: the content of the <c>script</c> elements is written as is, and so are the content of the
/// <c>style</c> elements and the value of the <c>style</c> attributes, unless <see cref="HtmlFormatter.StyleElementFormatter"/>
/// or <see cref="HtmlFormatter.StyleAttributeFormatter"/> is set (e.g. to a <see cref="CssMinifyFormatter"/>).
/// </para>
/// <para>
/// The whitespace that is removed is determined from the computed styles of the elements (<c>display</c>,
/// <c>white-space-collapse</c> and the <c>content</c> of the <c>::before</c> and <c>::after</c> pseudo-elements), computed
/// from the user agent style sheet and the style sheets of the document. Every media query is considered to match, so the
/// whitespace that is significant in any environment (screen size, print, color scheme...) is kept.
/// </para>
/// </remarks>
public sealed partial class HtmlMinifyFormatter : HtmlFormatter
{
    private readonly FrozenSet<string> _preservedStartTags = FrozenSet<string>.Empty.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>Whether the comments are removed. The default is true.</summary>
    public bool RemoveComments { get; init; } = true;

    /// <summary>
    /// Whether the collapsible whitespace is collapsed: a sequence of ASCII whitespace is replaced by a single space, and
    /// the spaces that are not rendered (at the start or the end of a line, or following another space) are removed. The
    /// text whose whitespace is preserved (e.g. in <c>pre</c> or <c>textarea</c>) is not modified, and the whitespace of
    /// the <c>class</c> attributes is collapsed. The default is true.
    /// </summary>
    public bool CollapseWhitespace { get; init; } = true;

    /// <summary>
    /// Whether the spaces and tabs at the end of the lines of the preformatted text (<c>white-space-collapse: preserve</c>,
    /// e.g. in <c>pre</c>) are removed, as well as the trailing whitespace of the preformatted elements. The default is true.
    /// </summary>
    public bool TrimTrailingWhitespaceInPreformattedText { get; init; } = true;

    /// <summary>
    /// Whether the start and end tags that can be omitted without changing the document are omitted
    /// (https://html.spec.whatwg.org/multipage/syntax.html#optional-tags). The default is true.
    /// </summary>
    public bool OmitOptionalTags { get; init; } = true;

    /// <summary>
    /// The local names of the HTML elements whose start tag is always written, even when it can be omitted (e.g. <c>html</c>
    /// and <c>head</c>, for the tools that expect them). The names are compared ASCII case-insensitively. Empty by default.
    /// </summary>
    public IReadOnlyCollection<string> PreservedStartTags
    {
        get => _preservedStartTags;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            _preservedStartTags = value.ToFrozenSet(StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Whether the quotes of the attribute values are removed when the value does not need them, a single quote is used
    /// when it avoids escaping double quotes, and the empty values are omitted (<c>&lt;script defer&gt;</c>). The default is true.
    /// </summary>
    public bool RemoveAttributeQuotes { get; init; } = true;

    /// <summary>
    /// Whether the <c>type</c> attribute of the <c>script</c> elements is removed when it denotes a classic script (an empty
    /// value or a JavaScript MIME type such as <c>text/javascript</c>). The default is true.
    /// </summary>
    public bool RemoveScriptTypeAttribute { get; init; } = true;

    /// <summary>Creates a writer that emits minified HTML to the given output.</summary>
    protected override HtmlMarkupWriter CreateWriter(TextWriter output) => new MinifyWriter(this, output);

    private bool IsPreservedStartTag(string localName) => _preservedStartTags.Count > 0 && _preservedStartTags.Contains(localName);
}
