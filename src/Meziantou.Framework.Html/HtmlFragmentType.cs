namespace Meziantou.Framework.Html;

/// <summary>
/// Specifies the type of HTML fragment encountered during parsing.
/// </summary>
// NOTE: Keep in sync with HtmlParserState
#if HTML_PUBLIC
public
#else
internal
#endif
enum HtmlFragmentType
{
    /// <summary>Plain text content.</summary>
    Text,

    /// <summary>An opening tag (&lt;).</summary>
    TagOpen,

    /// <summary>The end of a tag (&gt;).</summary>
    TagEnd,

    /// <summary>A self-closing tag end (/&gt;).</summary>
    TagEndClose,

    /// <summary>A closing tag (&lt;/tagname).</summary>
    TagClose,

    /// <summary>An attribute name.</summary>
    AttName,

    /// <summary>An attribute value.</summary>
    AttValue,

    /// <summary>An HTML comment (&lt;!-- comment --&gt;).</summary>
    Comment,

    /// <summary>CDATA section text (&lt;![CDATA[ ... ]]&gt;).</summary>
    CDataText,
}
