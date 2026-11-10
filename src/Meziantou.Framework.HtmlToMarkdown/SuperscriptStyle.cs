namespace Meziantou.Framework;

/// <summary>Specifies how superscript (<c>sup</c>) elements are converted.</summary>
public enum SuperscriptStyle
{
    /// <summary>Handle the element as an unknown element, as set by <see cref="HtmlToMarkdownOptions.UnknownElementHandling"/>.</summary>
    UnknownElement,

    /// <summary>Write the element as an inline HTML tag, <c>&lt;sup&gt;text&lt;/sup&gt;</c>, around its converted content.</summary>
    Html,

    /// <summary>Write the content between carets, <c>^text^</c>, as Pandoc and markdown-it-sup do.</summary>
    Caret,

    /// <summary>Write the content without any markup.</summary>
    Text,
}
