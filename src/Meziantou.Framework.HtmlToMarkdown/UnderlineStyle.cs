namespace Meziantou.Framework;

/// <summary>Specifies how underline (<c>u</c>) elements are converted.</summary>
public enum UnderlineStyle
{
    /// <summary>Handle the element as an unknown element, as set by <see cref="HtmlToMarkdownOptions.UnknownElementHandling"/>.</summary>
    UnknownElement,

    /// <summary>Write the element as an inline HTML tag, <c>&lt;u&gt;text&lt;/u&gt;</c>, around its converted content.</summary>
    Html,

    /// <summary>Write the content between two plus signs, <c>++text++</c>, as markdown-it-ins does.</summary>
    PlusSigns,

    /// <summary>Write the content without any markup.</summary>
    Text,
}
