namespace Meziantou.Framework.Html;

/// <summary>Specifies the type of an HTML node.</summary>
#if HTML_PUBLIC
public
#else
internal
#endif
enum HtmlNodeType
{
    /// <summary>An attribute node.</summary>
    Attribute,

    /// <summary>A comment node (&lt;!-- comment --&gt;).</summary>
    Comment,

    /// <summary>The document node.</summary>
    Document,

    /// <summary>An element node (e.g., &lt;div&gt;, &lt;span&gt;).</summary>
    Element,

    /// <summary>An end element node (deprecated/unused).</summary>
    EndElement,

    /// <summary>A text node containing character data.</summary>
    Text,

    /// <summary>No node type specified.</summary>
    None,

    /// <summary>A processing instruction node (&lt;?xml ?&gt;).</summary>
    ProcessingInstruction,

    /// <summary>A document type declaration node (&lt;!DOCTYPE&gt;).</summary>
    DocumentType,

    /// <summary>An XPath query result node.</summary>
    XPathResult,
}
