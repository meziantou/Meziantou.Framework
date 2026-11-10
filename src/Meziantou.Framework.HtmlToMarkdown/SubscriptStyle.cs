namespace Meziantou.Framework;

/// <summary>Specifies how subscript (<c>sub</c>) elements are converted.</summary>
public enum SubscriptStyle
{
    /// <summary>Handle the element as an unknown element, as set by <see cref="HtmlToMarkdownOptions.UnknownElementHandling"/>.</summary>
    UnknownElement,

    /// <summary>Write the element as an inline HTML tag, <c>&lt;sub&gt;text&lt;/sub&gt;</c>, around its converted content.</summary>
    Html,

    /// <summary>Write the content between tildes, <c>~text~</c>, as Pandoc and markdown-it-sub do.</summary>
    Tilde,

    /// <summary>Write the content without any markup.</summary>
    Text,
}
