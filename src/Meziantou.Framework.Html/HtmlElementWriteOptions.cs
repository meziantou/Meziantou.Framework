namespace Meziantou.Framework.Html;

/// <summary>
/// Specifies options that control how HTML elements are written.
/// </summary>
[Flags]
#if HTML_PUBLIC
public
#else
internal
#endif
enum HtmlElementWriteOptions
{
    /// <summary>No special write options.</summary>
    None = 0x0,

    /// <summary>Don't write a closing tag if the element is empty (e.g., &lt;meta&gt; instead of &lt;meta /&gt;).</summary>
    DontCloseIfEmpty = 0x1,

  /// <summary>Always write a closing tag, even if the element is empty (e.g., &lt;div&gt;&lt;/div&gt; instead of &lt;div /&gt;).</summary>
    AlwaysClose = 0x2,

    /// <summary>The element cannot have child elements.</summary>
    NoChild = 0x4,
}
