namespace Meziantou.Framework.Html;

/// <summary>Specifies the type of HTML parsing error.</summary>
#if HTML_PUBLIC
public
#else
internal
#endif
enum HtmlErrorType
{
    /// <summary>An opening tag was not closed.</summary>
    TagNotClosed,

    /// <summary>A closing tag was found without a corresponding opening tag.</summary>
    TagNotOpened,

    /// <summary>A text encoding error occurred while reading the HTML.</summary>
    EncodingError,

    /// <summary>The detected encoding does not match the declared encoding.</summary>
    EncodingMismatch,

    /// <summary>A namespace was referenced but not declared.</summary>
    NamespaceNotDeclared,

    /// <summary>An attribute was declared more than once on an element.</summary>
    DuplicateAttribute,
}
