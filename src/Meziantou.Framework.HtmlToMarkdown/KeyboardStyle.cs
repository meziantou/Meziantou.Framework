namespace Meziantou.Framework;

/// <summary>Specifies how keyboard input (<c>kbd</c>) elements are converted.</summary>
public enum KeyboardStyle
{
    /// <summary>Handle the element as an unknown element, as set by <see cref="HtmlToMarkdownOptions.UnknownElementHandling"/>.</summary>
    UnknownElement,

    /// <summary>Write the element as an inline HTML tag, <c>&lt;kbd&gt;text&lt;/kbd&gt;</c>, around its converted content.</summary>
    Html,

    /// <summary>Write the text of the element as a code span.</summary>
    CodeSpan,

    /// <summary>Write the content without any markup.</summary>
    Text,
}
