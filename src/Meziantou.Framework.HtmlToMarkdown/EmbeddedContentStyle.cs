namespace Meziantou.Framework;

/// <summary>Specifies how embedded content (<c>video</c>, <c>audio</c> and <c>iframe</c> elements) is converted.</summary>
public enum EmbeddedContentStyle
{
    /// <summary>Handle the element as an unknown element, as set by <see cref="HtmlToMarkdownOptions.UnknownElementHandling"/>.</summary>
    UnknownElement,

    /// <summary>Write the element as raw HTML.</summary>
    Html,

    /// <summary>
    /// Write a link to the URL of the element: its <c>src</c> attribute, or the first <c>source</c> child of a <c>video</c>
    /// or an <c>audio</c>. The text of the link is the <c>title</c> attribute, or the URL. An element without a URL is removed.
    /// </summary>
    Link,

    /// <summary>Remove the element and its content.</summary>
    Remove,
}
